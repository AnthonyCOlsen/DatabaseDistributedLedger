using System.Buffers.Binary;
using System.Security.Cryptography;
using UserDimensions.Ledger;

internal static partial class Tests
{
    public static void RunProgressJournal()
    {
        var batch = Path.Combine(Path.GetTempPath(), "2026-11-27-Progress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(batch);
        Console.WriteLine("Disposable journal fixture " + batch);
        var key = RandomNumberGenerator.GetBytes(32);
        var table = Guid.NewGuid();
        var original = new InstanceManifest(Guid.NewGuid(), new string('1',64), new(new string('2',64),new string('3',128)));
        var binding = new IntentBinding(Guid.NewGuid(), Guid.NewGuid(), original, 17);
        byte[] drop = [0x11, .. table.ToByteArray(true)];
        byte[] delete = [0x03, .. table.ToByteArray(true), .. Guid.NewGuid().ToByteArray(true)];
        IntentEvent[] events = [new(Guid.NewGuid(), drop), new(Guid.NewGuid(), delete)];
        var intent = FrozenIntent.Capture(binding, events);
        string Root() => Directory.CreateDirectory(Path.Combine(batch, Guid.NewGuid().ToString("N"))).FullName;
        string Slot(string root) => Path.Combine(root, intent.Request.ToString("N"));
        IntentProgressJournal Open(string root, Action<string>? checkpoint = null) => new(root,intent,key,new(checkpoint));
        ProgressTip Init(IntentProgressJournal journal)
        {
            Assert(journal.Initialize() == ProgressWrite.WriteCompleted);
            return journal.Read().MatchedHead!;
        }
        static string[] Snapshot(string dir) => Directory.GetFiles(dir).Order(StringComparer.Ordinal)
            .Select(p => Path.GetFileName(p) + "=" + Convert.ToHexString(File.ReadAllBytes(p))).ToArray();
        void Remac(byte[] frame) => HMACSHA256.HashData(key, frame.AsSpan(..^32), frame.AsSpan(^32..));
        Check("journal exact ordered records, independent frame MACs and acknowledged head", () =>
        {
            var root = Root();
            var calls = new List<string>();
            using var j = Open(root,calls.Add);
            Assert(j.Read().State == ProgressState.Restricted && !Directory.Exists(Slot(root)));
            var zero = Init(j);
            var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
            byte[] previous = new byte[32];
            var tip = zero;
            for (int i = 0; i < 2; i++)
            {
                calls.Clear();
                Assert(j.Append(tip,ids[i],i) == ProgressWrite.WriteCompleted);
                string recName = (i + 1).ToString("D4") + ".rec";
                Assert(calls.SequenceEqual(new[] { recName, "head" }.SelectMany(n =>
                    new[] { "before-write:", "after-flush:", "before-rename:", "after-rename:", "after-publish:" }.Select(x => x + n))));
                var raw = File.ReadAllBytes(Path.Combine(Slot(root),recName));
                var head = File.ReadAllBytes(Path.Combine(Slot(root),"head"));
                foreach (var frame in new[] { raw, head })
                {
                    Assert(frame.AsSpan(8,32).SequenceEqual(SHA256.HashData(intent.ManifestCopy())));
                    Assert(BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(40)) == i + 1);
                    Assert(frame.AsSpan(^32..).SequenceEqual(HMACSHA256.HashData(key, frame.AsSpan(..^32))));
                }
                Assert(raw.Length == 177 && raw.AsSpan(0,8).SequenceEqual("UDDLJRC1"u8));
                Assert(raw.AsSpan(44,32).SequenceEqual(previous) && raw[76] == 1);
                Assert(new Guid(raw.AsSpan(77,16),true) == ids[i] && BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(93)) == i);
                Assert(new Guid(raw.AsSpan(97,16),true) == events[i].BlockId &&
                    raw.AsSpan(113,32).SequenceEqual(SHA256.HashData(events[i].Body)));
                previous = SHA256.HashData(raw);
                Assert(head.Length == 108 && head.AsSpan(0,8).SequenceEqual("UDDLJHD1"u8) && head.AsSpan(44,32).SequenceEqual(previous));
                tip = new(i + 1, Convert.ToHexString(SHA256.HashData(head)));
                using var reopened = Open(root);
                var read = reopened.Read();
                Assert(read.State == ProgressState.ObservedExact && read.MatchedHead == tip &&
                    read.VerifiedRecords.Select(x => x.RecordId).SequenceEqual(ids.Take(i+1)) && read.Ahead == null);
            }
            calls.Clear();
            var before = Snapshot(Slot(root));
            Assert(j.Initialize() == ProgressWrite.Refused && j.Append(zero,Guid.NewGuid(),0) == ProgressWrite.Refused);
            Assert(j.Append(tip,ids[0],1) == ProgressWrite.Refused && j.Append(tip,Guid.NewGuid(),2) == ProgressWrite.Refused);
            using var wrongKey = new IntentProgressJournal(root,intent,RandomNumberGenerator.GetBytes(32),new());
            Assert(wrongKey.Read().MatchedHead == null && wrongKey.Append(tip,Guid.NewGuid(),0) == ProgressWrite.Refused);
            foreach (var other in new[] { binding with { Epoch = 18 }, binding with { Original = original with { InstanceId = Guid.NewGuid() } } })
            {
                using var wrongBinding = new IntentProgressJournal(root,FrozenIntent.Capture(other,events),key,new());
                Assert(wrongBinding.Read().MatchedHead == null && wrongBinding.Read().State == ProgressState.Restricted);
            }
            Assert(calls.Count == 0 && before.SequenceEqual(Snapshot(Slot(root))));
        });

