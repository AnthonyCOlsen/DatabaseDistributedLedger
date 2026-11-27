using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace UserDimensions.Ledger;

internal sealed record ProgressTip(int Sequence, string HeadDigest);
internal sealed record ProgressEntry(Guid RecordId, int EventPosition, Guid BlockId, string BodyDigest);
internal enum ProgressState { Restricted, ObservedExact, UnconfirmedTail }
internal sealed record ProgressRead(ProgressState State, ProgressTip? MatchedHead,
    IReadOnlyList<ProgressEntry> VerifiedRecords, ProgressEntry? Ahead);
internal enum ProgressWrite { Refused, WriteUncertain, WriteCompleted }

// One trusted host exclusively serializes every operation (including disposal) for this
// intent in its pre-existing private journal root. No cross-process locking or freshness witness.
// The injected per-instance service key is independent of the ledger signer. Nothing here
// provisions keys or interprets an observation as a lifecycle transition or action permission.
internal sealed class IntentProgressJournal : IDisposable
{
    internal const int MaxRecords = 128;
    private const int Header = 8 + 32 + 4, Tag = 32, HeadSize = Header + 32 + Tag;
    private const int RecordSize = Header + 32 + 1 + 16 + 4 + 16 + 32 + Tag;
    private readonly FrozenIntent intent;
    private readonly byte[] binding, key;
    private readonly string slot;
    private readonly DurableFiles files;
    private bool disposed;

    internal IntentProgressJournal(string privateRoot, FrozenIntent intent, ReadOnlySpan<byte> serviceKey, DurableFiles files)
    {
        if (serviceKey.Length != 32) throw new ArgumentException("Expected a 32-byte service credential.");
        var root = Paths.Absolute(privateRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        this.intent = intent;
        this.files = files;
        slot = Path.Combine(root, intent.Request.ToString("N"));
        // Manifest includes request, original instance/signer/ledger/full genesis, epoch,
        // business association, ordered block IDs, lengths and exact-body digests.
        binding = SHA256.HashData(intent.ManifestCopy());
        key = serviceKey.ToArray();
    }

    private string FileAt(string name) => Path.Combine(slot, name);
    private static string Name(int seq) => seq.ToString("D4", CultureInfo.InvariantCulture) + ".rec";
    private static bool StorageError(Exception e) => e is IOException or UnauthorizedAccessException or LedgerException;

    // Explicit fresh initialization. No implicit recreation of an absent head on open/append.
    internal ProgressWrite Initialize()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        try
        {
            Paths.Safe(slot);
            try { _ = File.GetAttributes(slot); return ProgressWrite.Refused; }
            catch (FileNotFoundException) { }
            var unique = Path.Combine(Path.GetDirectoryName(slot)!, ".alloc-" + Guid.NewGuid().ToString("N"));
            files.Directory(unique);
            files.Move(unique, slot);
            var head = Frame(true, 0, new byte[32]);
            files.Publish(FileAt("head"), head);
            return ExactTip(Read(), 0, head) ? ProgressWrite.WriteCompleted : ProgressWrite.WriteUncertain;
        }
        catch (Exception e) when (StorageError(e)) { return ProgressWrite.WriteUncertain; }
    }

    private static bool ExactTip(ProgressRead read, int seq, byte[] head) =>
        read.State == ProgressState.ObservedExact && read.MatchedHead == Tip(seq, head);
    private static ProgressTip Tip(int seq, byte[] head) => new(seq, Convert.ToHexString(SHA256.HashData(head)));

