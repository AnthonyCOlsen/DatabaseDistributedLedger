using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Math.EC.Rfc8032;
using SharpCompress.Compressors.LZMA;

namespace UserDimensions.Ledger;

public sealed class LedgerException(string code, int exitCode, string message, object? details = null) : Exception(message)
{
    public string Code { get; } = code;
    public int ExitCode { get; } = exitCode;
    public object Details { get; } = details ?? new { };
}

public static class Errors
{
    public static void Require(bool condition, string code, int exit, string message)
    { if (!condition) throw new LedgerException(code, exit, message); }
}

public sealed record ResourceLimits(long MaxRawBlockBytes = 16777216, long MaxDecompressedEventBytes = 67108864,
    long MaxInflightImportBytes = 134217728, long PerBlockTimeoutSeconds = 60)
{
    public void CheckRaw(long bytes) => Errors.Require(bytes <= MaxRawBlockBytes && bytes <= MaxInflightImportBytes / 4 && bytes <= int.MaxValue,
        "RESOURCE_LIMIT", 5, "Raw block exceeds configured size or bounded buffered implementation memory.");
}

public static class Binary
{
    public static readonly UTF8Encoding Utf8 = new(false, true);
    public static byte[] GuidBytes(Guid value) => value.ToByteArray(bigEndian: true);
    public static string Hex(ReadOnlySpan<byte> value) => Convert.ToHexStringLower(value);
    public static string Sha512(ReadOnlySpan<byte> value) => Hex(SHA512.HashData(value));
    public static int CompareGuid(Guid a, Guid b) => GuidBytes(a).AsSpan().SequenceCompareTo(GuidBytes(b));
    public static void WriteU64(Stream stream, ulong value) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, value); stream.Write(b); }
    public static void WriteI64(Stream stream, long value) => WriteU64(stream, unchecked((ulong)value));
    public static void WriteVar(Stream stream, ulong value)
    { do { var b = (byte)(value & 127); value >>= 7; stream.WriteByte((byte)(b | (value == 0 ? 0 : 128))); } while (value != 0); }
    public static void WriteText(Stream stream, string text)
    { var bytes = Utf8.GetBytes(text); WriteVar(stream, (ulong)bytes.Length); stream.Write(bytes); }
}

internal sealed class Cursor(byte[] data)
{
    public int Position { get; private set; }
    public int Remaining => data.Length - Position;
    public byte Byte() { Errors.Require(Remaining > 0, "BAD_EVENT", 2, "Truncated event."); return data[Position++]; }
    public byte[] Bytes(int count)
    { Errors.Require(count >= 0 && count <= Remaining, "BAD_EVENT", 2, "Truncated event field."); var b = data.AsSpan(Position, count).ToArray(); Position += count; return b; }
    public Guid Guid()
    { var id = new Guid(Bytes(16), bigEndian: true); Errors.Require(id != System.Guid.Empty, "BAD_EVENT", 2, "Zero entity identity."); return id; }
    public ulong Var()
    {
        ulong value = 0;
        for (var i = 0; i < 10; i++)
        {
            var b = Byte();
            Errors.Require(i != 9 || b <= 1, "BAD_EVENT", 2, "ULEB128 overflow.");
            value |= (ulong)(b & 127) << (i * 7);
            if ((b & 128) == 0) { Errors.Require(i == 0 || b != 0, "BAD_EVENT", 2, "Nonminimal ULEB128."); return value; }
        }
        throw new LedgerException("BAD_EVENT", 2, "ULEB128 overflow.");
    }
    public int Count() { var n = Var(); Errors.Require(n <= (ulong)Remaining, "BAD_EVENT", 2, "Event length/count exceeds remaining input."); return checked((int)n); }
    public string Text() { try { return Binary.Utf8.GetString(Bytes(Count())); } catch (DecoderFallbackException) { throw new LedgerException("BAD_EVENT", 2, "Invalid UTF-8."); } }
    public void End() => Errors.Require(Remaining == 0, "BAD_EVENT", 2, "Trailing event bytes.");
}

