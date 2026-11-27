using System.Security.Cryptography;

namespace UserDimensions.Ledger;

// Private staging representation. The caller supplies a coherent expected snapshot, including
// the original instance identity. No credential, registration or operational authority is inferred.
internal sealed record IntentBinding(Guid Request, Guid Business, InstanceManifest Original, ulong Epoch);
internal sealed record IntentEvent(Guid BlockId, byte[] Body);

internal sealed class FrozenIntent
{
    internal const int MaxEvents = 128, MaxBody = 64 * 1024, MaxTotal = 1024 * 1024;
    internal Guid Request { get; }
    internal int Count => events.Length;
    private readonly IntentEvent[] events;
    private readonly byte[] manifest;
    private readonly int total;

    private FrozenIntent(Guid request, IntentEvent[] events, byte[] manifest, int total)
        => (Request, this.events, this.manifest, this.total) = (request, events, manifest, total);

    internal IntentEvent EventAt(int index) => new(events[index].BlockId, events[index].Body.ToArray());

    internal static FrozenIntent Capture(IntentBinding binding, IntentEvent[] input)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(input);
        var original = binding.Original;
        if (binding.Request == Guid.Empty || binding.Business == Guid.Empty || original?.InstanceId is null ||
            original.InstanceId == Guid.Empty || original.Binding is null || input.Length is < 1 or > MaxEvents)
            throw new ArgumentException("Incomplete binding or event count.");
        var signer = StrictJson.Hex(original.PublicKey, 32);
        var ledger = StrictJson.Hex(original.Binding.LedgerId, 32);
        var genesis = StrictJson.Hex(original.Binding.GenesisHash, 64);
        var entries = input.ToArray();
        var ids = new HashSet<Guid>();
        int total = 0;
        foreach (var e in entries)
        {
            if (e?.Body is null || e.BlockId == Guid.Empty || !ids.Add(e.BlockId) ||
                e.Body.Length is < 1 or > MaxBody || e.Body.Length > MaxTotal - total)
                throw new ArgumentException("Invalid event identity or size.");
            total += e.Body.Length;
        }

        // Versioned internal manifest: network-order GUIDs, little-endian integers, ordered
        // block IDs, lengths and SHA-256 of the exact uncompressed Protocol 1.03 bodies.
        using var bytes = new MemoryStream();
        using var m = new BinaryWriter(bytes);
        m.Write("UDDLFIM1"u8);
        m.Write(Binary.GuidBytes(binding.Request));
        m.Write(Binary.GuidBytes(binding.Business));
        m.Write(Binary.GuidBytes(original.InstanceId));
        m.Write(signer);
        m.Write(ledger);
        m.Write(genesis);
        m.Write(binding.Epoch);
        m.Write(entries.Length);
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            var body = e.Body.ToArray();
            _ = EventCodec.Decode(body);
            entries[i] = new(e.BlockId, body);
            m.Write(Binary.GuidBytes(e.BlockId));
            m.Write(body.Length);
            m.Write(SHA256.HashData(body));
        }
        return new(binding.Request, entries, bytes.ToArray(), total);
    }

    internal byte[] ManifestCopy() => manifest.ToArray();
    internal byte[] PayloadCopy()
    {
        var bytes = new byte[total];
        int offset = 0;
        foreach (var e in events)
        {
            e.Body.CopyTo(bytes, offset);
            offset += e.Body.Length;
        }
        return bytes;
    }

    internal byte[] Transition(bool ready)
    {
        var bytes = new byte[40];
        (ready ? "UDDLFIR1"u8 : "UDDLFIL1"u8).CopyTo(bytes);
        SHA256.HashData(manifest, bytes.AsSpan(8));
        return bytes;
    }

    internal bool MatchesFiles(string slot)
    {
        var storedManifest = Paths.ReadBounded(Path.Combine(slot, "manifest"), manifest.Length);
        if (!storedManifest.AsSpan().SequenceEqual(manifest)) return false;
        var payload = Paths.ReadBounded(Path.Combine(slot, "payload"), total);
        if (payload.Length != total) return false;
        int offset = 0;
        foreach (var e in events)
        {
            if (!payload.AsSpan(offset, e.Body.Length).SequenceEqual(e.Body)) return false;
            offset += e.Body.Length;
        }
        return true;
    }
}

