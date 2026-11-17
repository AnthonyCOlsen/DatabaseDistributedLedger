using System.Security.Cryptography;
using System.Text.Json;
using UserDimensions.Ledger;

internal static partial class Tests
{
    private sealed record Qa014Publication(string Root,byte[] ParentSeed,byte[] OwnerKey,SignedBlock Original,
        SignedBlock Late,string[] Command,string Kind);

    private static string[] Qa014Inventory(string root) => Directory.GetFiles(root,"*",SearchOption.AllDirectories)
        .Select(p=>"F:"+Path.GetRelativePath(root,p)+":"+Binary.Sha512(File.ReadAllBytes(p)))
        .Concat(Directory.GetDirectories(root,"*",SearchOption.AllDirectories).Select(p=>"D:"+Path.GetRelativePath(root,p)))
        .Order(StringComparer.Ordinal).ToArray();

    private static SignedBlock Qa014Revoke(string ledger,long time,byte[] outerSeed,byte[] target,byte[] counter,Func<byte[],byte[]> countersign)
    {
        var id=Guid.NewGuid();var intent=Signing.RevocationIntent(ledger,id,time,Signing.PublicKey(outerSeed),target,counter);
        return BlockCodec.Parse(BlockCodec.Create(ledger,id,time,outerSeed,EventCodec.Encode(new(0x21,
            PublicKey:target,Countersigner:counter,Countersignature:countersign(intent),ConsentTimestamp:time))));
    }

    private static Qa014Publication Qa014ActivePublication(string kind)
    {
        var root=Path.Combine(Scratch,"qa014-archive-"+kind);LedgerInstance.Initialize(root);
        using var instance=LedgerInstance.Open(root);
        using var owner=SigningIdentity.Decode(File.ReadAllBytes(Path.Combine(root,"signing-key.v1.json")));
        var parentSeed=RandomNumberGenerator.GetBytes(32);var middleSeed=RandomNumberGenerator.GetBytes(32);
        var ledger=Binary.Hex(Signing.PublicKey(parentSeed));
        var genesis=BlockCodec.Parse(BlockCodec.Create(ledger,Guid.NewGuid(),0,parentSeed,null));
        instance.Join(ledger,genesis.Hash,genesis.Raw);
        var grantee=kind=="pending"?Signing.PublicKey(middleSeed):owner.PublicKey;
        instance.Import(BlockCodec.Create(ledger,Guid.NewGuid(),100,parentSeed,EventCodec.Encode(new(0x20,PublicKey:grantee))),out _);
        if(kind=="pending") instance.Import(BlockCodec.Create(ledger,Guid.NewGuid(),150,middleSeed,
            EventCodec.Encode(new(0x20,PublicKey:owner.PublicKey))),out _);
        SignedBlock original;string[] command;
        if(kind=="revocation")
        {
            var id=Guid.NewGuid();var parentKey=Signing.PublicKey(parentSeed);
            var intent=Signing.RevocationIntent(ledger,id,200,owner.PublicKey,owner.PublicKey,parentKey);
            var consent=new RevocationConsent(ledger,id,200,owner.PublicKey,owner.PublicKey,parentKey,Signing.Sign(parentSeed,intent));
            var input=Path.Combine(Scratch,"qa014-original-consent.bin");File.WriteAllBytes(input,consent.Encode());
            original=instance.PublishConsentForTesting(consent.Encode(),201).Block;
            command=["block","publish","--revocation-consent-file",input];
        }
        else
        {
            var body=EventCodec.Encode(new(0x11,Guid.NewGuid()));var id=Guid.NewGuid();
            var input=Path.Combine(Scratch,"qa014-original-"+kind+".event");File.WriteAllBytes(input,body);
            original=instance.PublishForTesting(body,id,300).Block;
            command=["block","publish","--event-file",input,"--block-guid",id.ToString("D")];
        }
        var late=kind=="pending"
            ? Qa014Revoke(ledger,125,parentSeed,Signing.PublicKey(middleSeed),Signing.PublicKey(middleSeed),bytes=>Signing.Sign(middleSeed,bytes))
            : Qa014Revoke(ledger,kind=="revocation"?150:200,parentSeed,owner.PublicKey,owner.PublicKey,owner.Sign);
        return new(root,parentSeed,owner.PublicKey.ToArray(),original,late,command,kind);
    }

