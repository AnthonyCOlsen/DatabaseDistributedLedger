using UserDimensions.Ledger;

internal static partial class Tests
{
    public static void RunFrozenIntentStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "2026-11-27-FrozenIntent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Console.WriteLine("Frozen intent fixture root " + root);
        Guid table = Guid.NewGuid(), row = Guid.NewGuid(), a = Guid.NewGuid(), b = Guid.NewGuid();
        byte[] drop = [0x11, .. table.ToByteArray(bigEndian: true)];
        byte[] delete = [0x03, .. table.ToByteArray(bigEndian: true), .. row.ToByteArray(bigEndian: true)];
        var original = new InstanceManifest(Guid.NewGuid(), new string('1', 64),
            new(new string('2', 64), new string('3', 128)));
        var binding = new IntentBinding(Guid.NewGuid(), Guid.NewGuid(), original, 42);
        IntentEvent[] Events() => [new(a, drop.ToArray()), new(b, delete.ToArray())];
        FrozenIntent Fresh() => FrozenIntent.Capture(binding with { Request = Guid.NewGuid() }, Events());
        string Slot(FrozenIntent f) => Path.Combine(root, f.Request.ToString("N"));
        static string[] Inventory(string dir) => Directory.GetFiles(dir).Order(StringComparer.Ordinal)
            .Select(p => Path.GetFileName(p) + "=" + Convert.ToHexString(File.ReadAllBytes(p))).ToArray();
        var plain = new FrozenIntentStore(root, new DurableFiles());

        Check("FI ordered capture, owned bytes, exact manifest and durable adapter order", () =>
        {
            var entries = Events();
            var f = FrozenIntent.Capture(binding, entries);
            entries[0].Body[0] = 0;
            entries[1] = entries[0];
            f.EventAt(1).Body[0] = 0;
            f.ManifestCopy()[0] = 0;
            f.PayloadCopy()[0] = 0;
            Assert(f.Count == 2 && f.EventAt(0).BlockId == a && f.EventAt(1).BlockId == b);
            Assert(f.EventAt(0).Body.SequenceEqual(drop) && f.EventAt(1).Body.SequenceEqual(delete));
            var calls = new List<string>();
            var store = new FrozenIntentStore(root, new DurableFiles(calls.Add));
            Assert(store.Stage(f) == FrozenReadiness.WriteCompleted, string.Join(", ", calls));
            var slot = Slot(f);
            Assert(File.ReadAllBytes(Path.Combine(slot, "payload")).SequenceEqual(drop.Concat(delete)));
            using var reader = new BinaryReader(File.OpenRead(Path.Combine(slot, "manifest")));
            Assert(reader.ReadBytes(8).SequenceEqual("UDDLFIM1"u8.ToArray()));
            Guid ReadGuid() => new(reader.ReadBytes(16), bigEndian: true);
            Assert(ReadGuid() == binding.Request && ReadGuid() == binding.Business && ReadGuid() == original.InstanceId);
            Assert(Convert.ToHexString(reader.ReadBytes(32)) == original.PublicKey);
            Assert(Convert.ToHexString(reader.ReadBytes(32)) == original.Binding!.LedgerId);
            Assert(Convert.ToHexString(reader.ReadBytes(64)) == original.Binding.GenesisHash);
            Assert(reader.ReadUInt64() == 42 && reader.ReadInt32() == 2);
            foreach (var e in Events())
            {
                Assert(ReadGuid() == e.BlockId && reader.ReadInt32() == e.Body.Length);
                Assert(reader.ReadBytes(32).SequenceEqual(System.Security.Cryptography.SHA256.HashData(e.Body)));
            }
            Assert(reader.BaseStream.Position == reader.BaseStream.Length);
            var ordered = new List<string>();
            foreach (var name in new[] { ".lock", "payload", "manifest", "ready" })
                foreach (var point in new[] { "before-write:", "after-flush:", "before-rename:", "after-rename:", "after-publish:" })
                    ordered.Add(point + name);
            ordered.AddRange(["before-rename:sealed", "after-rename:sealed"]);
            Assert(calls.SkipWhile(x => x != "before-write:.lock").SequenceEqual(ordered));
            calls.Clear();
            var baseline = Inventory(slot);
            Assert(store.Stage(f) == FrozenReadiness.ObservedExact && plain.Observe(f) == FrozenReadiness.ObservedExact);
            var variants = new[] {
                binding with { Epoch = 43 }, binding with { Business = Guid.NewGuid() },
                binding with { Original = original with { InstanceId = Guid.NewGuid() } },
                binding with { Original = original with { PublicKey = new string('4', 64) } },
                binding with { Original = original with { Binding = original.Binding with { LedgerId = new string('5', 64) } } },
                binding with { Original = original with { Binding = original.Binding with { GenesisHash = new string('6', 128) } } }
            };
            foreach (var variant in variants)
                Assert(store.Stage(FrozenIntent.Capture(variant, Events())) == FrozenReadiness.Restricted);
            Assert(store.Observe(FrozenIntent.Capture(binding with { Request = Guid.NewGuid() }, Events())) == FrozenReadiness.Restricted);
            Assert(store.Stage(FrozenIntent.Capture(binding, Events().Reverse().ToArray())) == FrozenReadiness.Restricted);
            Assert(store.Stage(FrozenIntent.Capture(binding, [new(a, delete), new(b, drop)])) == FrozenReadiness.Restricted);
            Assert(calls.Count == 0 && Inventory(slot).SequenceEqual(baseline));
        });