    // Fixed frames. Magic carries version/domain; sequence and position are little endian,
    // IDs network order. Kind 1 records an event reference only. MAC covers every preceding byte.
    private byte[] Frame(bool head, int seq, byte[] link, Guid id = default, int pos = 0)
    {
        var bytes = new byte[head ? HeadSize : RecordSize];
        (head ? "UDDLJHD1"u8 : "UDDLJRC1"u8).CopyTo(bytes);
        binding.CopyTo(bytes, 8);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), seq);
        link.CopyTo(bytes, Header);
        if (!head)
        {
            var e = intent.EventAt(pos);
            var body = bytes.AsSpan(Header + 32);
            body[0] = 1;
            id.TryWriteBytes(body[1..17], true, out _);
            BinaryPrimitives.WriteInt32LittleEndian(body[17..21], pos);
            e.BlockId.TryWriteBytes(body[21..37], true, out _);
            SHA256.HashData(e.Body, body[37..69]);
        }
        HMACSHA256.HashData(key, bytes.AsSpan(..^Tag), bytes.AsSpan(^Tag..));
        return bytes;
    }

    private byte[]? OpenFrame(string name, bool head)
    {
        int size = head ? HeadSize : RecordSize;
        var bytes = Paths.ReadBounded(FileAt(name), size);
        if (bytes.Length != size || !bytes.AsSpan(0, 8).SequenceEqual(head ? "UDDLJHD1"u8 : "UDDLJRC1"u8) ||
            !bytes.AsSpan(8, 32).SequenceEqual(binding)) return null;
        return CryptographicOperations.FixedTimeEquals(bytes.AsSpan(^Tag..), HMACSHA256.HashData(key, bytes.AsSpan(..^Tag)))
            ? bytes : null;
    }

    private ProgressEntry? Decode(byte[]? raw, int sequence, byte[] previous, HashSet<Guid> ids)
    {
        if (raw == null || BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(40)) != sequence ||
            !raw.AsSpan(Header, 32).SequenceEqual(previous)) return null;
        var body = raw.AsSpan(Header + 32);
        var id = new Guid(body[1..17], true);
        int pos = BinaryPrimitives.ReadInt32LittleEndian(body[17..21]);
        if (body[0] != 1 || id == Guid.Empty || pos < 0 || pos >= intent.Count || !ids.Add(id)) return null;
        var expected = intent.EventAt(pos);
        if (new Guid(body[21..37], true) != expected.BlockId || !body[37..69].SequenceEqual(SHA256.HashData(expected.Body)))
            return null;
        return new(id, pos, expected.BlockId, Convert.ToHexString(body[37..69]));
    }

    // VerifiedRecords can be partial authenticated links. Only MatchedHead ties the whole
    // requested prefix to the separately authenticated head. An unmatched tail never advances it.
    internal ProgressRead Read()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var prefix = new List<ProgressEntry>();
        ProgressTip? tip = null;
        ProgressRead Result(ProgressState state, ProgressEntry? ahead = null) =>
            new(state, tip, prefix.AsReadOnly(), ahead);
        try
        {
            Paths.Safe(slot);
            var head = OpenFrame("head", true);
            if (head == null) return Result(ProgressState.Restricted);
            int seq = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(40));
            if (seq < 0 || seq > MaxRecords) return Result(ProgressState.Restricted);
            byte[] previous = new byte[32];
            var ids = new HashSet<Guid>();
            var names = new HashSet<string>(StringComparer.Ordinal) { "head" };
            for (int i = 1; i <= seq; i++)
            {
                names.Add(Name(i));
                var record = OpenFrame(Name(i), false);
                var entry = Decode(record, i, previous, ids);
                if (entry == null) return Result(ProgressState.Restricted);
                prefix.Add(entry);
                previous = SHA256.HashData(record!);
            }
            if (!head.AsSpan(Header, 32).SequenceEqual(previous)) return Result(ProgressState.Restricted);
            tip = Tip(seq, head);
            ProgressEntry? ahead = null;
            foreach (var path in Directory.EnumerateFileSystemEntries(slot))
            {
                var name = Path.GetFileName(path);
                if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                    return Result(ProgressState.Restricted);
                if (names.Remove(name)) continue;
                if (seq >= MaxRecords || ahead != null || name != Name(seq + 1)) return Result(ProgressState.Restricted);
                ahead = Decode(OpenFrame(name, false), seq + 1, previous, ids);
                if (ahead == null) return Result(ProgressState.Restricted);
            }
            if (names.Count != 0) return Result(ProgressState.Restricted);
            return Result(ahead == null ? ProgressState.ObservedExact : ProgressState.UnconfirmedTail, ahead);
        }
        catch (Exception e) when (StorageError(e)) { return Result(ProgressState.Restricted); }
    }

    internal ProgressWrite Append(ProgressTip expected, Guid recordId, int eventPosition)
    {
        var prior = Read();
        if (prior.State != ProgressState.ObservedExact || prior.MatchedHead != expected || expected is null ||
            expected.Sequence >= MaxRecords || recordId == Guid.Empty || eventPosition < 0 || eventPosition >= intent.Count ||
            prior.VerifiedRecords.Any(e => e.RecordId == recordId)) return ProgressWrite.Refused;
        try
        {
            var old = OpenFrame("head", true);
            if (old == null || Tip(expected.Sequence, old) != expected) return ProgressWrite.Refused;
            int next = expected.Sequence + 1;
            var record = Frame(false, next, old.AsSpan(Header, 32).ToArray(), recordId, eventPosition);
            files.Publish(FileAt(Name(next)), record);
            if (!Paths.ReadBounded(FileAt(Name(next)), RecordSize).AsSpan().SequenceEqual(record))
                return ProgressWrite.WriteUncertain;
            var head = Frame(true, next, SHA256.HashData(record));
            files.Publish(FileAt("head"), head, replace: true);
            return ExactTip(Read(), next, head) ? ProgressWrite.WriteCompleted : ProgressWrite.WriteUncertain;
        }
        catch (Exception e) when (StorageError(e)) { return ProgressWrite.WriteUncertain; }
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(key);
        disposed = true;
    }
}
