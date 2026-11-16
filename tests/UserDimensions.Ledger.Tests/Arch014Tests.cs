using System.Security.Cryptography;
using UserDimensions.Ledger;

internal static partial class Tests
{
    public static void RunArch014()
    {
        Check("ARCH-014 interrupted replica join retains original identity and exact trust pin",()=>
        {
            var a=Path.Combine(Scratch,"join-source"); var b=Path.Combine(Scratch,"join-interrupted");
            LedgerInstance.Initialize(a); var original=LedgerInstance.Initialize(b); SignedBlock genesis;
            using(var source=LedgerInstance.Open(a)) genesis=source.InitializeLedger();
            var key=File.ReadAllBytes(Path.Combine(b,"signing-key.v1.json"));
            var fault=new DurableFiles(n=> { if(n=="genesis-durable-before-binding") throw new IOException("Interrupted join"); });
            using(var replica=LedgerInstance.Open(b,fault))
            {
                try { replica.Join(genesis.LedgerId,genesis.Hash,genesis.Raw); throw new Exception("Missing fault"); } catch(IOException) { }
            }
            using var recovered=LedgerInstance.Open(b);
            Throws("GENESIS_BINDING_INCOMPLETE",()=>recovered.InitializeLedger());
            Assert(recovered.Manifest==original && original.PublicKey!=genesis.LedgerId);
            recovered.RecoverGenesis(genesis.LedgerId,genesis.Hash);
            Assert(recovered.RecoverGenesis(genesis.LedgerId,genesis.Hash).Hash==genesis.Hash);
            Assert(recovered.Manifest.InstanceId==original.InstanceId && recovered.PublicKey==original.PublicKey);
            Assert(File.ReadAllBytes(Path.Combine(b,"signing-key.v1.json")).SequenceEqual(key));
            Throws("GENESIS_CONFLICT",()=>recovered.RecoverGenesis(genesis.LedgerId,new string('0',128)));
            Assert(recovered.Manifest.Binding==new LedgerBinding(genesis.LedgerId,genesis.Hash));
        });
        Check("ARCH-014 wrong join evidence and legacy versions leave instance byte-identical",()=>
        {
            var seed=RandomNumberGenerator.GetBytes(32); var ledger=Binary.Hex(Signing.PublicKey(seed));
            var g=BlockCodec.Parse(BlockCodec.Create(ledger,Guid.NewGuid(),1,seed,null));
            var root=Path.Combine(Scratch,"join-reject"); LedgerInstance.Initialize(root);
            var before=Directory.GetFiles(root).ToDictionary(p=>Path.GetFileName(p),p=>Binary.Sha512(File.ReadAllBytes(p)));
            using(var replica=LedgerInstance.Open(root))
            {
                Throws("GENESIS_CONFLICT",()=>replica.Join(ledger,new string('0',128),g.Raw));
                Throws("LEDGER_IDENTITY_MISMATCH",()=>replica.Join(new string('1',64),g.Hash,g.Raw));
                foreach(var version in new byte[]{0,1,2})
                { var raw=g.Raw.ToArray(); raw[1]=version; Throws("UNSUPPORTED_VERSION",()=>replica.Import(raw,out _)); }
                var forged=BlockCodec.Create(new string('1',64),Guid.NewGuid(),1,seed,null);
                Throws("LEDGER_IDENTITY_MISMATCH",()=>replica.Import(forged,out _));
            }
            Assert(!Directory.GetDirectories(root).Any());
            Assert(Directory.GetFiles(root).All(p=>before[Path.GetFileName(p)]==Binary.Sha512(File.ReadAllBytes(p))));
            var manifest=File.ReadAllText(Path.Combine(root,"instance.identity.json")).Replace("\"version\":2","\"version\":1");
            Throws("IDENTITY_MISMATCH",()=>InstanceManifest.Decode(Binary.Utf8.GetBytes(manifest)));
        });
        Check("ARCH-014 explicit publication retry returns original time/hash/path without new bytes",()=>
        {
            var root=Path.Combine(Scratch,"publish-retry"); LedgerInstance.Initialize(root); using var instance=LedgerInstance.Open(root);
            var g=instance.InitializeLedger(); var id=Guid.NewGuid(); var body=EventCodec.Encode(new(0x11,Guid.NewGuid()));
            var first=instance.PublishForTesting(body,id,g.Timestamp+100).Block;
            var retry=instance.PublishForTesting(body,id,g.Timestamp+99999).Block;
            Assert(first.Hash==retry.Hash && first.Timestamp==retry.Timestamp && instance.Snapshot().Blocks.Count==2);
            Throws("BLOCK_GUID_CONFLICT",()=>instance.PublishForTesting(EventCodec.Encode(new(0x11,Guid.NewGuid())),id,g.Timestamp+200));
            Throws("BAD_EVENT",()=>instance.Publish(EventCodec.Encode(new(0x21,PublicKey:new byte[32],Countersigner:new byte[32],Countersignature:new byte[64]))));
        });
        Check("ARCH-014 revocation signed-time boundaries, tamper rejection and durable expiry retry",()=>
        {
            var aRoot=Path.Combine(Scratch,"consent-a"); var bRoot=Path.Combine(Scratch,"consent-b");
            LedgerInstance.Initialize(aRoot); LedgerInstance.Initialize(bRoot); using var a=LedgerInstance.Open(aRoot); using var b=LedgerInstance.Open(bRoot);
            var g=a.InitializeLedger(); b.Join(g.LedgerId,g.Hash,g.Raw); var t=g.Timestamp+100;
            var grant=a.PublishForTesting(EventCodec.Encode(new(0x20,PublicKey:Convert.FromHexString(b.PublicKey))),timestamp:t).Block;
            b.Import(grant.Raw,out _);
            foreach(var delta in new long[]{0,900000,900001,-1})
            {
                var consent=a.CreateConsentForTesting(Guid.NewGuid(),t+100,Convert.FromHexString(b.PublicKey),Convert.FromHexString(b.PublicKey));
                Assert(consent.Intent.Length==165 && consent.Encode().Length==229);
                var body=EventCodec.Encode(new(0x21,PublicKey:consent.Target,Countersigner:consent.Countersigner,Countersignature:consent.Signature,ConsentTimestamp:consent.Timestamp));
                using var bKey=SigningIdentity.Decode(File.ReadAllBytes(Path.Combine(bRoot,"signing-key.v1.json")));
                var raw=bKey.CreateBlock(g.LedgerId,consent.BlockId,t+100+delta,body);
                if(delta>=0 && delta<=900000) { var block=BlockCodec.Parse(raw); Assert(block.EventBytes.Length==137); }
                else Throws("CONSENT_EXPIRED",()=>BlockCodec.Parse(raw));
                var tampered=consent.Encode(); tampered[61]^=1; Throws("BAD_SIGNATURE",()=>RevocationConsent.Parse(tampered));
            }
            Throws("CONSENT_EXPIRED",()=>RevocationConsent.CheckInterval(long.MaxValue,long.MinValue));
            var request=a.CreateConsentForTesting(Guid.NewGuid(),t+200,Convert.FromHexString(b.PublicKey),Convert.FromHexString(b.PublicKey));
            var first=b.PublishConsentForTesting(request.Encode(),t+201).Block;
            var second=b.PublishConsentForTesting(request.Encode(),t+900001).Block;
            Assert(first.Hash==second.Hash && b.Snapshot().Blocks.Count==3);
            Assert(!b.Snapshot().AuthorizedKeys.Contains(b.PublicKey));
        });
        foreach(var blockGrant in new[]{false,true}) Check("ARCH-014 inverted batch grant durability prerequisite, blocked="+blockGrant,()=>
        {
            var seed=RandomNumberGenerator.GetBytes(32); var grantee=RandomNumberGenerator.GetBytes(32); var ledger=Binary.Hex(Signing.PublicKey(seed));
            var g=BlockCodec.Parse(BlockCodec.Create(ledger,Guid.NewGuid(),1,seed,null));
            var grant=BlockCodec.Parse(BlockCodec.Create(ledger,Guid.NewGuid(),2,seed,EventCodec.Encode(new(0x20,PublicKey:Signing.PublicKey(grantee)))));
            var dependent=BlockCodec.Parse(BlockCodec.Create(ledger,Guid.NewGuid(),3,grantee,EventCodec.Encode(new(0x11,Guid.NewGuid()))));
            var root=Path.Combine(Scratch,"batch-"+blockGrant); LedgerInstance.Initialize(root);
            using(var instance=LedgerInstance.Open(root))
            { instance.Join(ledger,g.Hash,g.Raw); if(blockGrant) Directory.CreateDirectory(instance.BlockPath(grant,false)); }
            var input=Path.Combine(Scratch,"inverted-"+blockGrant+".ddlb"); File.WriteAllBytes(input,Ddlb.Encode([dependent,grant]));
            var result=Cli(root,"block","import","--input",input);
            using var reopened=LedgerInstance.Open(root);
            if(blockGrant)
            {
                Assert(result.Exit==4 && reopened.Snapshot().Blocks.Count==1);
                var items=result.Json.GetProperty("error").GetProperty("details").GetProperty("results");
                Assert(items[0].GetProperty("reason").GetString()=="AUTHORITY_UNRESOLVED" && items[0].GetProperty("retryable").GetBoolean());
                Assert(!File.Exists(reopened.BlockPath(dependent,false)) && !File.Exists(reopened.BlockPath(dependent,true)));
            }
            else
            {
                Assert(result.Exit==0 && reopened.Snapshot().Blocks.Count==3);
                var items=result.Json.GetProperty("data").GetProperty("results");
                Assert(items[0].GetProperty("sha512").GetString()==dependent.Hash && items[1].GetProperty("sha512").GetString()==grant.Hash);
                Assert(items[0].GetProperty("durable").GetBoolean() && items[1].GetProperty("durable").GetBoolean());
            }
        });
        Check("ARCH-014 public CLI rejects authoring time and legacy ledger flags before mutation",()=>
        {
            var root=Path.Combine(Scratch,"invalid-flags");
            Assert(Cli(root,"block","publish","--event-file",Path.Combine(Scratch,"absent"),"--timestamp-utc-ms","0").Exit==2);
            Assert(Cli(root,"master","countersign-revocation","--block-guid",Guid.NewGuid().ToString("D")).Exit==2);
            Assert(Cli(root,"ledger","join","--ledger-guid",Guid.NewGuid().ToString("D")).Exit==2);
            Assert(!Directory.Exists(root));
            Assert(!typeof(LedgerInstance).GetMethods().Where(m=>m.IsPublic && m.Name is "Publish" or "CreateRevocationConsent" or "PublishRevocationConsent").SelectMany(m=>m.GetParameters()).Any(p=>p.Name is "timestamp" or "time"));
        });
        Check("ARCH-014 uncertain temporary publication prevents signing a new retry variant",()=>
        {
            var root=Path.Combine(Scratch,"uncertain-retry"); LedgerInstance.Initialize(root); SignedBlock g;
            using(var instance=LedgerInstance.Open(root)) g=instance.InitializeLedger();
            var body=EventCodec.Encode(new(0x11,Guid.NewGuid())); var id=Guid.NewGuid();
            var fault=new DurableFiles(n=> { if(n.StartsWith("before-rename:",StringComparison.Ordinal) && n.EndsWith(".BLK",StringComparison.Ordinal)) throw new IOException("Injected rename failure"); });
            using(var instance=LedgerInstance.Open(root,fault))
            {
                try { instance.PublishForTesting(body,id,g.Timestamp+1); throw new Exception("Missing rename fault"); } catch(IOException) { }
            }
            var before=Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>Binary.Sha512(File.ReadAllBytes(p)));
            using(var retry=LedgerInstance.Open(root)) Throws("DURABILITY_UNCERTAIN",()=>retry.PublishForTesting(body,id,g.Timestamp+2));
            Assert(Directory.GetFiles(root,"*",SearchOption.AllDirectories).Length==before.Count);
            Assert(before.All(x=>Binary.Sha512(File.ReadAllBytes(x.Key))==x.Value));
        });
        Check("ARCH-014 missing pinned genesis fails closed and diagnostic status preserves trust",()=>
        {
            var root=Path.Combine(Scratch,"missing-pin"); LedgerInstance.Initialize(root); SignedBlock g; string path;
            using(var instance=LedgerInstance.Open(root)) { g=instance.InitializeLedger(); path=instance.BlockPath(g,false); }
            File.Move(path,Path.Combine(Scratch,"missing-pin-preserved.blk"));
            var result=Cli(root,"status"); var data=result.Json.GetProperty("data");
            Assert(result.Exit==0 && data.GetProperty("instance").GetProperty("state").GetString()=="recovery_required");
            Assert(data.GetProperty("binding").GetProperty("genesis_sha512").GetString()==g.Hash);
            Assert(data.GetProperty("blocks").ValueKind==System.Text.Json.JsonValueKind.Null);
            using var instance2=LedgerInstance.Open(root); Throws("RECOVERY_REQUIRED",()=>instance2.Publish(EventCodec.Encode(new(0x11,Guid.NewGuid()))));
        });
    }
}
