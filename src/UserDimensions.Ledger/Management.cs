using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;

namespace UserDimensions.Ledger;

/// <summary>The finite Phase 1 CLI contract; also callable in-process without starting SQLite.</summary>
public static class Management
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        try { var data=Execute(args); stdout.Write(JsonSerializer.Serialize(new { ok=true,data,error=(object?)null })+"\n"); return 0; }
        catch(Exception exception)
        {
            var e=exception as LedgerException ?? (exception is IOException or UnauthorizedAccessException
                ? new LedgerException("IO_ERROR",6,"Filesystem operation failed: "+exception.Message)
                : new LedgerException("INTERNAL_ERROR",6,"Internal operation failed."));
            var details=e.Details is ImportInterruption receipt ? new { ledger_id_hex=receipt.Disposition.Block.LedgerId,block_guid=receipt.Disposition.Block.Id,sha512=receipt.Disposition.Block.Hash,durable=true,reason=receipt.Cause } : e.Details;
            stdout.Write(JsonSerializer.Serialize(new { ok=false,data=(object?)null,error=new { code=e.Code,message=e.Message,retryable=e.Code is "IO_ERROR" or "DUPLICATE_INSTANCE" or "AUTHORITY_UNRESOLVED",details } })+"\n");
            stderr.WriteLine(e.Code+": "+e.Message); return e.ExitCode;
        }
    }
    private sealed class Arguments
    {
        public string Root { get; }
        public string Command { get; }
        private readonly Dictionary<string,string> options=new(StringComparer.Ordinal);
        public Arguments(string[] args)
        {
            Errors.Require(args.Length>=3 && args[0]=="--instance-root","INVALID_ARGUMENT",2,"Use ledgerctl --instance-root ABS COMMAND."); Root=Paths.Absolute(args[1]);
            var position=3; Command=args[2]; if(Command!="status") { Errors.Require(args.Length>=4 && !args[3].StartsWith("--",StringComparison.Ordinal),"INVALID_ARGUMENT",2,"Missing subcommand."); Command+="."+args[3]; position=4; }
            var allowed=Command switch {
                "instance.init" or "instance.status" or "ledger.init" or "status" or "config.get" => Array.Empty<string>(),
                "instance.recover" => ["from","confirm-unbound-initialization"], "ledger.join" => ["ledger-id","genesis-sha512","genesis-file"], "ledger.recover-genesis" => ["ledger-id","genesis-sha512"],
                "key.backup" or "key.restore" => ["directory"], "block.publish" => ["event-file","block-guid","revocation-consent-file"],
                "block.import" => ["input"], "block.get" => ["id","output","ledger-id","sha512"],
                "block.export" => ["output","ledger-id","id","sha512","from-timestamp-ms","to-timestamp-ms"],
                "master.countersign-revocation" => ["outer-signer-public-key","revoked-public-key","output"],
                "config.set" => ["name","value"], _ => throw new LedgerException("INVALID_ARGUMENT",2,"Unknown Phase 1 command.") };
            while(position<args.Length)
            {
                var flag=args[position++]; Errors.Require(flag.StartsWith("--",StringComparison.Ordinal) && allowed.Contains(flag[2..],StringComparer.Ordinal),"INVALID_ARGUMENT",2,"Unknown option.");
                var name=flag[2..]; string value;
                if(name=="confirm-unbound-initialization") value="true";
                else { Errors.Require(position<args.Length && !args[position].StartsWith("--",StringComparison.Ordinal),"INVALID_ARGUMENT",2,"Missing option value."); value=args[position++]; }
                Errors.Require(options.TryAdd(name,value),"INVALID_ARGUMENT",2,"Duplicate option.");
            }
            string[] required=Command switch {
                "instance.recover" => ["from"], "ledger.join" => ["ledger-id","genesis-sha512","genesis-file"], "ledger.recover-genesis" => ["ledger-id","genesis-sha512"],
                "key.backup" or "key.restore" => ["directory"], "block.publish" => [], "block.import" => ["input"],
                "block.get" => ["id","output"], "block.export" => ["output"], "config.set" => ["name","value"],
                "master.countersign-revocation" => allowed, _ => [] };
            foreach(var r in required) Need(r);
            foreach(var pair in options)
            {
                if(pair.Key is "from" or "directory" or "event-file" or "genesis-file" or "revocation-consent-file" or "input" or "output") Paths.Absolute(pair.Value);
                if(pair.Key is "id" or "block-guid") StrictJson.Uuid(pair.Value,"INVALID_ARGUMENT");
                if(pair.Key is "sha512" or "genesis-sha512") StrictJson.Hex(pair.Value,64,"INVALID_ARGUMENT");
                if(pair.Key is "outer-signer-public-key" or "revoked-public-key" or "ledger-id") StrictJson.Hex(pair.Value,32,"INVALID_ARGUMENT");
                if(pair.Key is "from-timestamp-ms" or "to-timestamp-ms" or "value") Number(pair.Key);
            }
            if(Command=="block.publish") Errors.Require((Get("event-file")!=null) != (Get("revocation-consent-file")!=null) && (Get("revocation-consent-file")==null || Get("block-guid")==null),"INVALID_ARGUMENT",2,"Choose ordinary event-file with optional GUID, or revocation-consent-file alone.");
            if(Command=="config.set") Errors.Require(new[]{"max_raw_block_bytes","max_decompressed_event_bytes","max_inflight_import_bytes","per_block_timeout_seconds"}.Contains(Need("name")),"INVALID_ARGUMENT",2,"Unknown Phase 1 configuration setting.");
            if(Command=="block.export") Errors.Require((Get("sha512")==null || Get("id")!=null) && (Get("id")==null || (Get("from-timestamp-ms")==null && Get("to-timestamp-ms")==null)) && (Number("from-timestamp-ms")==null || Number("to-timestamp-ms")==null || Number("from-timestamp-ms")<=Number("to-timestamp-ms")),"INVALID_ARGUMENT",2,"Incompatible export filters.");
            if(Command=="instance.recover" && Get("confirm-unbound-initialization")==null && File.Exists(Need("from"))) throw new LedgerException("INVALID_ARGUMENT",2,"Key-only recovery requires explicit --confirm-unbound-initialization.");
        }
        public string? Get(string name) => options.GetValueOrDefault(name);
        public string Need(string name) => Get(name) ?? throw new LedgerException("INVALID_ARGUMENT",2,"Missing --"+name);
        public Guid? Guid(string name) => Get(name) is {} text ? StrictJson.Uuid(text,"INVALID_ARGUMENT") : null;
        public long? Number(string name)
        {
            if(Get(name) is not {} text) return null;
            var digits=text.StartsWith('-') && name!="value"?text[1..]:text;
            Errors.Require(digits.Length>0 && digits.All(c=>c is >= '0' and <= '9') && long.TryParse(text,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out _),"INVALID_ARGUMENT",2,"Invalid ASCII integer.");
            return long.Parse(text,CultureInfo.InvariantCulture);
        }
    }
    private static object Execute(string[] rawArgs)
    {
        var a=new Arguments(rawArgs); var op=a.Command;
        if(op=="instance.init") { var m=LedgerInstance.Initialize(a.Root); return new { operation=op,instance_guid=m.InstanceId,public_key_hex=m.PublicKey,instance_root=a.Root,durable=true }; }
        if(op=="instance.recover" || op=="key.restore")
        {
            var keyOnly=a.Get("confirm-unbound-initialization")!=null; var m=LedgerInstance.Recover(a.Root,a.Need(op=="instance.recover"?"from":"directory"),keyOnly);
            if(op=="key.restore") return new { operation=op,instance_guid=m.InstanceId,public_key_hex=m.PublicKey,restored=true,state=m.Binding==null?"unbound":"bound" };
            return new { operation=op,instance_guid=m.InstanceId,public_key_hex=m.PublicKey,recovered=true,state=m.Binding==null?"unbound":"bound",recovery_kind=keyOnly?"key_only_unbound":"original_backup" };
        }
        if(op is "instance.status" or "status") return Status(a.Root,op);
        using var instance=OpenForOperation(a.Root); instance.EnsureConfig();
        switch(op)
        {
            case "ledger.init": var genesis=instance.InitializeLedger(); return new { operation=op,ledger_id_hex=genesis.LedgerId,genesis_sha512=genesis.Hash,genesis_block_guid=genesis.Id,durable=true,binding_durable=true };
            case "ledger.join": genesis=instance.Join(a.Need("ledger-id"),a.Need("genesis-sha512"),Paths.ReadBounded(a.Need("genesis-file"),Math.Min(instance.Limits.MaxRawBlockBytes,instance.Limits.MaxInflightImportBytes/4))); return new { operation=op,ledger_id_hex=genesis.LedgerId,genesis_sha512=genesis.Hash,genesis_block_guid=genesis.Id,binding_durable=true };
            case "ledger.recover-genesis": genesis=instance.RecoverGenesis(a.Need("ledger-id"),a.Need("genesis-sha512")); return new { operation=op,ledger_id_hex=genesis.LedgerId,genesis_sha512=genesis.Hash,genesis_block_guid=genesis.Id,recovered=true,binding_durable=true };
            case "key.backup": instance.Backup(a.Need("directory")); return new { operation=op,directory=Paths.Absolute(a.Need("directory")),instance_guid=instance.Manifest.InstanceId,public_key_hex=instance.PublicKey,durable=true };
            case "config.get": return new { operation=op,limits=instance.LimitFields() };
            case "config.set": instance.Configure(a.Need("name"),a.Number("value")!.Value); return new { operation=op,limits=instance.LimitFields(),changed_name=a.Need("name") };
            case "block.publish":
                var isRevocation=a.Get("revocation-consent-file")!=null;
                var eventBytes=Paths.ReadBounded(a.Need(isRevocation?"revocation-consent-file":"event-file"),isRevocation?229:Math.Min(instance.Limits.MaxDecompressedEventBytes,instance.Limits.MaxInflightImportBytes/8));
                var published=isRevocation?instance.PublishRevocationConsent(eventBytes):instance.Publish(eventBytes,a.Guid("block-guid")); var b=published.Block;
                if(isRevocation) return new { operation=op,ledger_id_hex=b.LedgerId,block_guid=b.Id,sha512=b.Hash,timestamp_utc_ms=b.Timestamp.ToString(CultureInfo.InvariantCulture),authorization=published.Authorization,durable=true,path=instance.BlockPath(b,false),consent_timestamp_utc_ms=b.Event!.ConsentTimestamp.ToString(CultureInfo.InvariantCulture),consent_age_ms=RevocationConsent.CheckInterval(b.Timestamp,b.Event.ConsentTimestamp) };
                return new { operation=op,ledger_id_hex=b.LedgerId,block_guid=b.Id,sha512=b.Hash,timestamp_utc_ms=b.Timestamp.ToString(CultureInfo.InvariantCulture),authorization=published.Authorization,durable=true,path=instance.BlockPath(b,false) };
            case "master.countersign-revocation":
                var consent=instance.CreateRevocationConsent(StrictJson.Hex(a.Need("outer-signer-public-key"),32),StrictJson.Hex(a.Need("revoked-public-key"),32));
                instance.WriteOutput(a.Need("output"),consent.Encode());
                return new { operation=op,ledger_id_hex=consent.LedgerId,block_guid=consent.BlockId,consent_timestamp_utc_ms=consent.Timestamp.ToString(CultureInfo.InvariantCulture),outer_signer_public_key=a.Need("outer-signer-public-key"),revoked_public_key=a.Need("revoked-public-key"),countersigner_public_key=instance.PublicKey,consent_sha512=Binary.Sha512(consent.Intent),signature_hex=Binary.Hex(consent.Signature),path=Paths.Absolute(a.Need("output")),bytes=229,durable=true };
            case "block.get":
                b=instance.Get(a.Guid("id")!.Value,a.Get("ledger-id"),a.Get("sha512")); instance.WriteOutput(a.Need("output"),b.Raw);
                return new { operation=op,ledger_id_hex=b.LedgerId,block_guid=b.Id,sha512=b.Hash,path=Paths.Absolute(a.Need("output")),bytes=b.Raw.Length,durable=true };
            case "block.export":
                var selection=instance.SelectExport(a.Get("ledger-id"),a.Guid("id"),a.Get("sha512"),a.Number("from-timestamp-ms"),a.Number("to-timestamp-ms"));
                long length=14; foreach(var selected in selection.Blocks) length=checked(length+8+selected.Raw.Length);
                Errors.Require(length<=instance.Limits.MaxInflightImportBytes/2,"RESOURCE_LIMIT",5,"Buffered export exceeds configured memory.");
                var bytes=Ddlb.Encode(selection.Blocks); instance.WriteOutput(a.Need("output"),bytes);
                return new { operation=op,ledger_id_hex=selection.LedgerId,path=Paths.Absolute(a.Need("output")),bytes=bytes.Length,sha512=Binary.Sha512(bytes),block_count=selection.Blocks.Count,durable=true };
            case "block.import": return Import(instance,a.Need("input"));
            default: throw new LedgerException("INVALID_ARGUMENT",2,"Unknown operation.");
        }
    }
    private static LedgerInstance OpenForOperation(string root)
    {
        try { return LedgerInstance.Open(root); }
        catch(LedgerException e) when(e.Code is "IDENTITY_MISMATCH" or "SIGNING_IDENTITY_UNAVAILABLE")
        { throw new LedgerException("RECOVERY_REQUIRED",4,"Original identity must be restored before ordinary processing.",new {reason=e.Code}); }
    }
    private sealed record ImportResult(int index,string? ledger_id_hex,Guid? block_guid,string? sha512,string status,string reason,string authorization,bool duplicate,bool durable,bool retryable);
    private static object Import(LedgerInstance instance,string path)
    {
        Paths.Safe(path); using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        var results=new List<ImportResult>(); var errors=new SortedDictionary<int,LedgerException>();
        var candidates=new List<(int Index,SignedBlock Block)>(); long total=1, resident=0; var committed=0; var duplicates=0;
        ImportResult Rejected(int index,SignedBlock? block,LedgerException e) => new(index,block?.LedgerId,block?.Id,block?.Hash,"rejected",e.Code,
            e.Code=="AUTHORITY_UNRESOLVED"?"pending":e.Code=="NOT_AUTHORIZED"?"unauthorized":e.Code=="BLOCK_GUID_CONFLICT"?"authorized":"not_evaluated",false,false,e.Code=="AUTHORITY_UNRESOLVED" || e.Code=="IO_ERROR");
        void ReadItem(int index,long size)
        {
            SignedBlock? block=null;
            try
            {
                instance.Limits.CheckRaw(size);
                Errors.Require(resident+size*8+1024<=instance.Limits.MaxInflightImportBytes/2,"RESOURCE_LIMIT",5,"Bounded batch validation memory exceeded; split the batch or raise the configured limit.");
                var raw=new byte[(int)size]; input.ReadExactly(raw); block=BlockCodec.Parse(raw,instance.Limits);
                resident+=size*8+block.EventBytes.LongLength*4+1024;
                Errors.Require(resident<=instance.Limits.MaxInflightImportBytes/2,"RESOURCE_LIMIT",5,"Decoded batch memory exceeded.");
                candidates.Add((index,block)); results.Add(new(index,block.LedgerId,block.Id,block.Hash,"rejected","NOT_PROCESSED","not_evaluated",false,false,false));
            }
            catch(LedgerException e) { errors[index]=e; results.Add(Rejected(index,block,e)); }
        }
        try
        {
            var magic=new byte[4]; var count=input.Read(magic); input.Position=0;
            if(count==4 && magic.AsSpan().SequenceEqual("DDLB"u8))
            {
                var header=new byte[14]; input.ReadExactly(header); Errors.Require(header[4]==0 && header[5]==1,"BAD_FRAME",2,"Unsupported DDLB container version.");
                var announced=BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(6));
                Errors.Require(announced<=(ulong)Math.Min(int.MaxValue,instance.Limits.MaxInflightImportBytes/2048),"RESOURCE_LIMIT",5,"DDLB result-count memory limit."); total=(long)announced;
                var lengthBytes=new byte[8];
                for(var i=0;i<total;i++)
                {
                    input.ReadExactly(lengthBytes); var size=BinaryPrimitives.ReadUInt64BigEndian(lengthBytes);
                    Errors.Require(size<=long.MaxValue && size<=(ulong)(input.Length-input.Position),"BAD_FRAME",2,"Truncated/overflowed DDLB record.");
                    var next=checked(input.Position+(long)size); ReadItem(i,(long)size); input.Position=next;
                }
                Errors.Require(input.Position==input.Length,"BAD_FRAME",2,"Trailing DDLB bytes.");
            }
            else ReadItem(0,input.Length);
        }
        catch(Exception e) when(e is EndOfStreamException or LedgerException or IOException or UnauthorizedAccessException)
        {
            var failure=e as LedgerException ?? new LedgerException(e is EndOfStreamException?"BAD_FRAME":"IO_ERROR",e is EndOfStreamException?2:6,"Incomplete import framing or input I/O.");
            var index=results.Count; errors[index]=failure;
            while(results.Count<total) results.Add(Rejected(results.Count,null,failure));
        }
        // Only successfully durable history is authority for the next item. Canonical sorting makes
        // grants durable before their dependents, even when transport order is inverted.
        var durabilityInterrupted=false;
        foreach(var item in candidates.OrderBy(x=>x.Block.Timestamp).ThenBy(x=>x.Block.Id,Comparer<Guid>.Create(Binary.CompareGuid))
            .ThenBy(x=>Binary.Hex(x.Block.Signer),StringComparer.Ordinal).ThenBy(x=>x.Index))
        {
            var block=item.Block;
            if(durabilityInterrupted)
            {
                var stopped=new LedgerException("DURABILITY_UNCERTAIN",6,"An earlier publication needs durability reconciliation; remaining batch items were not written.");
                errors[item.Index]=stopped; results[item.Index]=Rejected(item.Index,block,stopped); continue;
            }
            try
            {
                var d=instance.ImportBuffered(block.Raw,out var duplicate,resident);
                results[item.Index]=new(item.Index,block.LedgerId,block.Id,block.Hash,d.Status,d.Reason,d.Authorization,duplicate,true,false);
                if(duplicate) duplicates++; else committed++;
            }
            catch(Exception exception) when(exception is LedgerException or IOException or UnauthorizedAccessException)
            {
                var e=exception as LedgerException ?? new LedgerException("IO_ERROR",6,"Block publication failed."); errors[item.Index]=e;
                durabilityInterrupted=e.Code is "IO_ERROR" or "DURABILITY_UNCERTAIN";
                if(e.Details is ImportInterruption receipt)
                {
                    var d=receipt.Disposition;
                    results[item.Index]=new(item.Index,block.LedgerId,block.Id,block.Hash,d.Status,d.Reason,d.Authorization,!receipt.NewlyPersisted,true,false);
                    if(receipt.NewlyPersisted) committed++; else duplicates++;
                }
                else results[item.Index]=Rejected(item.Index,block,e);
            }
        }
        if(committed+duplicates>0)
        {
            try
            {
                var final=instance.Snapshot().Blocks.ToDictionary(x=>x.Block.Hash,StringComparer.Ordinal);
                for(var i=0;i<results.Count;i++) if(results[i].durable && final.TryGetValue(results[i].sha512!,out var d))
                    results[i]=results[i] with {status=d.Status,reason=d.Reason,authorization=d.Authorization,retryable=false};
            }
            catch(Exception e) when(e is LedgerException or IOException or UnauthorizedAccessException)
            { errors.TryAdd(results.Count,e as LedgerException ?? new LedgerException("IO_ERROR",6,"Final classification failed.")); }
        }
        var rejected=results.Count(x=>!x.durable);
        var counts=new { results,durable_count=committed,duplicate_count=duplicates,rejected_count=rejected,total_count=total };
        if(errors.Count>0)
        {
            var first=errors.First().Value;
            throw new LedgerException(committed+duplicates>0?"PARTIAL_IMPORT":first.Code,committed+duplicates>0?3:first.ExitCode,
                "Import did not complete without errors.",new { counts.results,counts.durable_count,counts.duplicate_count,counts.rejected_count,counts.total_count,reason=first.Code });
        }
        return new { operation="block.import",counts.results,counts.durable_count,counts.duplicate_count,counts.rejected_count,counts.total_count };
    }
    private static object Status(string root,string operation)
    {
        InstanceManifest? manifest=null; var available=false; var authorized=false; var codes=new List<string>(); AuthorizationSnapshot? snapshot=null;
        var newRoot=(!Directory.Exists(root) || !Directory.EnumerateFileSystemEntries(root).Any()) && (!File.Exists(root+".DLDB") || new FileInfo(root+".DLDB").Length==0);
        try
        {
            if(!newRoot)
            {
                using var instance=LedgerInstance.Open(root); manifest=instance.Manifest; available=true;
                if(instance.ConfigError!=null) codes.Add(instance.ConfigError);
                else { snapshot=instance.Snapshot(); authorized=manifest.Binding!=null && snapshot.AuthorizedKeys.Contains(manifest.PublicKey); }
            }
        }
        catch(Exception e) when(e is LedgerException or IOException or UnauthorizedAccessException)
        {
            available=false; snapshot=null; authorized=false;
            codes.Add(e is LedgerException le?le.Code:"IO_ERROR");
            try { manifest=InstanceManifest.Decode(Paths.ReadBounded(Path.Combine(root,"instance.identity.json"),4096,"IDENTITY_MISMATCH")); } catch(Exception secondary) when(secondary is LedgerException or IOException or UnauthorizedAccessException) { }
        }
        var state=newRoot?"uninitialized":!available?"recovery_required":manifest!.Binding==null?"unbound":"bound";
        var sorted=codes.Order(StringComparer.Ordinal).ToArray();
        if(operation=="instance.status") return new { operation,state,instance_guid=manifest?.InstanceId,public_key_hex=manifest?.PublicKey,signing_available=available,ledger_id_hex=manifest?.Binding?.LedgerId,genesis_sha512=manifest?.Binding?.GenesisHash,recovery_codes=sorted };
        var blocks=snapshot?.Blocks; object? blockFields=blocks==null && !newRoot?null:new {
            retained_count=blocks?.Count ?? 0,pending_count=blocks?.Count(x=>x.Status=="pending") ?? 0,quarantined_count=blocks?.Count(x=>x.Status=="quarantined") ?? 0,disputed_count=0,opaque_count=0 };
        return new { operation,instance=new {state,instance_guid=manifest?.InstanceId,public_key_hex=manifest?.PublicKey,recovery_codes=sorted},
            binding=new {ledger_id_hex=manifest?.Binding?.LedgerId,genesis_sha512=manifest?.Binding?.GenesisHash,state=manifest?.Binding==null?"unbound":"bound"},
            signing=new {available,authorized},blocks=blockFields,projection=new {state="not_installed",applied_count=(int?)null},peers=!available && !newRoot?null:new {state="not_installed",connected_count=(int?)null} };
    }
}