        foreach (var damage in new[] { "missing", "bad-record-mac", "head-mac", "wrong-predecessor", "duplicate-id",
            "unsupported-kind", "wrong-position", "unknown-name", "truncated-tail", "head-limit", "head-reference" })
            Check("journal restricts " + damage + " and preserves bytes", () =>
            {
                var root = Root();
                using var j = Open(root);
                var tip = Init(j);
                var first = Guid.NewGuid();
                Assert(j.Append(tip,first,0) == ProgressWrite.WriteCompleted);
                Assert(j.Append(j.Read().MatchedHead!,Guid.NewGuid(),1) == ProgressWrite.WriteCompleted);
                var slot = Slot(root);
                var path = Path.Combine(slot,"0002.rec");
                var raw = File.ReadAllBytes(path);
                var hp = Path.Combine(slot,"head");
                var head = File.ReadAllBytes(hp);
                switch (damage)
                {
                    case "missing": File.Delete(path); break;
                    case "bad-record-mac": raw[^1] ^= 1; File.WriteAllBytes(path,raw); break;
                    case "head-mac": head[^1] ^= 1; File.WriteAllBytes(hp,head); break;
                    case "unknown-name": File.WriteAllBytes(Path.Combine(slot,"unexpected"),[1]); break;
                    case "truncated-tail": File.WriteAllBytes(Path.Combine(slot,"0003.rec"),[1]); break;
                    case "head-reference": head[44] ^= 1; Remac(head); File.WriteAllBytes(hp,head); break;
                    case "head-limit": BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(40),129); Remac(head); File.WriteAllBytes(hp,head); break;
                    default:
                        if (damage == "wrong-predecessor") raw[44] ^= 1;
                        if (damage == "duplicate-id") first.ToByteArray(true).CopyTo(raw,77);
                        if (damage == "unsupported-kind") raw[76] = 250;
                        if (damage == "wrong-position") BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(93),0);
                        Remac(raw);
                        File.WriteAllBytes(path,raw);
                        SHA256.HashData(raw).CopyTo(head,44);
                        Remac(head);
                        File.WriteAllBytes(hp,head);
                        break;
                }
                var before = Snapshot(slot);
                var read = j.Read();
                bool tail = damage is "unknown-name" or "truncated-tail";
                int verified = damage is "head-mac" or "head-limit" ? 0 : tail || damage == "head-reference" ? 2 : 1;
                Assert(read.State == ProgressState.Restricted && read.VerifiedRecords.Count == verified &&
                    (read.MatchedHead != null) == tail && read.Ahead == null);
                Assert(j.Append(tip,Guid.NewGuid(),0) == ProgressWrite.Refused && Snapshot(slot).SequenceEqual(before));
            });

        foreach (var point in new[] { "before-write:0002.rec", "after-flush:0002.rec", "after-publish:0002.rec",
            "before-rename:head", "after-publish:head" })
            Check("journal append interrupted at " + point, () =>
            {
                var root = Root();
                using var normal = Open(root);
                Assert(normal.Append(Init(normal), Guid.NewGuid(),0) == ProgressWrite.WriteCompleted);
                var old = normal.Read().MatchedHead!;
                var id = Guid.NewGuid();
                bool hit = false;
                using var fault = Open(root, p => { if (p == point) { hit = true; throw new IOException("Synthetic checkpoint interruption"); } });
                Assert(fault.Append(old,id,1) == ProgressWrite.WriteUncertain && hit);
                using var reopened = Open(root);
                var before = Snapshot(Slot(root));
                var result = reopened.Read();
                int n = point == "after-publish:head" ? 2 : 1;
                Assert(result.MatchedHead?.Sequence == n && result.VerifiedRecords.Count == n);
                bool orphan = point == "after-publish:0002.rec";
                var expected = point is "before-rename:head" or "after-flush:0002.rec" ? ProgressState.Restricted :
                    orphan ? ProgressState.UnconfirmedTail : ProgressState.ObservedExact;
                Assert(result.State == expected && result.Ahead?.RecordId == (orphan ? id : (Guid?)null));
                if (n == 1) Assert(result.MatchedHead == old);
                if (n == 2) Assert(result.VerifiedRecords[1].RecordId == id);
                Assert(Snapshot(Slot(root)).SequenceEqual(before));
            });
        CryptographicOperations.ZeroMemory(key);
    }
}