        Check("FI duplicate, empty, excessive and malformed captures are rejected", () =>
        {
            void Reject(IntentEvent[] values)
            {
                try { FrozenIntent.Capture(binding, values); }
                catch (Exception e) when (e is ArgumentException or LedgerException) { return; }
                throw new Exception("Unexpected capture");
            }
            Reject([]);
            Reject([new(Guid.Empty, drop)]);
            Reject([new(a, drop), new(a, delete)]);
            Reject([new(a, new byte[FrozenIntent.MaxBody + 1])]);
            Reject(new IntentEvent[FrozenIntent.MaxEvents + 1]);
            Reject([new(a, drop), new(b, [0x11])]);
            var body = new byte[FrozenIntent.MaxBody];
            Reject(Enumerable.Range(0, FrozenIntent.MaxTotal / body.Length + 1)
                .Select(_ => new IntentEvent(Guid.NewGuid(), body)).ToArray());
        });

        foreach (string fault in new[] { "after-flush:manifest", "after-publish:ready", "before-rename:sealed", "after-rename:sealed" })
            Check("FI interrupted writer and independent reopen at " + fault, () =>
            {
                var f = Fresh();
                bool hit = false;
                var writer = new FrozenIntentStore(root, new DurableFiles(point =>
                {
                    if (point == fault) { hit = true; throw new IOException("Synthetic lost return at " + point); }
                }));
                Assert(writer.Stage(f) == FrozenReadiness.Restricted && hit);
                var slot = Slot(f);
                bool final = fault == "after-rename:sealed";
                Assert(File.Exists(Path.Combine(slot, ".lock")) == !final);
                Assert(File.Exists(Path.Combine(slot, "ready")) == (fault != "after-flush:manifest"));
                if (fault == "after-flush:manifest")
                    Assert(Directory.GetFiles(slot, "*.tmp").Length == 1 && !File.Exists(Path.Combine(slot, "manifest")));
                var before = Inventory(slot);
                var calls = new List<string>();
                var reopened = new FrozenIntentStore(root, new DurableFiles(calls.Add));
                var status = final ? FrozenReadiness.ObservedExact : FrozenReadiness.Restricted;
                Assert(reopened.Observe(f) == status && reopened.Stage(f) == status);
                Assert(calls.Count == 0 && Inventory(slot).SequenceEqual(before));
            });

        foreach (string changed in new[] { "truncate", "body", "manifest", "missing", "extra", "stale-lock", "ready", "sealed" })
            Check("FI read-only restriction with " + changed, () =>
            {
                var f = Fresh();
                Assert(plain.Stage(f) == FrozenReadiness.WriteCompleted);
                var slot = Slot(f);
                string PathFor(string name) => Path.Combine(slot, name);
                switch (changed)
                {
                    case "missing": File.Delete(PathFor("manifest")); break;
                    case "extra": File.WriteAllBytes(PathFor("extra"), [1]); break;
                    case "stale-lock": File.Copy(PathFor("sealed"), PathFor(".lock")); break;
                    case "truncate": File.WriteAllBytes(PathFor("payload"), drop); break;
                    default:
                        var path = PathFor(changed == "body" ? "payload" : changed);
                        var bytes = File.ReadAllBytes(path);
                        bytes[^1] ^= 1;
                        File.WriteAllBytes(path, bytes);
                        break;
                }
                var before = Inventory(slot);
                var calls = new List<string>();
                var reopened = new FrozenIntentStore(root, new DurableFiles(calls.Add));
                Assert(reopened.Observe(f) == FrozenReadiness.Restricted && reopened.Stage(f) == FrozenReadiness.Restricted);
                Assert(calls.Count == 0 && Inventory(slot).SequenceEqual(before));
            });

        Check("FI exclusive construction retains an existing late winner", () =>
        {
            var f = Fresh();
            var slot = Slot(f);
            string[]? winner = null;
            var racing = new FrozenIntentStore(root, new DurableFiles(point =>
            {
                if (point != "before-rename:" + f.Request.ToString("N")) return;
                Assert(plain.Stage(f) == FrozenReadiness.WriteCompleted);
                winner = Inventory(slot);
            }));
            Assert(racing.Stage(f) == FrozenReadiness.Restricted);
            Assert(winner != null && Inventory(slot).SequenceEqual(winner));
            Assert(plain.Observe(f) == FrozenReadiness.ObservedExact);
        });
    }
}