public sealed record LedgerValue(byte Tag, object? Value)
{
    public static LedgerValue Null => new(0, null);
    public static LedgerValue Integer(long value) => new(1, value);
    public static LedgerValue Real(double value) => new(2, value);
    public static LedgerValue Text(string value) => new(3, value);
    public static LedgerValue Blob(byte[] value) => new(4, value);
}
public sealed record ColumnDefinition(Guid Id, string Name, string DeclaredType, bool Nullable, LedgerValue? Default);
public sealed record RowField(Guid ColumnId, LedgerValue Value);
public sealed record LedgerEvent(byte Kind, Guid TableId = default, Guid EntityId = default, string? Name = null,
    IReadOnlyList<RowField>? Fields = null, IReadOnlyList<ColumnDefinition>? Columns = null,
    byte[]? PublicKey = null, byte[]? Countersigner = null, byte[]? Countersignature = null);

public static class EventCodec
{
    public static LedgerEvent Decode(byte[] bytes)
    {
        var c = new Cursor(bytes); var kind = c.Byte(); LedgerEvent e;
        switch (kind)
        {
            case 1: case 2:
                var table = c.Guid(); var row = c.Guid(); var n = c.Count(); var fields = new List<RowField>(); Guid? previous = null;
                for (var i = 0; i < n; i++)
                {
                    var id = c.Guid(); Errors.Require(previous == null || Binary.CompareGuid(previous.Value, id) < 0, "BAD_EVENT", 2, "Row columns must be unique and sorted by raw GUID.");
                    var v = ReadValue(c); Errors.Require(kind != 1 || v.Tag != 0, "BAD_EVENT", 2, "INSERT omits NULL-valued fields."); fields.Add(new(id, v)); previous = id;
                }
                e = new(kind, table, row, Fields: fields); break;
            case 3: e = new(kind, c.Guid(), c.Guid()); break;
            case 0x10:
                table = c.Guid(); var name = c.Text(); n = c.Count(); var columns = new List<ColumnDefinition>(); var ids = new HashSet<Guid>();
                for (var i = 0; i < n; i++) { var col = ReadColumn(c); Errors.Require(ids.Add(col.Id), "BAD_EVENT", 2, "Duplicate column identity."); columns.Add(col); }
                e = new(kind, table, Name: name, Columns: columns); break;
            case 0x11: e = new(kind, c.Guid()); break;
            case 0x12: e = new(kind, c.Guid(), Name: c.Text()); break;
            case 0x13: case 0x16: e = new(kind, c.Guid(), Columns: [ReadColumn(c)]); break;
            case 0x14: e = new(kind, c.Guid(), c.Guid()); break;
            case 0x15: e = new(kind, c.Guid(), c.Guid(), c.Text()); break;
            case 0x20: e = new(kind, PublicKey: c.Bytes(32)); break;
            case 0x21: e = new(kind, PublicKey: c.Bytes(32), Countersigner: c.Bytes(32), Countersignature: c.Bytes(64)); break;
            default: throw new LedgerException("BAD_EVENT", 2, "Unsupported event kind.");
        }
        c.End(); return e;
    }
    private static LedgerValue ReadValue(Cursor c)
    {
        var tag = c.Byte();
        switch (tag)
        {
            case 0: return LedgerValue.Null;
            case 1: var v = c.Var(); return LedgerValue.Integer(unchecked((long)(v >> 1) ^ -((long)v & 1)));
            case 2:
                var bits = BinaryPrimitives.ReadInt64BigEndian(c.Bytes(8)); var d = BitConverter.Int64BitsToDouble(bits);
                Errors.Require(!double.IsNaN(d), "BAD_EVENT", 2, "REAL NaN is invalid in protocol 1.01."); return LedgerValue.Real(d);
            case 3: return LedgerValue.Text(c.Text());
            case 4: return LedgerValue.Blob(c.Bytes(c.Count()));
            default: throw new LedgerException("BAD_EVENT", 2, "Unsupported value tag.");
        }
    }
    private static ColumnDefinition ReadColumn(Cursor c)
    {
        var id = c.Guid(); var name = c.Text(); var type = c.Text(); var flags = c.Byte(); var presence = c.Byte();
        Errors.Require(flags <= 1 && presence <= 1, "BAD_EVENT", 2, "Reserved column flags/default marker.");
        return new(id, name, type, flags == 1, presence == 1 ? ReadValue(c) : null);
    }
    public static byte[] Encode(LedgerEvent e)
    {
        using var s = new MemoryStream(); s.WriteByte(e.Kind);
        void Id(Guid id) => s.Write(Binary.GuidBytes(id));
        switch (e.Kind)
        {
            case 1: case 2:
                Id(e.TableId); Id(e.EntityId);
                var fields = (e.Fields ?? []).Where(x => e.Kind != 1 || x.Value.Tag != 0).OrderBy(x => x.ColumnId, Comparer<Guid>.Create(Binary.CompareGuid)).ToList();
                Binary.WriteVar(s, (ulong)fields.Count); foreach (var f in fields) { Id(f.ColumnId); WriteValue(s, f.Value); } break;
            case 3: case 0x14: Id(e.TableId); Id(e.EntityId); break;
            case 0x10:
                Id(e.TableId); Binary.WriteText(s, e.Name ?? ""); Binary.WriteVar(s, (ulong)(e.Columns?.Count ?? 0));
                foreach (var col in e.Columns ?? []) WriteColumn(s, col); break;
            case 0x11: Id(e.TableId); break;
            case 0x12: Id(e.TableId); Binary.WriteText(s, e.Name ?? ""); break;
            case 0x13: case 0x16:
                Id(e.TableId); Errors.Require(e.Columns?.Count == 1, "BAD_EVENT", 2, "One column definition required."); WriteColumn(s, e.Columns![0]); break;
            case 0x15: Id(e.TableId); Id(e.EntityId); Binary.WriteText(s, e.Name ?? ""); break;
            case 0x20: s.Write(e.PublicKey ?? []); break;
            case 0x21: s.Write(e.PublicKey ?? []); s.Write(e.Countersigner ?? []); s.Write(e.Countersignature ?? []); break;
            default: throw new LedgerException("BAD_EVENT", 2, "Unsupported event kind.");
        }
        var bytes = s.ToArray(); Decode(bytes); return bytes;
    }
    private static void WriteColumn(Stream s, ColumnDefinition c)
    {
        s.Write(Binary.GuidBytes(c.Id)); Binary.WriteText(s, c.Name); Binary.WriteText(s, c.DeclaredType); s.WriteByte(c.Nullable ? (byte)1 : (byte)0);
        s.WriteByte(c.Default == null ? (byte)0 : (byte)1); if (c.Default != null) WriteValue(s, c.Default);
    }
    private static void WriteValue(Stream s, LedgerValue v)
    {
        s.WriteByte(v.Tag);
        switch (v.Tag)
        {
            case 0: break;
            case 1: var n = (long)v.Value!; Binary.WriteVar(s, unchecked((ulong)((n << 1) ^ (n >> 63)))); break;
            case 2: var d = (double)v.Value!; Errors.Require(!double.IsNaN(d), "BAD_EVENT", 2, "REAL NaN."); Binary.WriteI64(s, BitConverter.DoubleToInt64Bits(d)); break;
            case 3: Binary.WriteText(s, (string)v.Value!); break;
            case 4: var b = (byte[])v.Value!; Binary.WriteVar(s, (ulong)b.Length); s.Write(b); break;
            default: throw new LedgerException("BAD_EVENT", 2, "Unsupported value.");
        }
    }
}