internal enum FrozenReadiness { Restricted, ObservedExact, WriteCompleted }

// Pre-existing service-private root, stable during inspection. Only cooperating constructors
// write here, and none change an existing slot. Reads require independently supplied exact intent.
// Historical flush acknowledgment, authentication and custody cannot be derived from a read.
internal sealed class FrozenIntentStore
{
    private readonly string root;
    private readonly DurableFiles files;

    internal FrozenIntentStore(string root, DurableFiles files)
    {
        this.root = Paths.Absolute(root);
        if (!Directory.Exists(this.root)) throw new DirectoryNotFoundException(this.root);
        this.files = files ?? throw new ArgumentNullException(nameof(files));
    }

    private string Slot(FrozenIntent expected) => Path.Combine(root, expected.Request.ToString("N"));

    internal FrozenReadiness Observe(FrozenIntent expected)
    {
        string slot = Slot(expected);
        try
        {
            Paths.Safe(slot);
            var missing = new HashSet<string>(["sealed", "manifest", "payload", "ready"], StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateFileSystemEntries(slot))
            {
                if (!missing.Remove(Path.GetFileName(path)) ||
                    (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                    return FrozenReadiness.Restricted;
            }
            if (missing.Count != 0 || !MarkerMatches(slot, "sealed", expected.Transition(false)) ||
                !MarkerMatches(slot, "ready", expected.Transition(true)) || !expected.MatchesFiles(slot))
                return FrozenReadiness.Restricted;
            return FrozenReadiness.ObservedExact;
        }
        catch (Exception ex) when (StorageFailure(ex)) { return FrozenReadiness.Restricted; }
    }

    internal FrozenReadiness Stage(FrozenIntent expected)
    {
        string slot = Slot(expected);
        try
        {
            Paths.Safe(slot);
            try { _ = File.GetAttributes(slot); return Observe(expected); }
            catch (FileNotFoundException) { }
            // Move without replacement is the sole construction claim. A losing or interrupted
            // attempt may leave an empty scratch directory outside the slot; never reclaim it.
            var empty = Path.Combine(root, ".allocate-" + Guid.NewGuid().ToString("N"));
            files.Directory(empty);
            files.Move(empty, slot);
            files.Publish(Path.Combine(slot, ".lock"), expected.Transition(false));
            files.Publish(Path.Combine(slot, "payload"), expected.PayloadCopy());
            files.Publish(Path.Combine(slot, "manifest"), expected.ManifestCopy());
            if (!expected.MatchesFiles(slot)) return FrozenReadiness.Restricted;
            files.Publish(Path.Combine(slot, "ready"), expected.Transition(true));
            if (!MarkerMatches(slot, "ready", expected.Transition(true))) return FrozenReadiness.Restricted;
            // Same-directory, write-through, no-replace rename retires the active marker and
            // retains its exact bytes. A lost acknowledgment always restricts this invocation.
            files.Move(Path.Combine(slot, ".lock"), Path.Combine(slot, "sealed"));
            return Observe(expected) == FrozenReadiness.ObservedExact
                ? FrozenReadiness.WriteCompleted : FrozenReadiness.Restricted;
        }
        catch (Exception ex) when (StorageFailure(ex)) { return FrozenReadiness.Restricted; }
    }

    private static bool MarkerMatches(string slot, string name, byte[] expected)
        => Paths.ReadBounded(Path.Combine(slot, name), expected.Length).AsSpan().SequenceEqual(expected);

    private static bool StorageFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or LedgerException;
}
