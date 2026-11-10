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
            stdout.Write(JsonSerializer.Serialize(new { ok=false,data=(object?)null,error=new { code=e.Code,message=e.Message,retryable=e.Code is "IO_ERROR" or "DUPLICATE_INSTANCE",details=e.Details } })+"\n");
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
                "instance.recover" => ["from","confirm-unbound-initialization"], "ledger.join" or "ledger.recover-genesis" => ["ledger-guid","genesis-sha512"],
                "key.backup" or "key.restore" => ["directory"], "block.publish" => ["event-file","block-guid","timestamp-utc-ms"],
                "block.import" => ["input"], "block.get" => ["id","output","ledger-guid","sha512"],
                "block.export" => ["output","ledger-guid","id","sha512","from-timestamp-ms","to-timestamp-ms"],
                "master.countersign-revocation" => ["block-guid","timestamp-utc-ms","outer-signer-public-key","revoked-public-key","output"],
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
                "instance.recover" => ["from"], "ledger.join" or "ledger.recover-genesis" => ["ledger-guid","genesis-sha512"],
                "key.backup" or "key.restore" => ["directory"], "block.publish" => ["event-file"], "block.import" => ["input"],
                "block.get" => ["id","output"], "block.export" => ["output"], "config.set" => ["name","value"],
                "master.countersign-revocation" => allowed, _ => [] };
            foreach(var r in required) Need(r);
            foreach(var pair in options)
            {
                if(pair.Key is "from" or "directory" or "event-file" or "input" or "output") Paths.Absolute(pair.Value);
                if(pair.Key is "id" or "block-guid" or "ledger-guid") StrictJson.Uuid(pair.Value,"INVALID_ARGUMENT");
                if(pair.Key is "sha512" or "genesis-sha512") StrictJson.Hex(pair.Value,64,"INVALID_ARGUMENT");
                if(pair.Key is "outer-signer-public-key" or "revoked-public-key") StrictJson.Hex(pair.Value,32,"INVALID_ARGUMENT");
                if(pair.Key is "timestamp-utc-ms" or "from-timestamp-ms" or "to-timestamp-ms" or "value") Number(pair.Key);
            }
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
        using var instance=LedgerInstance.Open(a.Root); instance.EnsureConfig();
        switch(op)
        {
            case "ledger.init": var genesis=instance.InitializeLedger(); return new { operation=op,ledger_guid=genesis.LedgerId,genesis_sha512=genesis.Hash,genesis_block_guid=genesis.Id,durable=true,binding_durable=true };
            case "ledger.join": genesis=instance.Join(a.Guid("ledger-guid")!.Value,a.Need("genesis-sha512")); return new { operation=op,ledger_guid=genesis.LedgerId,genesis_sha512=genesis.Hash,genesis_block_guid=genesis.Id,binding_durable=true };
            case "ledger.recover-genesis": genesis=instance.RecoverGenesis(a.Guid("ledger-guid")!.Value,a.Need("genesis-sha512")); return new { operation=op,ledger_guid=genesis.LedgerId,genesis_sha512=genesis.Hash,genesis_block_guid=genesis.Id,recovered=true,binding_durable=true };
            case "key.backup": instance.Backup(a.Need("directory")); return new { operation=op,directory=Paths.Absolute(a.Need("directory")),instance_guid=instance.Manifest.InstanceId,public_key_hex=instance.PublicKey,durable=true };
            case "config.get": return new { operation=op,limits=instance.LimitFields() };
            case "config.set": instance.Configure(a.Need("name"),a.Number("value")!.Value); return new { operation=op,limits=instance.LimitFields(),changed_name=a.Need("name") };
            case "block.publish":
                var eventBytes=Paths.ReadBounded(a.Need("event-file"),Math.Min(instance.Limits.MaxDecompressedEventBytes,instance.Limits.MaxInflightImportBytes/4));
                var published=instance.Publish(eventBytes,a.Guid("block-guid"),a.Number("timestamp-utc-ms")); var b=published.Block;
                return new { operation=op,ledger_guid=b.LedgerId,block_guid=b.Id,sha512=b.Hash,timestamp_utc_ms=b.Timestamp.ToString(CultureInfo.InvariantCulture),authorization=published.Authorization,durable=true,path=instance.BlockPath(b,false) };
            case "master.countersign-revocation":
                var consent=instance.Countersign(a.Guid("block-guid")!.Value,a.Number("timestamp-utc-ms")!.Value,StrictJson.Hex(a.Need("outer-signer-public-key"),32),StrictJson.Hex(a.Need("revoked-public-key"),32));
                instance.WriteOutput(a.Need("output"),consent.Signature);
                return new { operation=op,ledger_guid=instance.Manifest.Binding!.LedgerId,block_guid=a.Guid("block-guid")!.Value,timestamp_utc_ms=a.Number("timestamp-utc-ms")!.Value.ToString(CultureInfo.InvariantCulture),outer_signer_public_key=a.Need("outer-signer-public-key"),revoked_public_key=a.Need("revoked-public-key"),countersigner_public_key=instance.PublicKey,consent_sha512=Binary.Sha512(consent.Intent),signature_hex=Binary.Hex(consent.Signature),path=Paths.Absolute(a.Need("output")),bytes=64,durable=true };
            case "block.get":
                b=instance.Get(a.Guid("id")!.Value,a.Guid("ledger-guid"),a.Get("sha512")); instance.WriteOutput(a.Need("output"),b.Raw);
                return new { operation=op,ledger_guid=b.LedgerId,block_guid=b.Id,sha512=b.Hash,path=Paths.Absolute(a.Need("output")),bytes=b.Raw.Length,durable=true };
            case "block.export":
                var selection=instance.SelectExport(a.Guid("ledger-guid"),a.Guid("id"),a.Get("sha512"),a.Number("from-timestamp-ms"),a.Number("to-timestamp-ms"));
                long length=14; foreach(var selected in selection.Blocks) length=checked(length+8+selected.Raw.Length);
                Errors.Require(length<=instance.Limits.MaxInflightImportBytes/2,"RESOURCE_LIMIT",5,"Buffered export exceeds configured memory.");
                var bytes=Ddlb.Encode(selection.Blocks); instance.WriteOutput(a.Need("output"),bytes);
                return new { operation=op,ledger_guid=selection.LedgerId,path=Paths.Absolute(a.Need("output")),bytes=bytes.Length,sha512=Binary.Sha512(bytes),block_count=selection.Blocks.Count,durable=true };
            case "block.import": return Import(instance,a.Need("input"));
            default: throw new LedgerException("INVALID_ARGUMENT",2,"Unknown operation.");
        }
    }
    private sealed record ImportResult(int index,Guid? ledger_guid,Guid? block_guid,string? sha512,string status,string reason,string authorization,bool duplicate,bool durable);
    private static object Import(LedgerInstance instance,string path)
    {
        Paths.Safe(path); using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        var results=new List<ImportResult>(); LedgerException? first=null; long total=1; var committed=0; var duplicates=0; var rejected=0;
        void Item(byte[] raw)
        {
            SignedBlock? block=null;
            try
            {
                block=BlockCodec.Parse(raw,instance.Limits); var d=instance.Import(raw,out var duplicate);
                results.Add(new(results.Count,block.LedgerId,block.Id,block.Hash,d.Status,d.Reason,d.Authorization,duplicate,true));
                if(duplicate) duplicates++; else committed++;
            }
            catch(LedgerException e) { first ??= e; rejected++; results.Add(new(results.Count,block?.LedgerId,block?.Id,block?.Hash,"rejected",e.Code,"not_evaluated",false,false)); }
            catch(IOException e) { first ??= new LedgerException("IO_ERROR",6,e.Message); rejected++; results.Add(new(results.Count,block?.LedgerId,block?.Id,block?.Hash,"rejected","IO_ERROR","not_evaluated",false,false)); }
        }
        try
        {
            var magic=new byte[4]; var count=input.Read(magic); input.Position=0;
            if(count==4 && magic.AsSpan().SequenceEqual("DDLB"u8))
            {
                var header=new byte[14]; input.ReadExactly(header); Errors.Require(header[4]==0 && header[5]==1,"BAD_FRAME",2,"Unsupported DDLB container version.");
                var announced=BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(6));
                Errors.Require(announced<=(ulong)Math.Min(int.MaxValue,instance.Limits.MaxInflightImportBytes/1024),"RESOURCE_LIMIT",5,"DDLB result-count memory limit."); total=(long)announced;
                var lengthBytes=new byte[8];
                for(long i=0;i<total;i++)
                {
                    input.ReadExactly(lengthBytes); var size=BinaryPrimitives.ReadUInt64BigEndian(lengthBytes);
                    Errors.Require(size<=long.MaxValue && size<=(ulong)(input.Length-input.Position),"BAD_FRAME",2,"Truncated/overflowed DDLB record.");
                    instance.Limits.CheckRaw((long)size); var raw=new byte[(int)size]; input.ReadExactly(raw); Item(raw);
                }
                Errors.Require(input.Position==input.Length,"BAD_FRAME",2,"Trailing DDLB bytes.");
            }
            else { instance.Limits.CheckRaw(input.Length); var raw=new byte[(int)input.Length]; input.ReadExactly(raw); Item(raw); }
        }
        catch(EndOfStreamException) { first ??= new LedgerException("BAD_FRAME",2,"Truncated DDLB framing."); }
        catch(LedgerException e) { first ??= e; }
        // Earlier/later inputs may have reclassified already accepted variants: report one coherent final snapshot.
        if(committed+duplicates>0)
        {
            var final=instance.Snapshot().Blocks.ToDictionary(x=>x.Block.Hash,StringComparer.Ordinal);
            for(var i=0;i<results.Count;i++) if(results[i].durable && final.TryGetValue(results[i].sha512!,out var d)) results[i]=results[i] with {status=d.Status,reason=d.Reason,authorization=d.Authorization};
        }
        var counts=new { results,durable_count=committed,duplicate_count=duplicates,rejected_count=rejected,total_count=total };
        if(first!=null) throw new LedgerException(committed+duplicates>0?"PARTIAL_IMPORT":first.Code,committed+duplicates>0?3:first.ExitCode,"Import did not complete without errors.",new { counts.results,counts.durable_count,counts.duplicate_count,counts.rejected_count,counts.total_count,reason=first.Code });
        return new { operation="block.import",counts.results,counts.durable_count,counts.duplicate_count,counts.rejected_count,counts.total_count };
    }
    private static object Status(string root,string operation)
    {
        InstanceManifest? manifest=null; var available=false; var authorized=false; var codes=new List<string>(); AuthorizationSnapshot? snapshot=null;
        var newRoot=!Directory.Exists(root) || !Directory.EnumerateFileSystemEntries(root).Any();
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
            codes.Add(e is LedgerException le?le.Code:"IO_ERROR");
            try { manifest=InstanceManifest.Decode(Paths.ReadBounded(Path.Combine(root,"instance.identity.json"),4096,"IDENTITY_MISMATCH")); } catch(Exception secondary) when(secondary is LedgerException or IOException or UnauthorizedAccessException) { }
        }
        var state=newRoot?"uninitialized":!available?"recovery_required":manifest!.Binding==null?"unbound":"bound";
        var sorted=codes.Order(StringComparer.Ordinal).ToArray();
        if(operation=="instance.status") return new { operation,state,instance_guid=manifest?.InstanceId,public_key_hex=manifest?.PublicKey,signing_available=available,ledger_guid=manifest?.Binding?.LedgerId,genesis_sha512=manifest?.Binding?.GenesisHash,recovery_codes=sorted };
        var blocks=snapshot?.Blocks; object? blockFields=blocks==null && !newRoot?null:new {
            retained_count=blocks?.Count ?? 0,pending_count=blocks?.Count(x=>x.Status=="pending") ?? 0,quarantined_count=blocks?.Count(x=>x.Status=="quarantined") ?? 0,disputed_count=blocks?.Count(x=>x.Reason=="BLOCK_ID_DISPUTED") ?? 0,opaque_count=0 };
        return new { operation,instance=new {state,instance_guid=manifest?.InstanceId,public_key_hex=manifest?.PublicKey,recovery_codes=sorted},
            binding=new {ledger_guid=manifest?.Binding?.LedgerId,genesis_sha512=manifest?.Binding?.GenesisHash,state=manifest?.Binding==null?"unbound":"bound"},
            signing=new {available,authorized},blocks=blockFields,projection=new {state="not_installed",applied_count=(int?)null},peers=!available && !newRoot?null:new {state="not_installed",connected_count=(int?)null} };
    }
}