public static class Signing
{
    public static byte[] PublicKey(byte[] seed) { Errors.Require(seed.Length == 32, "IDENTITY_MISMATCH", 4, "Invalid seed length."); var pk = new byte[32]; Ed25519.GeneratePublicKey(seed, 0, pk, 0); return pk; }
    public static byte[] Sign(byte[] seed, byte[] message) { var sig = new byte[64]; Ed25519.Sign(seed, 0, message, 0, message.Length, sig, 0); return sig; }
    public static bool Verify(byte[] key, byte[] message, byte[] signature)
    { try { return key.Length == 32 && signature.Length == 64 && Ed25519.Verify(signature, 0, key, 0, message, 0, message.Length); } catch (ArgumentException) { return false; } }
    public static byte[] RevocationIntent(Guid ledger, Guid block, long time, byte[] outer, byte[] target, byte[] counter)
    {
        Errors.Require(outer.Length == 32 && target.Length == 32 && counter.Length == 32, "BAD_EVENT", 2, "Revocation key length.");
        using var s = new MemoryStream(); s.Write("DDL-REVOKE-v1"u8); s.Write(Binary.GuidBytes(ledger)); s.Write(Binary.GuidBytes(block)); Binary.WriteI64(s, time); s.Write(outer); s.Write(target); s.Write(counter); return s.ToArray();
    }
}

