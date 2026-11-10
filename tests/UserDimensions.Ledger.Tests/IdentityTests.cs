using System.Security.Cryptography;
using UserDimensions.Ledger;

internal static partial class Tests
{
    internal static string Scratch = Path.Combine(Repository(), ".git", "dev-003-work", "tests-" + Guid.NewGuid().ToString("N"));
    private static string Repository()
    { for (var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent) if(Directory.Exists(Path.Combine(d.FullName,".git"))) return d.FullName; throw new Exception("Run tests inside this checkout."); }
    public static void RunIdentity()
    {
        Directory.CreateDirectory(Scratch);
        Check("88-byte AES-GCM AAD and original identity round-trip", () =>
        {
            using var key=SigningIdentity.Create(); var encoded=key.Encode();
            Assert(SigningIdentity.Aad(key.InstanceId,key.PublicKey).Length==88);
            using var loaded=SigningIdentity.Decode(encoded); Assert(loaded.InstanceId==key.InstanceId && loaded.PublicKey.SequenceEqual(key.PublicKey));
            Assert(!key.Encode().SequenceEqual(encoded));
            var text=Binary.Utf8.GetString(encoded).Replace("\"version\":1", "\"version\":1.0");
            Throws("IDENTITY_MISMATCH",()=>SigningIdentity.Decode(Binary.Utf8.GetBytes(text)));
            text=Binary.Utf8.GetString(encoded).Replace("\"version\":1", "\"version\":1,\"version\":1");
            Throws("IDENTITY_MISMATCH",()=>SigningIdentity.Decode(Binary.Utf8.GetBytes(text)));
        });
        Check("manifest strict fields, public identity and binding",()=>
        {
            var manifest=new InstanceManifest(Guid.NewGuid(),new string('1',64),new(Guid.NewGuid(),new string('2',128)));
            Assert(InstanceManifest.Decode(manifest.Encode())==manifest);
            Throws("IDENTITY_MISMATCH",()=>InstanceManifest.Decode(Binary.Utf8.GetBytes(Binary.Utf8.GetString(manifest.Encode()).Replace("signing-key.v1.json","../signing-key.v1.json"))));
        });
        Check("Windows durable no-overwrite, atomic replacement, external lock",()=>
        {
            var files=new DurableFiles(); var path=Path.Combine(Scratch,"publish.dat");
            files.Publish(path,[1,2]); Throws("OUTPUT_EXISTS",()=>files.Publish(path,[3])); Assert(File.ReadAllBytes(path).SequenceEqual(new byte[]{1,2}));
            files.Publish(path,[4],true); Assert(File.ReadAllBytes(path).SequenceEqual(new byte[]{4}));
            using var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None);
            Throws("IO_ERROR",()=>files.Publish(path,[5],true));
        });
        Check("persistent instance, duplicate-open protection, missing-key gate",()=>
        {
            var root=Path.Combine(Scratch,"instance"); var manifest=LedgerInstance.Initialize(root);
            using (var instance=LedgerInstance.Open(root)) { Assert(instance.Manifest==manifest); Throws("DUPLICATE_INSTANCE",()=>LedgerInstance.Open(root)); }
            File.Move(Path.Combine(root,"signing-key.v1.json"),Path.Combine(root,"saved-key"));
            Throws("RECOVERY_REQUIRED",()=>LedgerInstance.Open(root)); Throws("RECOVERY_REQUIRED",()=>LedgerInstance.Initialize(root));
        });
        Check("key-only interrupted initialization explicit same-identity recovery",()=>
        {
            var root=Path.Combine(Scratch,"interrupted");
            var fault=new DurableFiles(name=> { if(name=="after-publish:signing-key.v1.json") throw new IOException("Injected crash"); });
            try { LedgerInstance.Initialize(root,fault); } catch(IOException) { }
            Assert(File.Exists(Path.Combine(root,"signing-key.v1.json")) && !File.Exists(Path.Combine(root,"instance.identity.json")));
            var recovered=LedgerInstance.Recover(root,Path.Combine(root,"signing-key.v1.json"),true);
            Assert(LedgerInstance.Recover(root,Path.Combine(root,"signing-key.v1.json"),true)==recovered);
            using var opened=LedgerInstance.Open(root); Assert(opened.Manifest==recovered);
        });
        Check("persistent resource limits and corrupt config fail closed",()=>
        {
            var root=Path.Combine(Scratch,"config"); LedgerInstance.Initialize(root);
            var before=File.ReadAllBytes(Path.Combine(root,"instance.identity.json"));
            using (var instance=LedgerInstance.Open(root)) { instance.Configure("max_raw_block_bytes",5000); Assert(instance.Limits.MaxRawBlockBytes==5000); }
            using (var instance=LedgerInstance.Open(root)) Assert(instance.Limits.MaxRawBlockBytes==5000);
            Assert(File.ReadAllBytes(Path.Combine(root,"instance.identity.json")).SequenceEqual(before));
            File.WriteAllText(Path.Combine(root,"instance.config.json"),"{}");
            using var broken=LedgerInstance.Open(root); Assert(broken.ConfigError=="CONFIG_CORRUPT"); Throws("CONFIG_CORRUPT",()=>broken.Snapshot());
        });
    }
}