    private static string Qa014Archive(Qa014Publication test)
    {
        using var instance=LedgerInstance.Open(test.Root);instance.Import(test.Late.Raw,out _);
        var state=instance.Snapshot().Blocks.Single(x=>x.Block.Hash==test.Original.Hash);
        Assert(state.Status==(test.Kind=="pending"?"pending":"quarantined"));
        var path=instance.BlockPath(test.Original,true);
        Assert(File.ReadAllBytes(path).SequenceEqual(test.Original.Raw));return path;
    }

    private static void Qa014SameFields(JsonElement left,JsonElement right) => Assert(
        left.EnumerateObject().OrderBy(x=>x.Name,StringComparer.Ordinal).Select(x=>x.Name+"="+x.Value.GetRawText())
            .SequenceEqual(right.EnumerateObject().OrderBy(x=>x.Name,StringComparer.Ordinal).Select(x=>x.Name+"="+x.Value.GetRawText())),
        "Historical publication must equal the original active response, without added/missing fields");

    private static (string Root,SignedBlock Genesis,SignedBlock Block,string EventFile,string Input) Qa014Data(string name)
    {
        var root=Path.Combine(Scratch,"qa014-data-"+name);LedgerInstance.Initialize(root);
        using var instance=LedgerInstance.Open(root);var genesis=instance.InitializeLedger();
        var body=EventCodec.Encode(new(0x11,Guid.NewGuid()));var block=instance.PublishForTesting(body,timestamp:genesis.Timestamp+1).Block;
        var input=Path.Combine(Scratch,"qa014-data-"+name+".BLK");
        using var key=SigningIdentity.Decode(File.ReadAllBytes(Path.Combine(root,"signing-key.v1.json")));
        File.WriteAllBytes(input,key.CreateBlock(genesis.LedgerId,Guid.NewGuid(),genesis.Timestamp+2,body));
        var eventFile=Path.Combine(Scratch,"qa014-data-"+name+".event");File.WriteAllBytes(eventFile,body);
        return (root,genesis,block,eventFile,input);
    }

