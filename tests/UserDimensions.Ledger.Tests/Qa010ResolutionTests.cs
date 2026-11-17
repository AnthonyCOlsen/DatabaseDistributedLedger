using System.Text.Json;
using UserDimensions.Ledger;

internal static partial class Tests
{
    private static Dictionary<string,string> FileHashes(string root) => Directory.GetFiles(root,"*",SearchOption.AllDirectories)
        .ToDictionary(p=>Path.GetRelativePath(root,p),p=>Binary.Sha512(File.ReadAllBytes(p)));

    private static (string Root,string Input,SignedBlock Grant,SignedBlock Dependent) Qa010Batch(string name)
    {
        var root=Path.Combine(Scratch,name); LedgerInstance.Initialize(root); SignedBlock genesis;
        using(var instance=LedgerInstance.Open(root)) genesis=instance.InitializeLedger();
        var other=Enumerable.Repeat((byte)161,32).ToArray();
        using var key=SigningIdentity.Decode(File.ReadAllBytes(Path.Combine(root,"signing-key.v1.json")));
        var grant=BlockCodec.Parse(key.CreateBlock(genesis.LedgerId,Guid.NewGuid(),genesis.Timestamp+1,EventCodec.Encode(new(0x20,PublicKey:Signing.PublicKey(other)))));
        var dependent=BlockCodec.Parse(BlockCodec.Create(genesis.LedgerId,Guid.NewGuid(),genesis.Timestamp+2,other,EventCodec.Encode(new(0x11,Guid.NewGuid()))));
        var input=Path.Combine(Scratch,name+".DDLB"); File.WriteAllBytes(input,Ddlb.Encode([dependent,grant]));
        return (root,input,grant,dependent);
    }

    public static void RunQa010Resolution()
    {
        Check("QA-010 join rejects DDLB as BAD_FRAME without any instance mutation",()=>
        {
            var root=Path.Combine(Scratch,"qa010-join-frame"); LedgerInstance.Initialize(root);
            var seed=Enumerable.Repeat((byte)162,32).ToArray(); var ledger=Binary.Hex(Signing.PublicKey(seed));
            var genesis=BlockCodec.Parse(BlockCodec.Create(ledger,Guid.NewGuid(),1,seed,null));
            var before=FileHashes(root); var directories=Directory.GetDirectories(root,"*",SearchOption.AllDirectories);
            foreach(var bytes in new[]{Ddlb.Encode([genesis]),"DDLB"u8.ToArray()})
            {
                var input=Path.Combine(Scratch,"qa010-join-wrapper.bin"); File.WriteAllBytes(input,bytes);
                var result=Cli(root,"ledger","join","--ledger-id",ledger,"--genesis-sha512",genesis.Hash,"--genesis-file",input);
                Assert(result.Exit==2 && result.Json.GetProperty("error").GetProperty("code").GetString()=="BAD_FRAME");
                Assert(FileHashes(root).OrderBy(x=>x.Key).SequenceEqual(before.OrderBy(x=>x.Key)));
                Assert(Directory.GetDirectories(root,"*",SearchOption.AllDirectories).SequenceEqual(directories));
            }
            var legacy=genesis.Raw.ToArray(); legacy[1]=2;
            using var instance=LedgerInstance.Open(root);
            Throws("UNSUPPORTED_VERSION",()=>instance.Join(ledger,genesis.Hash,legacy));
        });
        foreach(var boundary in new[]{"batch-grant-durable","batch-item-completed"}) Check("QA-010 actual inverted DDLB process interruption at "+boundary,()=>
        {
            var test=Qa010Batch("qa010-crash-"+boundary);
            var keyBefore=Binary.Sha512(File.ReadAllBytes(Path.Combine(test.Root,"signing-key.v1.json")));
            // Canonical first item is grant at TRANSPORT index 1; dependent index 0 cannot yet persist.
            ExpectCrash("batch",test.Root,boundary+":1:"+test.Grant.Hash,test.Input);
            using(var instance=LedgerInstance.Open(test.Root))
            {
                Assert(instance.Snapshot().Blocks.Count==2);
                Assert(File.ReadAllBytes(instance.BlockPath(test.Grant,false)).SequenceEqual(test.Grant.Raw));
                Assert(!File.Exists(instance.BlockPath(test.Dependent,false)) && !File.Exists(instance.BlockPath(test.Dependent,true)));
            }
            Assert(keyBefore==Binary.Sha512(File.ReadAllBytes(Path.Combine(test.Root,"signing-key.v1.json"))));
            var retry=Cli(test.Root,"block","import","--input",test.Input);
            Assert(retry.Exit==0 && retry.Json.GetProperty("data").GetProperty("duplicate_count").GetInt32()==1 && retry.Json.GetProperty("data").GetProperty("durable_count").GetInt32()==1);
        });
        Check("QA-010 batch callbacks preserve canonical order and normal JSON result",()=>
        {
            var test=Qa010Batch("qa010-batch-order"); var observed=new List<string>();
            var files=new DurableFiles(name=>{if(name.StartsWith("batch-",StringComparison.Ordinal))observed.Add(name);});
            using var stdout=new StringWriter(); using var stderr=new StringWriter();
            var exit=Management.Run(["--instance-root",test.Root,"block","import","--input",test.Input],stdout,stderr,files);
            Assert(exit==0 && stderr.ToString()=="");
            Assert(observed.SequenceEqual(new[]{"batch-grant-durable:1:"+test.Grant.Hash,"batch-item-completed:1:"+test.Grant.Hash,"batch-item-completed:0:"+test.Dependent.Hash}));
            using var result=JsonDocument.Parse(stdout.ToString()); var data=result.RootElement.GetProperty("data");
            Assert(data.GetProperty("durable_count").GetInt32()==2 && data.GetProperty("results")[0].GetProperty("sha512").GetString()==test.Dependent.Hash);
            observed.Clear();
            using var again=new StringWriter();
            Assert(Management.Run(["--instance-root",test.Root,"block","import","--input",test.Input],again,stderr,files)==0);
            Assert(observed.SequenceEqual(new[]{"batch-item-completed:1:"+test.Grant.Hash,"batch-item-completed:0:"+test.Dependent.Hash}));
        });
        Check("QA-010 failed batch observer preserves durable counts and stops dependent writes",()=>
        {
            var test=Qa010Batch("qa010-observer-failure");
            var files=new DurableFiles(name=>{if(name.StartsWith("batch-grant-durable:",StringComparison.Ordinal))throw new IOException("Injected observer failure");});
            using var stdout=new StringWriter();using var stderr=new StringWriter();
            var exit=Management.Run(["--instance-root",test.Root,"block","import","--input",test.Input],stdout,stderr,files);
            using var json=JsonDocument.Parse(stdout.ToString());var error=json.RootElement.GetProperty("error");var details=error.GetProperty("details");
            Assert(exit==3 && error.GetProperty("code").GetString()=="PARTIAL_IMPORT");
            Assert(details.GetProperty("durable_count").GetInt32()==1 && details.GetProperty("rejected_count").GetInt32()==1 && details.GetProperty("total_count").GetInt32()==2);
            Assert(details.GetProperty("results")[1].GetProperty("durable").GetBoolean() && !details.GetProperty("results")[0].GetProperty("durable").GetBoolean());
            using var instance=LedgerInstance.Open(test.Root);Assert(instance.Snapshot().Blocks.Count==2);
            Assert(!File.Exists(instance.BlockPath(test.Dependent,false)) && !File.Exists(instance.BlockPath(test.Dependent,true)));
        });
    }
}
