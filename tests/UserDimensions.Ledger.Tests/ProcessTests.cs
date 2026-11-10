using System.Diagnostics;
using UserDimensions.Ledger;

internal static partial class Tests
{
    public static int Worker(string[] args)
    {
        if(args[0]=="lease")
        {
            using var instance=LedgerInstance.Open(args[1]); File.WriteAllText(args[2],"ready");
            var deadline=Stopwatch.StartNew();
            while(!File.Exists(args[3]) && deadline.Elapsed<TimeSpan.FromSeconds(15)) Thread.Sleep(10);
            return File.Exists(args[3])?0:90;
        }
        var fault=new DurableFiles(name=> { if(name==args[2] || (args[2]=="archive" && name.StartsWith("after-rename:",StringComparison.Ordinal) && name.EndsWith(".XBLK",StringComparison.Ordinal))) Environment.Exit(77); });
        if(args[0]=="init") LedgerInstance.Initialize(args[1],fault);
        else
        {
            using var instance=LedgerInstance.Open(args[1],fault);
            if(args[0]=="genesis") instance.InitializeLedger();
            if(args[0]=="config") instance.Configure("max_raw_block_bytes",8000);
            if(args[0]=="publish") instance.Publish(File.ReadAllBytes(args[3]),Guid.Parse(args[4]),long.Parse(args[5],System.Globalization.CultureInfo.InvariantCulture));
        }
        return 0;
    }
    private static Process StartWorker(params string[] arguments)
    {
        var start=new ProcessStartInfo("dotnet") { UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true };
        start.ArgumentList.Add(typeof(Tests).Assembly.Location); start.ArgumentList.Add("--worker");
        foreach(var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start)!;
    }
    private static void ExpectCrash(params string[] arguments)
    { using var p=StartWorker(arguments); Assert(p.WaitForExit(15000),"Worker did not finish"); Assert(p.ExitCode==77,"Worker did not reach checkpoint: "+p.StandardError.ReadToEnd()); }
    public static void RunProcesses()
    {
        Check("independent compressed LZMA2 vector (7-Zip 26.05)",()=>
        {
            // Raw LZMA2 stream extracted from a tested XZ archive; receiver dictionary property is 20 (4 MiB).
            var packed=Convert.FromHexString("14E0F22F00875D00349B88B0575C5E8DC63994378EC05FB88B012CC8BF91FC49BC27A29850464556249A0C513B45A7CD8D7A8D54631D981116BBDED9490AF001E4A85E8EC48A8EDF27BC20210D70AEF20BFC1ACF1CFBAD408048C79194ABF066B0436B0D399B743934585A4E7F9958998B6B030E35DA8E593F1EF02499BFBC5FFA1F5E151C8D0A08CC0431AA06780000");
            Assert(Binary.Utf8.GetString(Lzma2.Decode(packed,new()))==string.Concat(Enumerable.Repeat("independent LZMA2 vector abcdefghijklmnopqrstuvwxyz 0123456789",1000)));
        });
        Check("cross-process duplicate original identity, including copied root",()=>
        {
            var root=Path.Combine(Scratch,"lease"); var copy=Path.Combine(Scratch,"copied-lease"); LedgerInstance.Initialize(root); Directory.CreateDirectory(copy);
            foreach(var name in new[]{"signing-key.v1.json","instance.identity.json"}) File.Copy(Path.Combine(root,name),Path.Combine(copy,name));
            var marker=Path.Combine(Scratch,"lease.ready"); var release=Path.Combine(Scratch,"lease.release"); using var p=StartWorker("lease",root,marker,release);
            try
            {
                var deadline=Stopwatch.StartNew(); while(!File.Exists(marker) && !p.HasExited && deadline.Elapsed<TimeSpan.FromSeconds(10)) Thread.Sleep(10);
                Assert(File.Exists(marker),"Lease worker failed: "+(p.HasExited?p.StandardError.ReadToEnd():"timeout"));
                Throws("DUPLICATE_INSTANCE",()=>LedgerInstance.Open(root)); Throws("DUPLICATE_INSTANCE",()=>LedgerInstance.Open(copy));
            }
            finally { File.WriteAllText(release,"release"); if(!p.WaitForExit(5000)) p.Kill(); }
        });
        Check("process interruption after durable key: original key-only recovery",()=>
        {
            var root=Path.Combine(Scratch,"process-init"); ExpectCrash("init",root,"after-publish:signing-key.v1.json");
            using var original=SigningIdentity.Decode(File.ReadAllBytes(Path.Combine(root,"signing-key.v1.json")));
            var manifest=LedgerInstance.Recover(root,Path.Combine(root,"signing-key.v1.json"),true); Assert(manifest.PublicKey==Binary.Hex(original.PublicKey));
        });
        Check("process interruption after durable genesis: no replacement genesis",()=>
        {
            var root=Path.Combine(Scratch,"process-genesis"); LedgerInstance.Initialize(root); ExpectCrash("genesis",root,"genesis-durable-before-binding");
            using var instance=LedgerInstance.Open(root); var genesis=instance.Snapshot().Blocks.Single().Block;
            Assert(instance.Manifest.Binding==null); instance.RecoverGenesis(genesis.LedgerId,genesis.Hash); Assert(instance.Snapshot().Blocks.Count==1);
        });
        foreach(var checkpoint in new[]{"before-rename:instance.config.json","after-rename:instance.config.json"}) Check("config process interruption "+checkpoint,()=>
        {
            var root=Path.Combine(Scratch,"process-config-"+Guid.NewGuid().ToString("N")); var manifest=LedgerInstance.Initialize(root);
            using(var instance=LedgerInstance.Open(root)) instance.Configure("max_raw_block_bytes",6000);
            ExpectCrash("config",root,checkpoint);
            using var reopened=LedgerInstance.Open(root); Assert(reopened.Manifest==manifest);
            Assert(reopened.Limits.MaxRawBlockBytes==(checkpoint.StartsWith("before",StringComparison.Ordinal)?6000:8000));
        });
        Check("process interruption during XBLK transition preserves every signed variant",()=>
        {
            var root=Path.Combine(Scratch,"process-archive"); LedgerInstance.Initialize(root); var seed=Enumerable.Repeat((byte)66,32).ToArray();
            byte[] eventBytes; Guid revId=Guid.NewGuid(); long time; SignedBlock data;
            using(var instance=LedgerInstance.Open(root))
            {
                var g=instance.InitializeLedger(); time=g.Timestamp+200;
                instance.Publish(EventCodec.Encode(new(0x20,PublicKey:Signing.PublicKey(seed))),timestamp:g.Timestamp+100);
                data=BlockCodec.Parse(BlockCodec.Create(g.LedgerId,Guid.NewGuid(),g.Timestamp+300,seed,EventCodec.Encode(new(0x11,Guid.NewGuid())))); instance.Import(data.Raw,out _);
                var counter=Signing.PublicKey(seed); var intent=Signing.RevocationIntent(g.LedgerId,revId,time,Convert.FromHexString(instance.PublicKey),counter,counter);
                eventBytes=EventCodec.Encode(new(0x21,PublicKey:counter,Countersigner:counter,Countersignature:Signing.Sign(seed,intent)));
            }
            var input=Path.Combine(Scratch,"process-revoke.event"); File.WriteAllBytes(input,eventBytes);
            ExpectCrash("publish",root,"archive",input,revId.ToString("D"),time.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var reopened=LedgerInstance.Open(root); var snapshot=reopened.Snapshot(); Assert(snapshot.Blocks.Count==4);
            Assert(snapshot.Blocks.Single(x=>x.Block.Hash==data.Hash).Status=="quarantined"); Assert(File.ReadAllBytes(reopened.BlockPath(data,true)).SequenceEqual(data.Raw));
        });
    }
}