public sealed record SignedBlock(Guid LedgerId, Guid Id, long Timestamp, byte[] Signer, byte Compression,
    LedgerEvent? Event, byte[] EventBytes, byte[] Raw)
{
    public string Hash => Binary.Sha512(Raw);
    public bool IsGenesis => Event == null;
    public static IOrderedEnumerable<SignedBlock> Ordered(IEnumerable<SignedBlock> blocks) => blocks.OrderBy(x => x.Timestamp)
        .ThenBy(x => x.Id, Comparer<Guid>.Create(Binary.CompareGuid)).ThenBy(x => Binary.Hex(x.Signer), StringComparer.Ordinal).ThenBy(x => x.Hash, StringComparer.Ordinal);
}

public static class BlockCodec
{
    public static byte[] Create(Guid ledger, Guid block, long timestamp, byte[] seed, byte[]? eventBytes, bool compress = false)
    {
        Errors.Require(ledger != Guid.Empty && block != Guid.Empty, "BAD_FRAME", 2, "Zero block/ledger identity.");
        if (eventBytes != null) EventCodec.Decode(eventBytes);
        var payload = eventBytes == null ? [] : compress ? Lzma2.Encode(eventBytes) : eventBytes;
        using var s = new MemoryStream(); s.Write([1, 1]); s.Write(Binary.GuidBytes(ledger)); s.Write(Binary.GuidBytes(block)); Binary.WriteI64(s, timestamp);
        s.Write(Signing.PublicKey(seed)); s.Write(SHA512.HashData(payload)); Binary.WriteU64(s, (ulong)payload.Length); s.WriteByte(eventBytes != null && compress ? (byte)1 : (byte)0); s.Write(payload);
        var message = s.ToArray(); s.Write(Signing.Sign(seed, message)); return s.ToArray();
    }
    public static SignedBlock Parse(byte[] raw, ResourceLimits? limits = null)
    {
        limits ??= new(); limits.CheckRaw(raw.Length); var watch = Stopwatch.StartNew();
        Errors.Require(raw.Length >= 2, "BAD_FRAME", 2, "Truncated version.");
        Errors.Require(raw[0] == 1 && raw[1] == 1, "UNSUPPORTED_VERSION", 2, "Only signed protocol 1.01 is supported.");
        Errors.Require(raw.Length >= 211, "BAD_FRAME", 2, "Truncated envelope.");
        var length = BinaryPrimitives.ReadUInt64BigEndian(raw.AsSpan(138, 8));
        Errors.Require(length == (ulong)(raw.Length - 211), "BAD_FRAME", 2, "Payload length mismatch.");
        var ledger = new Guid(raw.AsSpan(2, 16), true); var id = new Guid(raw.AsSpan(18, 16), true);
        Errors.Require(ledger != Guid.Empty && id != Guid.Empty, "BAD_FRAME", 2, "Zero ledger/block GUID.");
        var timestamp = BinaryPrimitives.ReadInt64BigEndian(raw.AsSpan(34, 8)); var key = raw.AsSpan(42, 32).ToArray(); var payload = raw.AsSpan(147, (int)length).ToArray();
        Errors.Require(CryptographicOperations.FixedTimeEquals(SHA512.HashData(payload), raw.AsSpan(74, 64)), "HASH_MISMATCH", 2, "Transmitted payload hash mismatch.");
        Errors.Require(Signing.Verify(key, raw.AsSpan(0, raw.Length - 64).ToArray(), raw[^64..]), "BAD_SIGNATURE", 2, "Invalid Ed25519 block signature.");
        var compression = raw[146]; Errors.Require(compression <= 1 && (length != 0 || compression == 0), "BAD_FRAME", 2, "Unsupported compression/genesis.");
        var bytes = compression == 1 ? Lzma2.Decode(payload, limits, watch) : payload;
        Errors.Require(bytes.LongLength <= limits.MaxDecompressedEventBytes, "RESOURCE_LIMIT", 5, "Decoded event exceeds limit.");
        Errors.Require(watch.Elapsed.TotalSeconds <= limits.PerBlockTimeoutSeconds, "PROCESSING_TIMEOUT", 5, "Block processing deadline exceeded.");
        var e = length == 0 ? null : EventCodec.Decode(bytes);
        if (e?.Kind == 0x21)
        {
            Errors.Require(!key.AsSpan().SequenceEqual(e.Countersigner) && (key.AsSpan().SequenceEqual(e.PublicKey) || e.Countersigner!.AsSpan().SequenceEqual(e.PublicKey)), "BAD_EVENT", 2, "Invalid revocation signer roles.");
            Errors.Require(Signing.Verify(e.Countersigner!, Signing.RevocationIntent(ledger, id, timestamp, key, e.PublicKey!, e.Countersigner!), e.Countersignature!), "BAD_SIGNATURE", 2, "Invalid revocation consent.");
        }
        return new(ledger, id, timestamp, key, compression, e, bytes, raw.ToArray());
    }
}

