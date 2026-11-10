using System.Buffers.Binary;
using System.Text.Json;
using UserDimensions.Ledger;

internal static partial class Tests
{
    private static (int Exit,JsonElement Json) Cli(string root,params string[] args)
    {
        using var stdout=new StringWriter(); using var stderr=new StringWriter();
        var exit=Management.Run(new[]{"--instance-root",root}.Concat(args).ToArray(),stdout,stderr);
        Assert(stdout.ToString().EndsWith('\n') && stdout.ToString().Count(c=>c=='\n')==1,"Exactly one stdout JSON object required");
        using var doc=JsonDocument.Parse(stdout.ToString()); return (exit,doc.RootElement.Clone());
    }
    public static void RunIntegration()
    {
        Check("two independent instances: publish, countersign, late revoke/regrant, exact archives",()=>
        {
            var aRoot=Path.Combine(Scratch,"masterA"); var bRoot=Path.Combine(Scratch,"masterB"); LedgerInstance.Initialize(aRoot); LedgerInstance.Initialize(bRoot);
            using var a=LedgerInstance.Open(aRoot); using var b=LedgerInstance.Open(bRoot);
            var genesis=a.InitializeLedger(); b.Import(genesis.Raw,out _); b.Join(genesis.LedgerId,genesis.Hash); var t=genesis.Timestamp;
            var body=EventCodec.Encode(new(0x11,Guid.NewGuid()));
            Throws("NOT_AUTHORIZED",()=>b.Publish(body,timestamp:t+300));
            var grant=a.Publish(EventCodec.Encode(new(0x20,PublicKey:Convert.FromHexString(b.PublicKey))),timestamp:t+100).Block;
            b.Import(grant.Raw,out _);
            var data=b.Publish(body,timestamp:t+300).Block; a.Import(data.Raw,out _);
            var revId=Guid.NewGuid(); var consent=b.Countersign(revId,t+200,Convert.FromHexString(a.PublicKey),Convert.FromHexString(b.PublicKey));
            var revocation=a.Publish(EventCodec.Encode(new(0x21,PublicKey:Convert.FromHexString(b.PublicKey),Countersigner:Convert.FromHexString(b.PublicKey),Countersignature:consent.Signature)),revId,t+200).Block;
            var archived=a.Snapshot().Blocks.Single(x=>x.Block.Hash==data.Hash);
            Assert(archived.Status=="quarantined" && archived.Reason=="UNAUTHORIZED_AT_POSITION");
            Assert(File.ReadAllBytes(a.BlockPath(data,true)).SequenceEqual(data.Raw));
            b.Import(revocation.Raw,out _); Throws("NOT_AUTHORIZED",()=>b.Publish(body,timestamp:t+400));
            Throws("BLOCK_NOT_FOUND",()=>a.Get(data.Id,hash:data.Hash));
            var regrant=a.Publish(EventCodec.Encode(new(0x20,PublicKey:Convert.FromHexString(b.PublicKey))),timestamp:t+250).Block;
            b.Import(regrant.Raw,out _); Assert(a.Get(data.Id).Raw.SequenceEqual(data.Raw));
            a.Import(data.Raw,out var duplicate); Assert(duplicate && a.Snapshot().Blocks.Count==5);
            Assert(!Directory.Exists(Path.Combine(aRoot,"STAT")) && !File.Exists(aRoot+".DLDB"));
        });
        Check("pending unknown signer promoted on late original grant",()=>
        {
            var root=Path.Combine(Scratch,"pending"); LedgerInstance.Initialize(root); using var instance=LedgerInstance.Open(root);
            var genesis=instance.InitializeLedger(); var seed=Enumerable.Repeat((byte)87,32).ToArray(); var id=Guid.NewGuid();
            var raw=BlockCodec.Create(genesis.LedgerId,id,genesis.Timestamp+300,seed,EventCodec.Encode(new(0x11,Guid.NewGuid())));
            var initial=instance.Import(raw,out _); Assert(initial.Status=="pending");
            instance.Publish(EventCodec.Encode(new(0x20,PublicKey:Signing.PublicKey(seed))),timestamp:genesis.Timestamp+200);
            Assert(instance.Get(id).Raw.SequenceEqual(raw));
        });
        Check("authenticated same-ID variants quarantine all, counters include both",()=>
        {
            var root=Path.Combine(Scratch,"disputed"); LedgerInstance.Initialize(root); using var instance=LedgerInstance.Open(root);
            var genesis=instance.InitializeLedger(); var other=Enumerable.Repeat((byte)88,32).ToArray();
            instance.Publish(EventCodec.Encode(new(0x20,PublicKey:Signing.PublicKey(other))),timestamp:genesis.Timestamp+100);
            var id=Guid.NewGuid(); var body=EventCodec.Encode(new(0x11,Guid.NewGuid()));
            var first=instance.Publish(body,id,genesis.Timestamp+200).Block;
            var second=BlockCodec.Create(genesis.LedgerId,id,genesis.Timestamp+200,other,body); instance.Import(second,out _);
            var snapshot=instance.Snapshot(); Assert(snapshot.Blocks.Count==4 && snapshot.Blocks.Count(x=>x.Reason=="BLOCK_ID_DISPUTED")==2);
            Assert(File.Exists(instance.BlockPath(first,true))); Throws("BLOCK_ID_DISPUTED",()=>instance.Get(id));
            Assert(instance.SelectExport().Blocks.Count==2);
        });
        Check("original-key backup, loss and restoration preserve binding",()=>
        {
            var root=Path.Combine(Scratch,"restore"); var backup=Path.Combine(Scratch,"backup"); LedgerInstance.Initialize(root); InstanceManifest manifest;
            using(var instance=LedgerInstance.Open(root)) { instance.InitializeLedger(); manifest=instance.Manifest; instance.Backup(backup); }
            File.Delete(Path.Combine(root,"signing-key.v1.json")); Throws("RECOVERY_REQUIRED",()=>LedgerInstance.Open(root));
            Assert(LedgerInstance.Recover(root,backup)==manifest);
            File.WriteAllText(Path.Combine(root,"signing-key.v1.json"),"corrupt"); Assert(LedgerInstance.Recover(root,backup)==manifest);
            using var reopened=LedgerInstance.Open(root); Assert(reopened.Manifest==manifest);
        });
        Check("durable genesis interrupted binding: explicit idempotent repair",()=>
        {
            var root=Path.Combine(Scratch,"genesis-crash"); LedgerInstance.Initialize(root);
            var fault=new DurableFiles(n=>{if(n=="genesis-durable-before-binding") throw new IOException("Injected interruption");});
            using(var instance=LedgerInstance.Open(root,fault)) { try { instance.InitializeLedger(); } catch(IOException) { } }
            using var retry=LedgerInstance.Open(root); var g=retry.Snapshot().Blocks.Single().Block;
            Throws("GENESIS_BINDING_INCOMPLETE",()=>retry.InitializeLedger()); Assert(retry.RecoverGenesis(g.LedgerId,g.Hash).Hash==g.Hash);
            Assert(retry.RecoverGenesis(g.LedgerId,g.Hash).Hash==g.Hash);
        });
        Check("canonical GUID before signer ordering and OUTOFRANGE extremes",()=>
        {
            var seed=Enumerable.Repeat((byte)3,32).ToArray(); var ledger=Guid.NewGuid();
            var g=BlockCodec.Parse(BlockCodec.Create(ledger,Guid.Parse("00000000-0000-4000-8000-000000000001"),0,seed,null));
            var id=Guid.Parse("00000000-0000-4000-8000-000000000002");
            var b=BlockCodec.Parse(BlockCodec.Create(ledger,id,0,seed,EventCodec.Encode(new(0x11,Guid.NewGuid()))));
            Assert(AuthorizationReplay.Evaluate(new[]{b,g}).Blocks.Single(x=>x.Block==b).Status=="retained");
            var root=Path.Combine(Scratch,"range"); LedgerInstance.Initialize(root); using var instance=LedgerInstance.Open(root);
            foreach(var timestamp in new[]{long.MinValue,long.MaxValue}) { var raw=BlockCodec.Create(ledger,Guid.NewGuid(),timestamp,seed,null); var block=BlockCodec.Parse(raw); Assert(instance.BlockPath(block,false).Contains(timestamp<0?"T0000000000000000":"Tffffffffffffffff")); }
        });
        Check("CLI exact envelopes, DDLB get/export distinction, duplicate and partial imports",()=>
        {
            var root=Path.Combine(Scratch,"cli"); Assert(Cli(root,"instance","init").Exit==0); var result=Cli(root,"ledger","init"); Assert(result.Exit==0);
            var id=result.Json.GetProperty("data").GetProperty("genesis_block_guid").GetString()!;
            var rawPath=Path.Combine(Scratch,"single.blk"); var exportPath=Path.Combine(Scratch,"batch.ddlb");
            Assert(Cli(root,"block","get","--id",id,"--output",rawPath).Exit==0); Assert(new FileInfo(rawPath).Length==211);
            Assert(Cli(root,"block","export","--output",exportPath).Exit==0); var exported=File.ReadAllBytes(exportPath); Assert(exported.AsSpan(0,4).SequenceEqual("DDLB"u8) && exported.Length==233);
            var duplicate=Cli(root,"block","import","--input",exportPath); Assert(duplicate.Exit==0 && duplicate.Json.GetProperty("data").GetProperty("duplicate_count").GetInt32()==1);
            var malformed=exported.Concat(new byte[]{55}).ToArray(); var malformedPath=Path.Combine(Scratch,"partial.ddlb"); File.WriteAllBytes(malformedPath,malformed);
            var partial=Cli(root,"block","import","--input",malformedPath); Assert(partial.Exit==3 && partial.Json.GetProperty("error").GetProperty("code").GetString()=="PARTIAL_IMPORT");
            var status=Cli(root,"status"); Assert(status.Exit==0 && status.Json.GetProperty("data").GetProperty("projection").GetProperty("state").GetString()=="not_installed");
            Assert(Cli(root,"block","export","--output",exportPath).Exit==4);
            Assert(Cli(root,"config","set","--name","max_raw_block_bytes","--value","0").Exit==2);
            Assert(Cli(root,"status","--unknown","x").Exit==2);
        });
    }
}
