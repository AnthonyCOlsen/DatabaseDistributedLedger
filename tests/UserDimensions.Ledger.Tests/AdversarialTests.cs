using System.Security.Cryptography;
using System.Text.Json;
using UserDimensions.Ledger;

internal static partial class Tests
{
    public static void RunAdversarial()
    {
        Check("explicit original backup restores two corrupt records using surviving genesis evidence",()=>
        {
            var root=Path.Combine(Scratch,"two-corrupt"); var backup=Path.Combine(Scratch,"two-corrupt-backup"); LedgerInstance.Initialize(root); InstanceManifest original;
            using(var instance=LedgerInstance.Open(root)) { instance.InitializeLedger(); original=instance.Manifest; instance.Backup(backup); }
            File.WriteAllText(Path.Combine(root,"instance.identity.json"),"{}"); File.WriteAllText(Path.Combine(root,"signing-key.v1.json"),"{}");
            Assert(LedgerInstance.Recover(root,backup)==original);
            using var reopened=LedgerInstance.Open(root); Assert(reopened.Manifest==original && reopened.Snapshot().Blocks.Count==1);
        });
        Check("key-only recovery rejects corrupted or previously bound manifest evidence",()=>
        {
            var root=Path.Combine(Scratch,"never-unbind"); LedgerInstance.Initialize(root); InstanceManifest bound;
            using(var instance=LedgerInstance.Open(root)) { instance.InitializeLedger(); bound=instance.Manifest; }
            Throws("RECOVERY_REQUIRED",()=>LedgerInstance.Recover(root,Path.Combine(root,"signing-key.v1.json"),true));
            Assert(InstanceManifest.Decode(File.ReadAllBytes(Path.Combine(root,"instance.identity.json")))==bound);
            File.WriteAllText(Path.Combine(root,"instance.identity.json"),"{}");
            Throws("RECOVERY_REQUIRED",()=>LedgerInstance.Recover(root,Path.Combine(root,"signing-key.v1.json"),true));
        });
        Check("CLI Phase 1 command walkthrough including countersigning, backup, restore and config",()=>
        {
            var a=Path.Combine(Scratch,"cli-master"); var b=Path.Combine(Scratch,"cli-replica");
            var ai=Cli(a,"instance","init"); var bi=Cli(b,"instance","init"); Assert(ai.Exit==0 && bi.Exit==0);
            var apk=ai.Json.GetProperty("data").GetProperty("public_key_hex").GetString()!;
            var bpk=bi.Json.GetProperty("data").GetProperty("public_key_hex").GetString()!;
            var initialized=Cli(a,"ledger","init"); Assert(initialized.Exit==0); var gd=initialized.Json.GetProperty("data");
            var ledger=gd.GetProperty("ledger_guid").GetString()!; var gh=gd.GetProperty("genesis_sha512").GetString()!;
            Assert(Cli(a,"ledger","recover-genesis","--ledger-guid",ledger,"--genesis-sha512",gh).Exit==0);
            var genesisFile=Path.Combine(Scratch,"cli-genesis.blk"); Assert(Cli(a,"block","get","--id",gd.GetProperty("genesis_block_guid").GetString()!,"--output",genesisFile).Exit==0);
            var g=BlockCodec.Parse(File.ReadAllBytes(genesisFile)); var time=g.Timestamp;
            Assert(Cli(b,"block","import","--input",genesisFile).Exit==0);
            Assert(Cli(b,"ledger","join","--ledger-guid",ledger,"--genesis-sha512",gh).Exit==0);
            var grantFile=Path.Combine(Scratch,"cli-grant.event"); File.WriteAllBytes(grantFile,EventCodec.Encode(new(0x20,PublicKey:Convert.FromHexString(bpk))));
            var grant=Cli(a,"block","publish","--event-file",grantFile,"--timestamp-utc-ms",(time+100).ToString(System.Globalization.CultureInfo.InvariantCulture)); Assert(grant.Exit==0);
            Assert(Cli(b,"block","import","--input",grant.Json.GetProperty("data").GetProperty("path").GetString()!).Exit==0);
            var revId=Guid.NewGuid().ToString("D"); var timestamp=(time+200).ToString(System.Globalization.CultureInfo.InvariantCulture); var consentFile=Path.Combine(Scratch,"cli-consent.sig");
            var consent=Cli(b,"master","countersign-revocation","--block-guid",revId,"--timestamp-utc-ms",timestamp,"--outer-signer-public-key",apk,"--revoked-public-key",bpk,"--output",consentFile);
            Assert(consent.Exit==0 && new FileInfo(consentFile).Length==64);
            var revokeFile=Path.Combine(Scratch,"cli-revoke.event"); File.WriteAllBytes(revokeFile,EventCodec.Encode(new(0x21,PublicKey:Convert.FromHexString(bpk),Countersigner:Convert.FromHexString(bpk),Countersignature:File.ReadAllBytes(consentFile))));
            var revoke=Cli(a,"block","publish","--event-file",revokeFile,"--block-guid",revId,"--timestamp-utc-ms",timestamp); Assert(revoke.Exit==0);
            Assert(Cli(b,"block","import","--input",revoke.Json.GetProperty("data").GetProperty("path").GetString()!).Exit==0);
            Assert(Cli(b,"config","set","--name","per_block_timeout_seconds","--value","30").Exit==0);
            Assert(Cli(b,"config","get").Json.GetProperty("data").GetProperty("limits").GetProperty("per_block_timeout_seconds").GetInt32()==30);
            var backup=Path.Combine(Scratch,"cli-backup"); Assert(Cli(b,"key","backup","--directory",backup).Exit==0);
            File.Delete(Path.Combine(b,"signing-key.v1.json"));
            Assert(Cli(b,"instance","status").Json.GetProperty("data").GetProperty("state").GetString()=="recovery_required");
            Assert(Cli(b,"block","export","--output",Path.Combine(Scratch,"forbidden.ddlb")).Exit==4);
            Assert(Cli(b,"key","restore","--directory",backup).Exit==0);
            Assert(Cli(b,"instance","recover","--from",backup).Exit==0);
            Assert(Cli(b,"instance","status").Json.GetProperty("data").GetProperty("public_key_hex").GetString()==bpk);
            var emptyFile=Path.Combine(Scratch,"empty.ddlb");
            Assert(Cli(a,"block","export","--output",emptyFile,"--from-timestamp-ms",long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)).Exit==0);
            Assert(new FileInfo(emptyFile).Length==14);
        });
        Check("no forged payload hash/signature/frame reaches durable retention",()=>
        {
            var root=Path.Combine(Scratch,"adversarial"); LedgerInstance.Initialize(root); byte[] raw;
            using(var instance=LedgerInstance.Open(root)) { var g=instance.InitializeLedger(); raw=instance.Publish(EventCodec.Encode(new(0x11,Guid.NewGuid())),timestamp:g.Timestamp+1).Block.Raw; }
            var mutations=new[]{(74,"HASH_MISMATCH"),(147,"HASH_MISMATCH"),(raw.Length-1,"BAD_SIGNATURE"),(138,"BAD_FRAME"),(0,"UNSUPPORTED_VERSION")};
            foreach(var (index,code) in mutations)
            {
                var changed=raw.ToArray(); changed[index]^=2; var path=Path.Combine(Scratch,"bad-"+index+".blk"); File.WriteAllBytes(path,changed);
                var result=Cli(root,"block","import","--input",path); Assert(result.Exit==2 && result.Json.GetProperty("error").GetProperty("code").GetString()==code);
            }
            using var reopened=LedgerInstance.Open(root); Assert(reopened.Snapshot().Blocks.Count==2);
        });
        Check("all representative signed REAL NaNs rejected and infinity/-zero retained",()=>
        {
            var table=Guid.NewGuid(); var row=Guid.NewGuid(); var col=Guid.NewGuid();
            foreach(var bits in new ulong[]{0x7ff0000000000001,0x7ff8000000000000,0xfff0000000000001,0xfff8000000000000})
            {
                using var s=new MemoryStream(); s.WriteByte(2); s.Write(Binary.GuidBytes(table)); s.Write(Binary.GuidBytes(row)); s.WriteByte(1); s.Write(Binary.GuidBytes(col)); s.WriteByte(2); Binary.WriteU64(s,bits);
                Throws("BAD_EVENT",()=>EventCodec.Decode(s.ToArray()));
            }
        });
        Check("configuration corruption diagnostic preserves healthy identity state",()=>
        {
            var root=Path.Combine(Scratch,"bad-config-status"); LedgerInstance.Initialize(root);
            File.WriteAllText(Path.Combine(root,"instance.config.json"),"{}");
            var status=Cli(root,"status"); Assert(status.Exit==0 && status.Json.GetProperty("data").GetProperty("instance").GetProperty("state").GetString()=="unbound");
            Assert(Cli(root,"config","get").Exit==4); Assert(Cli(root,"ledger","init").Exit==4);
        });
        Check("unbound multi-ledger selection and binding preserve unrelated retained history",()=>
        {
            var root=Path.Combine(Scratch,"multi-ledger"); LedgerInstance.Initialize(root);
            using var instance=LedgerInstance.Open(root); var seed=Enumerable.Repeat((byte)19,32).ToArray();
            var a=BlockCodec.Parse(BlockCodec.Create(Guid.NewGuid(),Guid.NewGuid(),1,seed,null)); var b=BlockCodec.Parse(BlockCodec.Create(Guid.NewGuid(),Guid.NewGuid(),1,seed,null));
            instance.Import(a.Raw,out _); instance.Import(b.Raw,out _); Throws("AMBIGUOUS_LEDGER",()=>instance.SelectExport());
            Assert(instance.SelectExport(a.LedgerId).Blocks.Count==1); var bPath=instance.BlockPath(b,false);
            instance.Join(a.LedgerId,a.Hash); Assert(instance.Snapshot().Blocks.Count==1 && File.Exists(bPath));
        });
        Check("backup taken before binding restores original key without erasing later genesis pin",()=>
        {
            var root=Path.Combine(Scratch,"early-backup"); var backup=Path.Combine(Scratch,"early-backup-copy"); LedgerInstance.Initialize(root); InstanceManifest bound;
            using(var instance=LedgerInstance.Open(root)) { instance.Backup(backup); instance.InitializeLedger(); bound=instance.Manifest; }
            File.Delete(Path.Combine(root,"signing-key.v1.json")); Assert(LedgerInstance.Recover(root,backup)==bound);
        });
        Check("interrupted classification reports retained bytes truthfully, then recovers",()=>
        {
            var root=Path.Combine(Scratch,"locked-dispute"); LedgerInstance.Initialize(root); SignedBlock first; byte[] second;
            using(var instance=LedgerInstance.Open(root))
            {
                var g=instance.InitializeLedger(); var other=Enumerable.Repeat((byte)23,32).ToArray();
                instance.Publish(EventCodec.Encode(new(0x20,PublicKey:Signing.PublicKey(other))),timestamp:g.Timestamp+1);
                var id=Guid.NewGuid(); var body=EventCodec.Encode(new(0x11,Guid.NewGuid()));
                first=instance.Publish(body,id,g.Timestamp+2).Block; second=BlockCodec.Create(g.LedgerId,id,g.Timestamp+2,other,body);
            }
            var input=Path.Combine(Scratch,"locked-dispute-input.blk"); File.WriteAllBytes(input,second);
            string path; using(var instance=LedgerInstance.Open(root)) path=instance.BlockPath(first,false);
            using(var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                var result=Cli(root,"block","import","--input",input);
                Assert(result.Exit==3 && result.Json.GetProperty("error").GetProperty("details").GetProperty("durable_count").GetInt32()==1);
                Assert(result.Json.GetProperty("error").GetProperty("details").GetProperty("results")[0].GetProperty("durable").GetBoolean());
            }
            using var reopened=LedgerInstance.Open(root); Assert(reopened.Snapshot().Blocks.Count(x=>x.Reason=="BLOCK_ID_DISPUTED")==2);
        });
        Check("truncated DDLB preserves and reports preceding accepted items",()=>
        {
            var root=Path.Combine(Scratch,"truncated-ddlb"); LedgerInstance.Initialize(root);
            var seed=Enumerable.Repeat((byte)24,32).ToArray(); var g=BlockCodec.Parse(BlockCodec.Create(Guid.NewGuid(),Guid.NewGuid(),1,seed,null));
            var raw=Ddlb.Encode(new[]{g,g}); var path=Path.Combine(Scratch,"truncated.ddlb"); File.WriteAllBytes(path,raw[..^10]);
            var result=Cli(root,"block","import","--input",path); Assert(result.Exit==3);
            var details=result.Json.GetProperty("error").GetProperty("details"); Assert(details.GetProperty("durable_count").GetInt32()==1 && details.GetProperty("rejected_count").GetInt32()==1 && details.GetProperty("results").GetArrayLength()==2);
        });
    }
}