public static class Lzma2
{
    // LZMA2's stored-chunk mode is interoperable raw LZMA2. No archive/header is emitted.
    // Incompressible output is deliberately permitted; decoding also supports compressed chunks.
    public static byte[] Encode(byte[] data)
    {
        using var s = new MemoryStream(); s.WriteByte(20);
        for (var pos = 0; pos < data.Length;)
        {
            var n = Math.Min(65536, data.Length - pos); s.WriteByte(pos == 0 ? (byte)1 : (byte)2);
            s.WriteByte((byte)((n - 1) >> 8)); s.WriteByte((byte)(n - 1)); s.Write(data, pos, n); pos += n;
        }
        s.WriteByte(0); return s.ToArray();
    }
    public static byte[] Decode(byte[] payload, ResourceLimits limits, Stopwatch? watch = null)
    {
        watch ??= Stopwatch.StartNew(); Errors.Require(payload.Length >= 2 && payload[0] == 20, "BAD_EVENT", 2, "LZMA2 requires property 20.");
        long decoded = 0; var pos = 1; var ended = false;
        // Validate the raw packet framing, declared output bounds and exact EOS before decoder allocation.
        while (pos < payload.Length)
        {
            var control = payload[pos++]; if (control == 0) { ended = true; break; }
            Errors.Require((control is 1 or 2 || control >= 128) && payload.Length - pos >= 2, "BAD_EVENT", 2, "Invalid/truncated LZMA2 packet.");
            var unpacked = ((payload[pos] << 8) | payload[pos + 1]) + 1; pos += 2; var packed = unpacked;
            if (control >= 128)
            {
                unpacked += (control & 31) << 16;
                Errors.Require(payload.Length - pos >= 2, "BAD_EVENT", 2, "Truncated LZMA2 size."); packed = ((payload[pos] << 8) | payload[pos + 1]) + 1; pos += 2;
                if (control >= 192) { Errors.Require(pos < payload.Length, "BAD_EVENT", 2, "Missing LZMA properties."); pos++; }
            }
            decoded += unpacked;
            Errors.Require(decoded <= limits.MaxDecompressedEventBytes && decoded <= int.MaxValue && decoded + payload.LongLength * 4 <= limits.MaxInflightImportBytes, "RESOURCE_LIMIT", 5, "LZMA2 decoded/memory limit.");
            Errors.Require(packed <= payload.Length - pos, "BAD_EVENT", 2, "Truncated LZMA2 data."); pos += packed;
        }
        Errors.Require(ended && pos == payload.Length, "BAD_EVENT", 2, "LZMA2 needs exact EOS with no trailing garbage.");
        try
        {
            using var input = new MemoryStream(payload, 1, payload.Length - 1, false);
            using var decoder = new LzmaStream([20], input);
            using var output = new MemoryStream(); var buffer = new byte[8192]; int count;
            while ((count = decoder.Read(buffer)) != 0)
            {
                Errors.Require(output.Length + count <= decoded, "BAD_EVENT", 2, "Decoded length mismatch."); output.Write(buffer, 0, count);
                Errors.Require(watch.Elapsed.TotalSeconds <= limits.PerBlockTimeoutSeconds, "PROCESSING_TIMEOUT", 5, "LZMA2 deadline.");
            }
            Errors.Require(output.Length == decoded, "BAD_EVENT", 2, "Decoded length mismatch."); return output.ToArray();
        }
        catch (LedgerException) { throw; }
        catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException) { throw new LedgerException("BAD_EVENT", 2, "Malformed LZMA2 stream."); }
    }
}
