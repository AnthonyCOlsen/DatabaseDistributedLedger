using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace UserDimensions.Ledger;

public sealed class LedgerInstance : IDisposable
{
    public string Root { get; }
    public InstanceManifest Manifest { get; private set; }
    private readonly SigningIdentity identity;
    private readonly IdentityLease rootLease, identityLease;
    private readonly DurableFiles files;
    public ResourceLimits Limits { get; private set; }
    public string? ConfigError { get; private set; }
    public string PublicKey => Manifest.PublicKey;
    private string ManifestPath => Path.Combine(Root,"instance.identity.json");
    private string KeyPath => Path.Combine(Root,"signing-key.v1.json");
    private string ConfigPath => Path.Combine(Root,"instance.config.json");

    private LedgerInstance(string root, InstanceManifest manifest, SigningIdentity key, IdentityLease rootLease, DurableFiles files)
    {
        Root=root; Manifest=manifest; identity=key; this.rootLease=rootLease; this.files=files;
        identityLease = new IdentityLease("identity:" + manifest.InstanceId.ToString("D"));
        Limits = new();
        try { Limits=LoadConfig(); } catch (LedgerException e) { ConfigError=e.Code; }
    }
    public static LedgerInstance Open(string root, DurableFiles? files = null)
    {
        root=Paths.Absolute(root); var lease = new IdentityLease("root:" + Normalize(root)); SigningIdentity? key=null;
        try
        {
            Errors.Require(File.Exists(Path.Combine(root,"instance.identity.json")) && File.Exists(Path.Combine(root,"signing-key.v1.json")),"RECOVERY_REQUIRED",4,"Original instance identity is missing.");
            var manifest=InstanceManifest.Decode(Paths.ReadBounded(Path.Combine(root,"instance.identity.json"),4096,"IDENTITY_MISMATCH"));
            key=SigningIdentity.Decode(Paths.ReadBounded(Path.Combine(root,"signing-key.v1.json"),4096,"IDENTITY_MISMATCH")); Match(manifest,key);
            return new(root,manifest,key,lease,files ?? new());
        }
        catch { key?.Dispose(); lease.Dispose(); throw; }
    }
    private static string Normalize(string path) => OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
    public static InstanceManifest Initialize(string root, DurableFiles? files = null)
    {
        root=Paths.Absolute(root); files ??= new();
        using var lease=new IdentityLease("root:"+Normalize(root));
        Errors.Require(!Directory.Exists(root) || !Directory.EnumerateFileSystemEntries(root).Any(),"RECOVERY_REQUIRED",4,"Existing instance content requires original-identity recovery.");
        Errors.Require(!File.Exists(root + ".DLDB"),"RECOVERY_REQUIRED",4,"Existing database requires recovery.");
        Errors.Require(root.Length + 250 < 32000,"INVALID_ARGUMENT",2,"Root leaves insufficient room for ledger paths.");
        // Check fixed media and platform before creating identity remnants.
        Errors.Require(OperatingSystem.IsWindows(),"DURABILITY_UNCERTAIN",6,"Windows 11 NTFS is the initial durability platform.");
        var drive=new DriveInfo(Path.GetPathRoot(root)!); Errors.Require(drive.DriveType==DriveType.Fixed && drive.DriveFormat=="NTFS","DURABILITY_UNCERTAIN",6,"Local NTFS is required.");
        files.Directory(root); using var key=SigningIdentity.Create(); var manifest=new InstanceManifest(key.InstanceId,Binary.Hex(key.PublicKey),null);
        files.Publish(Path.Combine(root,"signing-key.v1.json"),key.Encode());
        files.Publish(Path.Combine(root,"instance.identity.json"),manifest.Encode()); return manifest;
    }
    private static void Match(InstanceManifest manifest, SigningIdentity key) => Errors.Require(manifest.InstanceId==key.InstanceId && manifest.PublicKey==Binary.Hex(key.PublicKey),"IDENTITY_MISMATCH",4,"Manifest and signing key do not match.");
    public void Dispose() { identity.Dispose(); identityLease.Dispose(); rootLease.Dispose(); }
    public void EnsureConfig() => Errors.Require(ConfigError==null,ConfigError ?? "CONFIG_CORRUPT",4,"Configuration requires explicit repair.");
    private ResourceLimits LoadConfig()
    {
        if (!File.Exists(ConfigPath)) return new();
        try
        {
            using var doc=StrictJson.Parse(Paths.ReadBounded(ConfigPath,4096,"CONFIG_CORRUPT")); var r=doc.RootElement;
            StrictJson.Fields(r,"format","version","instance_guid","limits");
            Errors.Require(StrictJson.Text(r,"format")=="UDDL-INSTANCE-CONFIG" && StrictJson.Integer(r,"version")==1,"CONFIG_CORRUPT",4,"Unsupported config format.");
            Errors.Require(StrictJson.Uuid(StrictJson.Text(r,"instance_guid"))==Manifest.InstanceId,"CONFIG_IDENTITY_MISMATCH",4,"Configuration belongs to another instance.");
            var l=r.GetProperty("limits"); StrictJson.Fields(l,"max_raw_block_bytes","max_decompressed_event_bytes","max_inflight_import_bytes","per_block_timeout_seconds");
            var limits=new ResourceLimits(StrictJson.Integer(l,"max_raw_block_bytes"),StrictJson.Integer(l,"max_decompressed_event_bytes"),StrictJson.Integer(l,"max_inflight_import_bytes"),StrictJson.Integer(l,"per_block_timeout_seconds")); ValidateLimits(limits); return limits;
        }
        catch (LedgerException e) when (e.Code!="CONFIG_IDENTITY_MISMATCH") { throw new LedgerException("CONFIG_CORRUPT",4,"Malformed or unsupported configuration."); }
    }
    private static void ValidateLimits(ResourceLimits l)
    {
        Errors.Require(l.MaxRawBlockBytes>=211 && l.MaxDecompressedEventBytes>=1 && l.MaxInflightImportBytes>=211 && l.PerBlockTimeoutSeconds>=1,"INVALID_ARGUMENT",2,"Invalid positive resource limit.");
        Errors.Require(l.MaxRawBlockBytes<=int.MaxValue && l.MaxDecompressedEventBytes<=int.MaxValue && l.MaxInflightImportBytes<=int.MaxValue && l.PerBlockTimeoutSeconds<=long.MaxValue/1000,"RESOURCE_LIMIT",5,"Requested limit exceeds this buffered implementation's enforceable platform bounds.");
    }
    public object LimitFields() => new { max_raw_block_bytes=Limits.MaxRawBlockBytes, max_decompressed_event_bytes=Limits.MaxDecompressedEventBytes, max_inflight_import_bytes=Limits.MaxInflightImportBytes, per_block_timeout_seconds=Limits.PerBlockTimeoutSeconds };
    public void Configure(string name, long value)
    {
        EnsureConfig(); var next=name switch {
            "max_raw_block_bytes" => Limits with { MaxRawBlockBytes=value }, "max_decompressed_event_bytes" => Limits with { MaxDecompressedEventBytes=value },
            "max_inflight_import_bytes" => Limits with { MaxInflightImportBytes=value }, "per_block_timeout_seconds" => Limits with { PerBlockTimeoutSeconds=value },
            _ => throw new LedgerException("INVALID_ARGUMENT",2,"Unknown Phase 1 limit.") };
        ValidateLimits(next);
        var bytes=StrictJson.Write(new { format="UDDL-INSTANCE-CONFIG",version=1,instance_guid=Manifest.InstanceId.ToString("D"),limits=new {
            max_raw_block_bytes=next.MaxRawBlockBytes,max_decompressed_event_bytes=next.MaxDecompressedEventBytes,max_inflight_import_bytes=next.MaxInflightImportBytes,per_block_timeout_seconds=next.PerBlockTimeoutSeconds } });
        files.Publish(ConfigPath,bytes,File.Exists(ConfigPath)); Limits=next;
    }
    private List<(SignedBlock Block,string Path)> Inventory()
    {
        EnsureConfig(); var result=new List<(SignedBlock,string)>(); var directory=Path.Combine(Root,"BLK"); long resident=0;
        if (!Directory.Exists(directory)) return result;
        foreach (var path in SafeInventoryFiles(directory))
        {
            var length=new FileInfo(path).Length; Limits.CheckRaw(length);
            Errors.Require(resident + length*8 <= Limits.MaxInflightImportBytes,"RESOURCE_LIMIT",5,"Canonical replay exceeds configured buffered history memory; raise the limit.");
            var block=BlockCodec.Parse(Paths.ReadBounded(path,Math.Min(Limits.MaxRawBlockBytes,Limits.MaxInflightImportBytes/4)),Limits);
            if (Manifest.Binding!=null && Manifest.Binding.LedgerId!=block.LedgerId) continue;
            var expected=BlockPath(block,path.EndsWith(".XBLK",StringComparison.Ordinal));
            Errors.Require(string.Equals(path,expected,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal),"RECOVERY_REQUIRED",4,"Block filename does not match verified bytes.");
            resident+=length*8+block.EventBytes.LongLength*4;
            Errors.Require(resident<=Limits.MaxInflightImportBytes,"RESOURCE_LIMIT",5,"Decoded history exceeds configured memory.");
            result.Add((block,path));
        }
        return result;
    }
    private static IEnumerable<string> SafeInventoryFiles(string root)
    {
        Paths.Safe(root);
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
        {
            Paths.Safe(path);
            if (Directory.Exists(path)) { foreach (var child in SafeInventoryFiles(path)) yield return child; }
            else if (path.EndsWith(".BLK",StringComparison.Ordinal) || path.EndsWith(".XBLK",StringComparison.Ordinal)) yield return path;
        }
    }
    public string BlockPath(SignedBlock b, bool archive)
    {
        string directory, stamp;
        try
        {
            var date=DateTimeOffset.FromUnixTimeMilliseconds(b.Timestamp);
            directory=Path.Combine(Root,"BLK",date.ToString("yyyy",CultureInfo.InvariantCulture),date.ToString("yyyyMM",CultureInfo.InvariantCulture),date.ToString("yyyyMMdd",CultureInfo.InvariantCulture));
            stamp=date.ToString("yyyyMMdd-HHmmssfff",CultureInfo.InvariantCulture)+"0";
        }
        catch (ArgumentOutOfRangeException)
        { directory=Path.Combine(Root,"BLK","OUTOFRANGE"); stamp="T"+(unchecked((ulong)b.Timestamp)^0x8000000000000000UL).ToString("x16",CultureInfo.InvariantCulture); }
        return Path.Combine(directory,stamp+"-"+b.Id.ToString("D")+"-"+(archive?Binary.Hex(b.Signer):b.Hash)+(archive?".XBLK":".BLK"));
    }
    private AuthorizationSnapshot Reclassify(List<(SignedBlock Block,string Path)> inventory)
    {
        var snapshot=AuthorizationReplay.Evaluate(inventory.Select(x=>x.Block),Manifest.Binding);
        foreach (var disposition in snapshot.Blocks)
        {
            var destination=BlockPath(disposition.Block,disposition.Status!="retained");
            foreach (var item in inventory.Where(x=>x.Block.Hash==disposition.Block.Hash))
            {
                if (item.Path==destination) continue;
                if (File.Exists(destination))
                {
                    Errors.Require(Paths.ReadBounded(destination,Limits.MaxRawBlockBytes).AsSpan().SequenceEqual(item.Block.Raw),"OUTPUT_EXISTS",4,"Archive transition conflicts with an existing distinct signed variant.");
                    // Both complete copies are present and equal. The destination was already durably published.
                    File.Delete(item.Path);
                }
                else files.Move(item.Path,destination);
            }
        }
        return snapshot;
    }
    public AuthorizationSnapshot Snapshot() => Reclassify(Inventory());
    public BlockDisposition Import(byte[] raw, out bool duplicate)
    {
        EnsureConfig(); var block=BlockCodec.Parse(raw,Limits);
        Errors.Require(Manifest.Binding==null || block.LedgerId==Manifest.Binding.LedgerId,"LEDGER_IDENTITY_MISMATCH",4,"Imported block belongs to another ledger.");
        var inventory=Inventory(); duplicate=inventory.Any(x=>x.Block.Hash==block.Hash);
        var classified=AuthorizationReplay.Evaluate(inventory.Select(x=>x.Block).Append(block),Manifest.Binding).Blocks.Single(x=>x.Block.Hash==block.Hash);
        if (!duplicate)
        {
            var path=BlockPath(block,classified.Status!="retained"); files.Publish(path,block.Raw); inventory.Add((block,path));
        }
        return Reclassify(inventory).Blocks.Single(x=>x.Block.Hash==block.Hash);
    }
    private void Bind(SignedBlock genesis)
    {
        var next=Manifest with { Binding=new(genesis.LedgerId,genesis.Hash) }; files.Publish(ManifestPath,next.Encode(),true); Manifest=next;
    }
    public SignedBlock InitializeLedger()
    {
        EnsureConfig(); Errors.Require(Manifest.Binding==null,"ALREADY_BOUND",4,"Instance already bound.");
        var inventory=Inventory(); Errors.Require(!inventory.Any(x=>x.Block.IsGenesis && Binary.Hex(x.Block.Signer)==PublicKey),"GENESIS_BINDING_INCOMPLETE",4,"Existing original genesis requires explicit recovery.");
        var priorBlockRoot=Path.Combine(Root,"BLK");
        Errors.Require(!Directory.Exists(priorBlockRoot) || !Directory.EnumerateFileSystemEntries(priorBlockRoot,".uddl-*",SearchOption.AllDirectories).Any(),"RECOVERY_REQUIRED",4,"Unfinished ledger publication artifacts require explicit recovery.");
        var raw=identity.CreateBlock(Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),null);
        var genesis=Import(raw,out _).Block; files.Checkpoint("genesis-durable-before-binding"); Bind(genesis); return genesis;
    }
    public SignedBlock RecoverGenesis(Guid ledger,string hash)
    {
        EnsureConfig(); var inventory=Inventory();
        // A fully flushed original genesis temporary file can be explicitly finished, never silently replaced.
        var blockRoot=Path.Combine(Root,"BLK");
        if (Directory.Exists(blockRoot)) foreach (var temporary in Directory.EnumerateFiles(blockRoot,".uddl-*.tmp",SearchOption.AllDirectories))
        {
            SignedBlock candidate;
            try { candidate=BlockCodec.Parse(Paths.ReadBounded(temporary,Limits.MaxRawBlockBytes),Limits); } catch(LedgerException) { continue; }
            if(!candidate.IsGenesis || candidate.LedgerId!=ledger || candidate.Hash!=hash || Binary.Hex(candidate.Signer)!=PublicKey) continue;
            var destination=BlockPath(candidate,false);
            if(!File.Exists(destination)) files.Move(temporary,destination);
        }
        inventory=Inventory();
        Errors.Require(Manifest.Binding==null || Manifest.Binding==new LedgerBinding(ledger,hash),"GENESIS_MISMATCH",4,"Existing binding differs.");
        var own=inventory.Select(x=>x.Block).Where(b=>b.IsGenesis && Binary.Hex(b.Signer)==PublicKey).DistinctBy(b=>b.Hash).ToList();
        Errors.Require(own.Count<=1,"GENESIS_CONFLICT",4,"Multiple original genesis candidates.");
        var genesis=own.SingleOrDefault(b=>b.LedgerId==ledger && b.Hash==hash);
        Errors.Require(genesis!=null,"GENESIS_MISMATCH",4,"Matching original signed genesis not found."); Bind(genesis!); return genesis!;
    }
    public SignedBlock Join(Guid ledger,string hash)
    {
        EnsureConfig(); Errors.Require(Manifest.Binding==null || Manifest.Binding==new LedgerBinding(ledger,hash),"ALREADY_BOUND",4,"Instance bound to another ledger/genesis.");
        var geneses=Inventory().Select(x=>x.Block).Where(b=>b.LedgerId==ledger && b.IsGenesis).DistinctBy(b=>b.Hash).ToList();
        Errors.Require(geneses.Count==1 && geneses[0].Hash==hash,"GENESIS_MISMATCH",4,"Trusted unique signed genesis is unavailable.");
        Bind(geneses[0]); Snapshot(); return geneses[0];
    }
    private LedgerBinding Bound()
    { EnsureConfig(); return Manifest.Binding ?? throw new LedgerException("ALREADY_BOUND",4,"A trusted ledger binding is required."); }
    public BlockDisposition Publish(byte[] eventBytes, Guid? blockId=null,long? timestamp=null,bool compress=false)
    {
        var binding=Bound(); var e=EventCodec.Decode(eventBytes);
        Errors.Require(e.Kind!=0x21 || (blockId!=null && timestamp!=null),"INVALID_ARGUMENT",2,"Revocation requires fixed block GUID and timestamp.");
        var raw=identity.CreateBlock(binding.LedgerId,blockId ?? Guid.NewGuid(),timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),eventBytes,compress);
        var candidate=BlockCodec.Parse(raw,Limits); var inventory=Inventory();
        Errors.Require(!inventory.Any(x=>x.Block.Id==candidate.Id && x.Block.Hash!=candidate.Hash),"BLOCK_ID_DISPUTED",4,"Block GUID conflicts with retained bytes.");
        var state=AuthorizationReplay.Evaluate(inventory.Select(x=>x.Block).Append(candidate),binding).Blocks.Single(x=>x.Block.Hash==candidate.Hash);
        Errors.Require(state.Authorization=="authorized" && state.Status=="retained","NOT_AUTHORIZED",4,"Local signer is not authorized at the proposed canonical position.");
        return Import(raw,out _);
    }
    public (byte[] Signature,byte[] Intent) Countersign(Guid block,long timestamp,byte[] outer,byte[] target)
    {
        var binding=Bound(); var local=identity.PublicKey;
        Errors.Require(!local.AsSpan().SequenceEqual(outer) && (target.AsSpan().SequenceEqual(local) || target.AsSpan().SequenceEqual(outer)),"NOT_AUTHORIZED",4,"Invalid signer/target roles.");
        var all=Inventory().Select(x=>x.Block).ToList();
        var disputes=all.GroupBy(x=>x.Id).Where(g=>g.Select(x=>x.Hash).Distinct().Count()>1).Select(g=>g.Key).ToHashSet();
        Errors.Require(all.Where(x=>x.IsGenesis).DistinctBy(x=>x.Hash).Count()==1,"AUTHORITY_UNRESOLVED",4,"Unique trusted genesis is required.");
        var prefix=all.Where(x=>!disputes.Contains(x.Id) && (x.Timestamp<timestamp || (x.Timestamp==timestamp && (Binary.CompareGuid(x.Id,block)<0 || (x.Id==block && x.Signer.AsSpan().SequenceCompareTo(outer)<0)))));
        var snapshot=AuthorizationReplay.Evaluate(prefix,binding);
        Errors.Require(snapshot.AuthorizedKeys.Contains(PublicKey) && snapshot.AuthorizedKeys.Contains(Binary.Hex(outer)),
            all.Any(x=>Binary.Hex(x.Signer)==Binary.Hex(outer))?"NOT_AUTHORIZED":"AUTHORITY_UNRESOLVED",4,"Both distinct signers must be authorized at the proposed position.");
        var intent=Signing.RevocationIntent(binding.LedgerId,block,timestamp,outer,target,local); return (identity.Sign(intent),intent);
    }
    private Guid SelectLedger(Guid? requested, IReadOnlyList<BlockDisposition> all)
    {
        if (Manifest.Binding!=null)
        { Errors.Require(requested==null || requested==Manifest.Binding.LedgerId,"LEDGER_IDENTITY_MISMATCH",4,"Requested ledger differs from binding."); return Manifest.Binding.LedgerId; }
        if (requested!=null) return requested.Value;
        var ids=all.Select(x=>x.Block.LedgerId).Distinct().ToList(); Errors.Require(ids.Count==1,"AMBIGUOUS_LEDGER",4,"Select an explicit ledger GUID."); return ids[0];
    }
    public SignedBlock Get(Guid id,Guid? ledger=null,string? hash=null)
    {
        var snapshot=Snapshot(); var selected=SelectLedger(ledger,snapshot.Blocks); var matching=snapshot.Blocks.Where(x=>x.Block.LedgerId==selected && x.Block.Id==id).ToList();
        if (hash!=null)
        {
            var exact=matching.SingleOrDefault(x=>x.Block.Hash==hash);
            if (exact!=null && exact.Status!="retained") throw new LedgerException("BLOCK_NOT_FOUND",4,"Archived blocks are not exportable.",new { reason="ARCHIVED_NOT_EXPORTABLE" });
        }
        Errors.Require(!matching.Any(x=>x.Reason=="BLOCK_ID_DISPUTED"),"BLOCK_ID_DISPUTED",4,"Block identity is disputed.");
        var b=matching.SingleOrDefault(x=>x.Status=="retained" && (hash==null || x.Block.Hash==hash));
        Errors.Require(b!=null,"BLOCK_NOT_FOUND",4,"Active block not found."); return b!.Block;
    }
    public (Guid LedgerId,List<SignedBlock> Blocks) SelectExport(Guid? ledger=null,Guid? id=null,string? hash=null,long? from=null,long? to=null)
    {
        Errors.Require((hash==null || id!=null) && (id==null || (from==null && to==null)) && (from==null || to==null || from<=to),"INVALID_ARGUMENT",2,"Incompatible export filters.");
        var snapshot=Snapshot(); var selected=SelectLedger(ledger,snapshot.Blocks);
        if (id!=null) { var block=Get(id.Value,selected,hash); return (selected,[block]); }
        return (selected,SignedBlock.Ordered(snapshot.Blocks.Where(x=>x.Block.LedgerId==selected && x.Status=="retained").Select(x=>x.Block).Where(x=>(from==null || x.Timestamp>=from) && (to==null || x.Timestamp<=to))).ToList());
    }
    public void WriteOutput(string path,byte[] bytes) { EnsureConfig(); files.Publish(Paths.Absolute(path),bytes); }
    public void Backup(string directory)
    {
        EnsureConfig(); directory=Paths.Absolute(directory);
        Errors.Require(directory!=Root && !directory.StartsWith(Root+Path.DirectorySeparatorChar,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal),"INVALID_ARGUMENT",2,"Backup must be separate from the instance root.");
        files.Directory(directory);
        foreach (var name in new[]{"signing-key.v1.json","instance.identity.json"})
        {
            var data=Paths.ReadBounded(Path.Combine(Root,name),4096); var target=Path.Combine(directory,name);
            if (File.Exists(target)) Errors.Require(Paths.ReadBounded(target,4096).AsSpan().SequenceEqual(data),"OUTPUT_EXISTS",4,"Existing backup member differs.");
            else files.Publish(target,data);
        }
    }
    public static InstanceManifest Recover(string root,string source,bool keyOnly=false,DurableFiles? files=null)
    {
        root=Paths.Absolute(root); source=Paths.Absolute(source); files ??= new(); using var rootLock=new IdentityLease("root:"+Normalize(root));
        var sourceKey=keyOnly?source:Path.Combine(source,"signing-key.v1.json"); using var key=SigningIdentity.Decode(Paths.ReadBounded(sourceKey,4096,"IDENTITY_MISMATCH"));
        using var lease=new IdentityLease("identity:"+key.InstanceId.ToString("D")); InstanceManifest manifest;
        var manifestPath=Path.Combine(root,"instance.identity.json"); var keyPath=Path.Combine(root,"signing-key.v1.json");
        if (keyOnly)
        {
            Errors.Require(string.Equals(source,keyPath,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal),"INVALID_ARGUMENT",2,"Key-only recovery requires the canonical surviving original key.");
            manifest=new(key.InstanceId,Binary.Hex(key.PublicKey),null);
            var allowed=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"signing-key.v1.json","instance.identity.json"};
            var conflicts=Directory.EnumerateFileSystemEntries(root).Where(p=>!allowed.Contains(Path.GetFileName(p))).ToList();
            // Only a complete matching temp manifest is recoverable automatically; unknown remnants are preserved for operator diagnosis.
            foreach (var conflict in conflicts.ToArray())
            {
                if (!File.Exists(conflict) || !Path.GetFileName(conflict).StartsWith(".uddl-",StringComparison.Ordinal) || !Path.GetFileName(conflict).EndsWith(".tmp",StringComparison.Ordinal)) continue;
                try { if (InstanceManifest.Decode(Paths.ReadBounded(conflict,4096,"IDENTITY_MISMATCH"))==manifest) conflicts.Remove(conflict); } catch (LedgerException) { }
            }
            if (conflicts.Count!=0 || File.Exists(root+".DLDB")) throw new LedgerException("RECOVERY_REQUIRED",4,"Bound or conflicting initialization evidence.",new { reason="BOUND_OR_CONFLICTING_EVIDENCE" });
        }
        else
        {
            manifest=InstanceManifest.Decode(Paths.ReadBounded(Path.Combine(source,"instance.identity.json"),4096,"IDENTITY_MISMATCH")); Match(manifest,key);
            if(File.Exists(manifestPath))
            {
                InstanceManifest? current=null; try { current=InstanceManifest.Decode(Paths.ReadBounded(manifestPath,4096,"IDENTITY_MISMATCH")); } catch(LedgerException) { }
                if(current!=null)
                {
                    Errors.Require(current.InstanceId==manifest.InstanceId && current.PublicKey==manifest.PublicKey && (manifest.Binding==null || current.Binding==null || current.Binding==manifest.Binding),"IDENTITY_MISMATCH",4,"Backup conflicts with surviving original identity/binding.");
                    if(current.Binding!=null) manifest=current;
                }
            }
            if (!File.Exists(manifestPath) && Directory.Exists(Path.Combine(root,"BLK")))
            {
                Errors.Require(manifest.Binding!=null,"RECOVERY_REQUIRED",4,"Original binding evidence is required for ledger-bearing recovery.");
                var matching=SafeInventoryFiles(Path.Combine(root,"BLK")).Select(p=>BlockCodec.Parse(Paths.ReadBounded(p,16777216))).Any(b=>b.IsGenesis && b.LedgerId==manifest.Binding!.LedgerId && b.Hash==manifest.Binding.GenesisHash);
                Errors.Require(matching,"GENESIS_MISMATCH",4,"Original pinned genesis not found.");
            }
        }
        if (File.Exists(manifestPath))
        {
            InstanceManifest? existing=null;
            try { existing=InstanceManifest.Decode(Paths.ReadBounded(manifestPath,4096,"IDENTITY_MISMATCH")); } catch(LedgerException) { }
            if(existing!=null)
            {
                Errors.Require(existing.InstanceId==manifest.InstanceId && existing.PublicKey==manifest.PublicKey && (existing.Binding==null || existing.Binding==manifest.Binding),"IDENTITY_MISMATCH",4,"Existing manifest is a different original identity/binding.");
                if(existing!=manifest) files.Publish(manifestPath,manifest.Encode(),true);
            }
            else
            {
                // A corrupt manifest needs surviving original-key evidence before backup replacement.
                using var original=SigningIdentity.Decode(Paths.ReadBounded(keyPath,4096,"IDENTITY_MISMATCH")); Match(manifest,original);
                files.Move(manifestPath,Path.Combine(root,".uddl-corrupt-manifest-"+Guid.NewGuid().ToString("N")+".tmp"));
            }
        }
        if (File.Exists(keyPath))
        {
            SigningIdentity? existing=null;
            try { existing=SigningIdentity.Decode(Paths.ReadBounded(keyPath,4096,"IDENTITY_MISMATCH")); } catch(LedgerException) { }
            if(existing!=null) { using(existing) Match(manifest,existing); }
            else
            {
                Errors.Require(File.Exists(manifestPath) && InstanceManifest.Decode(Paths.ReadBounded(manifestPath,4096,"IDENTITY_MISMATCH"))==manifest,"IDENTITY_MISMATCH",4,"No surviving original manifest to authenticate key restoration.");
                files.Move(keyPath,Path.Combine(root,".uddl-corrupt-key-"+Guid.NewGuid().ToString("N")+".tmp"));
            }
        }
        if (!File.Exists(keyPath)) files.Publish(keyPath,Paths.ReadBounded(sourceKey,4096,"IDENTITY_MISMATCH"));
        if (!File.Exists(manifestPath)) files.Publish(manifestPath,manifest.Encode());
        return manifest;
    }
}

public static class Ddlb
{
    public static byte[] Encode(IReadOnlyList<SignedBlock> blocks)
    {
        using var s=new MemoryStream(); s.Write("DDLB"u8); s.Write([0,1]); Binary.WriteU64(s,(ulong)blocks.Count);
        foreach (var block in blocks) { Binary.WriteU64(s,(ulong)block.Raw.Length); s.Write(block.Raw); } return s.ToArray();
    }
}
