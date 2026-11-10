using UserDimensions.Ledger;
if(args.Length>0 && args[0]=="--worker") return Tests.Worker(args[1..]);
Tests.RunProtocol();
Tests.RunIdentity();
Tests.RunIntegration();
Tests.RunProcesses();
return Tests.Finish();

internal static partial class Tests
{
    private static int passed, failed;
    public static void Check(string name, Action action)
    {
        try { action(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e); }
    }
    public static void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
    public static void Throws(string code, Action action)
    {
        try { action(); throw new Exception("Expected " + code); }
        catch (LedgerException e) { Assert(e.Code == code, "Expected " + code + ", got " + e.Code); }
    }
    public static int Finish() { Console.WriteLine($"Developer regressions: {passed} passed, {failed} failed"); return failed == 0 ? 0 : 1; }
    public static void RunProtocol()
    {
        var seed = Convert.FromHexString("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        Check("RFC8032 empty-message Ed25519 vector", () =>
        {
            Assert(Binary.Hex(Signing.PublicKey(seed)) == "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
            var sig = Signing.Sign(seed, []);
            Assert(Binary.Hex(sig) == "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b");
            Assert(Signing.Verify(Signing.PublicKey(seed), [], sig));
        });
        var ledger = Guid.NewGuid(); var id = Guid.NewGuid();
        Check("211-byte genesis offsets and timestamp extremes", () =>
        {
            foreach (var timestamp in new[] { long.MinValue, -1L, 0L, long.MaxValue })
            {
                var raw = BlockCodec.Create(ledger, id, timestamp, seed, null); var b = BlockCodec.Parse(raw);
                Assert(raw.Length == 211 && raw[0] == 1 && raw[1] == 1 && raw[146] == 0);
                Assert(b.IsGenesis && b.Timestamp == timestamp && b.LedgerId == ledger && b.Id == id);
                var bad = raw.ToArray(); bad[0] = 0; Throws("UNSUPPORTED_VERSION", () => BlockCodec.Parse(bad));
                bad = raw.ToArray(); bad[^1] ^= 1; Throws("BAD_SIGNATURE", () => BlockCodec.Parse(bad));
            }
        });
        var table = Guid.NewGuid(); var column = Guid.NewGuid(); var row = Guid.NewGuid();
        var col = new ColumnDefinition(column, "a", "DECIMAL(10,2)", true, LedgerValue.Null);
        var events = new List<LedgerEvent> {
            new(1, table, row, Fields: [new(column, LedgerValue.Text("\u00e9\0text"))]),
            new(2, table, row, Fields: [new(column, LedgerValue.Null)]), new(3, table, row),
            new(0x10, table, Name: "table", Columns: [col]), new(0x11, table), new(0x12, table, Name:"rename"),
            new(0x13, table, Columns:[col]), new(0x14, table, column), new(0x15,table,column,"b"), new(0x16,table,Columns:[col]),
            new(0x20, PublicKey:Signing.PublicKey(seed)) };
        foreach (var e in events) Check($"event 0x{e.Kind:x2} byte round-trip compressed/uncompressed", () =>
        {
            var bytes = EventCodec.Encode(e); Assert(EventCodec.Encode(EventCodec.Decode(bytes)).SequenceEqual(bytes));
            foreach (var compressed in new[] { false, true })
            { var b = BlockCodec.Parse(BlockCodec.Create(ledger, Guid.NewGuid(), 10, seed, bytes, compressed)); Assert(b.EventBytes.SequenceEqual(bytes)); }
            Throws("BAD_EVENT", () => EventCodec.Decode(bytes.Concat(new byte[] { 0 }).ToArray()));
        });
        foreach (var value in new[] { LedgerValue.Integer(long.MinValue), LedgerValue.Integer(long.MaxValue), LedgerValue.Integer(-1),
            LedgerValue.Real(-0.0), LedgerValue.Real(double.PositiveInfinity), LedgerValue.Real(double.NegativeInfinity), LedgerValue.Real(double.Epsilon),
            LedgerValue.Blob([0, 255]), LedgerValue.Text("") })
            Check("native value " + value.Tag + ":" + value.Value, () => {
                var raw = EventCodec.Encode(new(2, table, row, Fields: [new(column,value)]));
                Assert(EventCodec.Encode(EventCodec.Decode(raw)).SequenceEqual(raw)); });
        Check("NaN and nonminimal integers rejected", () =>
        {
            Throws("BAD_EVENT", () => EventCodec.Encode(new(2, table, row, Fields: [new(column, LedgerValue.Real(double.NaN))])));
            var bytes = EventCodec.Encode(new(2, table, row, Fields:[new(column,LedgerValue.Integer(0))]));
            bytes[^1] = 128; Throws("BAD_EVENT", () => EventCodec.Decode(bytes.Concat(new byte[]{0}).ToArray()));
        });
        Check("raw LZMA2 EOS, property and decoded limits", () =>
        {
            var data = Enumerable.Repeat((byte)42, 150000).ToArray(); var packed = Lzma2.Encode(data);
            Assert(Lzma2.Decode(packed, new()).SequenceEqual(data));
            Throws("BAD_EVENT", () => Lzma2.Decode(packed[..^1],new()));
            Throws("BAD_EVENT", () => Lzma2.Decode(packed.Concat(new byte[]{0}).ToArray(),new()));
            Throws("RESOURCE_LIMIT", () => Lzma2.Decode(packed,new(MaxDecompressedEventBytes:100)));
        });
        Check("149-byte countersigning binds all intent fields", () =>
        {
            var other = Enumerable.Repeat((byte)7,32).ToArray(); var outer = Signing.PublicKey(seed); var counter = Signing.PublicKey(other);
            var intent = Signing.RevocationIntent(ledger,id,33,outer,counter,counter); Assert(intent.Length == 149);
            var e = new LedgerEvent(0x21,PublicKey:counter,Countersigner:counter,Countersignature:Signing.Sign(other,intent));
            var raw = BlockCodec.Create(ledger,id,33,seed,EventCodec.Encode(e)); Assert(BlockCodec.Parse(raw).Event!.Kind == 0x21);
            Throws("BAD_SIGNATURE", () => BlockCodec.Parse(BlockCodec.Create(ledger,id,34,seed,EventCodec.Encode(e))));
        });
    }
}