    public static void RunQa014Resolution()
    {
        Directory.CreateDirectory(Scratch);
        foreach(var kind in new[]{"quarantined","pending","revocation"}) Check("QA-014 archived "+kind+" retry returns exact metadata without mutation",()=>
        {
            var test=Qa014ActivePublication(kind);var original=Cli(test.Root,test.Command);Assert(original.Exit==0);
            var archive=Qa014Archive(test);var before=Qa014Inventory(test.Root);
            var response=Cli(test.Root,test.Command);Assert(response.Exit==4);
            var error=response.Json.GetProperty("error");Assert(error.GetProperty("code").GetString()=="BLOCK_ARCHIVED" && !error.GetProperty("retryable").GetBoolean());
            var details=error.GetProperty("details");Assert(details.EnumerateObject().Count()==4);
            Qa014SameFields(details.GetProperty("original_publication"),original.Json.GetProperty("data"));
            var current=details.GetProperty("current");Assert(current.EnumerateObject().Count()==6);
            Assert(current.GetProperty("state").GetString()=="archived" && current.GetProperty("status").GetString()==(kind=="pending"?"pending":"quarantined"));
            Assert(current.GetProperty("authorization").GetString()==(kind=="pending"?"pending":"unauthorized"));
            Assert(current.GetProperty("reason").GetString()==(kind=="pending"?"PENDING_AUTHORITY":"UNAUTHORIZED_AT_POSITION"));
            Assert(current.GetProperty("path").GetString()==archive && current.GetProperty("retained").GetBoolean());
            Assert(!details.GetProperty("new_publication").GetBoolean() && !details.GetProperty("mutated").GetBoolean());
            Assert(Qa014Inventory(test.Root).SequenceEqual(before));
            using(var instance=LedgerInstance.Open(test.Root,new DurableFiles(_=>throw new Exception("Retry reached a mutation callback"))))
            {
                try
                {
                    if(kind=="revocation") instance.PublishRevocationConsent(File.ReadAllBytes(test.Command[3]));
                    else instance.Publish(File.ReadAllBytes(test.Command[3]),test.Original.Id);
                    throw new Exception("Expected BLOCK_ARCHIVED from library");
                }
                catch(LedgerException e) { Assert(e.Code=="BLOCK_ARCHIVED" && e.ExitCode==4); }
            }
            // Reconciliation is explicit history import, separate from the read-only retry.
            using(var instance=LedgerInstance.Open(test.Root))
            {
                var restore=BlockCodec.Create(test.Original.LedgerId,Guid.NewGuid(),kind=="revocation"?175:250,test.ParentSeed,
                    EventCodec.Encode(new(0x20,PublicKey:test.OwnerKey)));
                instance.Import(restore,out _);
            }
            var restoredBefore=Qa014Inventory(test.Root);var restored=Cli(test.Root,test.Command);Assert(restored.Exit==0);
            Qa014SameFields(restored.Json.GetProperty("data"),original.Json.GetProperty("data"));
            Assert(Qa014Inventory(test.Root).SequenceEqual(restoredBefore));
        });
        Check("QA-014 archived retry preserves intent conflicts, new-publication authority and identity gates",()=>
        {
            var test=Qa014ActivePublication("gates");Qa014Archive(test);var before=Qa014Inventory(test.Root);
            var different=Path.Combine(Scratch,"qa014-different.event");File.WriteAllBytes(different,EventCodec.Encode(new(0x11,Guid.NewGuid())));
            var conflict=Cli(test.Root,"block","publish","--event-file",different,"--block-guid",test.Original.Id.ToString("D"));
            Assert(conflict.Exit==4 && conflict.Json.GetProperty("error").GetProperty("code").GetString()=="BLOCK_GUID_CONFLICT");
            var fresh=Cli(test.Root,"block","publish","--event-file",test.Command[3]);Assert(fresh.Exit==4 && fresh.Json.GetProperty("error").GetProperty("code").GetString()=="NOT_AUTHORIZED");
            Assert(Qa014Inventory(test.Root).SequenceEqual(before));
            File.Move(Path.Combine(test.Root,"signing-key.v1.json"),Path.Combine(Scratch,"qa014-preserved-original-key.json"));
            before=Qa014Inventory(test.Root);var missing=Cli(test.Root,test.Command);
            Assert(missing.Exit==4 && missing.Json.GetProperty("error").GetProperty("code").GetString()=="RECOVERY_REQUIRED");
            Assert(Qa014Inventory(test.Root).SequenceEqual(before));
        });
        Check("QA-014 corrupt retained history gates normal commands while preserving healthy identity and bytes",()=>
        {
            var test=Qa014Data("recovery");string path;
            using(var instance=LedgerInstance.Open(test.Root)) path=instance.BlockPath(test.Block,false);
            var raw=File.ReadAllBytes(path);raw[163]^=1;File.WriteAllBytes(path,raw);var before=Qa014Inventory(test.Root);
            var batch=Path.Combine(Scratch,"qa014-valid.DDLB");File.WriteAllBytes(batch,Ddlb.Encode([BlockCodec.Parse(File.ReadAllBytes(test.Input))]));
            string[][] commands=[
                ["block","get","--id",test.Block.Id.ToString("D"),"--output",Path.Combine(Scratch,"qa014-bad-get.BLK")],
                ["block","export","--output",Path.Combine(Scratch,"qa014-bad-export.DDLB")],
                ["block","publish","--event-file",test.EventFile,"--block-guid",test.Block.Id.ToString("D")],
                ["block","import","--input",test.Input],["block","import","--input",batch]];
            foreach(var command in commands)
            {
                var response=Cli(test.Root,command);var error=response.Json.GetProperty("error");
                Assert(response.Exit==4 && error.GetProperty("code").GetString()=="RECOVERY_REQUIRED");
                Assert(error.GetProperty("details").GetProperty("reason").GetString()=="LEDGER_HISTORY_CORRUPT");
                Assert(Qa014Inventory(test.Root).SequenceEqual(before));
            }
            var status=Cli(test.Root,"status").Json.GetProperty("data");
            Assert(status.GetProperty("signing").GetProperty("available").GetBoolean() && !status.GetProperty("signing").GetProperty("authorized").GetBoolean());
            Assert(status.GetProperty("blocks").ValueKind==JsonValueKind.Null && status.GetProperty("peers").ValueKind==JsonValueKind.Null);
            Assert(status.GetProperty("instance").GetProperty("recovery_codes").EnumerateArray().Any(x=>x.GetString()=="LEDGER_HISTORY_CORRUPT"));
            Assert(status.GetProperty("binding").GetProperty("genesis_sha512").GetString()==test.Genesis.Hash);
            var identity=Cli(test.Root,"instance","status").Json.GetProperty("data");Assert(identity.GetProperty("signing_available").GetBoolean());
            Assert(Qa014Inventory(test.Root).SequenceEqual(before) && File.ReadAllBytes(path).SequenceEqual(raw));
            Assert(!File.Exists(Path.Combine(Scratch,"qa014-bad-get.BLK")) && !File.Exists(Path.Combine(Scratch,"qa014-bad-export.DDLB")));
        });
        Check("QA-014 new incoming frame, hash, signature and version errors are not remapped",()=>
        {
            var test=Qa014Data("incoming");var valid=File.ReadAllBytes(test.Input);string retained;
            using(var instance=LedgerInstance.Open(test.Root)) retained=instance.BlockPath(test.Block,false);
            foreach(var corruptStore in new[]{false,true})
            {
                if(corruptStore) { var bytes=File.ReadAllBytes(retained);bytes[163]^=1;File.WriteAllBytes(retained,bytes); }
                foreach(var kind in new[]{"frame","hash","signature","version"})
                {
                    var bytes=valid.ToArray();var code="BAD_FRAME";
                    if(kind=="frame") bytes=bytes[..^1];
                    if(kind=="hash") { bytes[90]^=1;code="HASH_MISMATCH"; }
                    if(kind=="signature") { bytes[^1]^=1;code="BAD_SIGNATURE"; }
                    if(kind=="version") { bytes[1]=2;code="UNSUPPORTED_VERSION"; }
                    var input=Path.Combine(Scratch,"qa014-new-"+kind+".bin");File.WriteAllBytes(input,bytes);
                    var before=Qa014Inventory(test.Root);var response=Cli(test.Root,"block","import","--input",input);
                    Assert(response.Exit==2 && response.Json.GetProperty("error").GetProperty("code").GetString()==code);
                    Assert(Qa014Inventory(test.Root).SequenceEqual(before));
                }
            }
        });
        foreach(var kind in new[]{"truncated","empty","signature","hash","version","filename"}) Check("QA-014 retained "+kind+" corruption has a contextual recovery diagnostic",()=>
        {
            var test=Qa014Data(kind);string path;
            using(var instance=LedgerInstance.Open(test.Root)) path=instance.BlockPath(test.Block,false);
            var bytes=test.Block.Raw.ToArray();
            if(kind=="truncated") bytes=bytes[..^1];
            if(kind=="empty") bytes=[];
            if(kind=="signature") bytes[^1]^=1;
            if(kind=="hash") bytes[90]^=1;
            if(kind=="version") bytes[1]=2;
            if(kind=="filename") bytes=File.ReadAllBytes(test.Input);
            File.WriteAllBytes(path,bytes);var before=Qa014Inventory(test.Root);
            using var instance2=LedgerInstance.Open(test.Root);
            try { instance2.Snapshot();throw new Exception("Expected retained-history recovery gate"); }
            catch(LedgerException e)
            {
                var details=JsonSerializer.SerializeToElement(e.Details);
                Assert(e.Code=="RECOVERY_REQUIRED" && e.ExitCode==4 && details.GetProperty("reason").GetString()=="LEDGER_HISTORY_CORRUPT");
                Assert(details.GetProperty("path").GetString()==path);
            }
            Assert(Qa014Inventory(test.Root).SequenceEqual(before));
        });
        Check("QA-014 configured resource limits do not mislabel valid retained bytes as corruption",()=>
        {
            var test=Qa014Data("resource");using(var instance=LedgerInstance.Open(test.Root)) instance.Configure("max_raw_block_bytes",227);
            var before=Qa014Inventory(test.Root);var response=Cli(test.Root,"block","get","--id",test.Block.Id.ToString("D"),"--output",Path.Combine(Scratch,"qa014-limited.BLK"));
            Assert(response.Exit==5 && response.Json.GetProperty("error").GetProperty("code").GetString()=="RESOURCE_LIMIT");
            Assert(Qa014Inventory(test.Root).SequenceEqual(before));
        });
    }
}
