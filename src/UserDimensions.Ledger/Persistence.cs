using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UserDimensions.Ledger;

public static class Paths
{
    public static string Absolute(string path)
    {
        Errors.Require(Path.IsPathFullyQualified(path), "INVALID_ARGUMENT", 2, "Absolute path required.");
        try { var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); Safe(full); return full; }
        catch(Exception e) when(e is ArgumentException or NotSupportedException) { throw new LedgerException("INVALID_ARGUMENT",2,"Unsupported absolute path."); }
    }
    public static void Safe(string path)
    {
        Errors.Require(path.Length < 32000, "INVALID_ARGUMENT", 2, "Path too long.");
        for (var p = path; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
        {
            var name = Path.GetFileName(p);
            Errors.Require(name.Length <= 255 && (!OperatingSystem.IsWindows() || (!name.EndsWith('.') && !name.EndsWith(' '))), "INVALID_ARGUMENT", 2, "Unsupported path component.");
            if (File.Exists(p) || Directory.Exists(p)) Errors.Require((File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0, "INVALID_ARGUMENT", 2, "Symlink/junction paths are not supported.");
        }
    }
    public static byte[] ReadBounded(string path, long limit, string code = "RESOURCE_LIMIT")
    {
        Safe(path); using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Errors.Require(stream.Length <= limit && stream.Length <= int.MaxValue, code, code == "RESOURCE_LIMIT" ? 5 : 4, "Input exceeds bounded reader limit.");
        var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); return bytes;
    }
}

/// <summary>All mutation goes through this adapter. Fault callbacks are explicit injected test dependencies; no production environment-variable switch.</summary>
public sealed class DurableFiles(Action<string>? checkpoint = null)
{
    public void Checkpoint(string name) => checkpoint?.Invoke(name);
    public void Directory(string path)
    {
        Paths.Safe(path);
        if (System.IO.Directory.Exists(path)) return;
        var parent = Path.GetDirectoryName(path)!; Directory(parent);
        var temporary = Path.Combine(parent, ".uddl-dir-" + Guid.NewGuid().ToString("N") + ".tmp");
        System.IO.Directory.CreateDirectory(temporary); Move(temporary, path);
    }
    public void Publish(string path, byte[] bytes, bool replace = false)
    {
        Paths.Safe(path); Errors.Require(!System.IO.Directory.Exists(path), "OUTPUT_EXISTS", 4, "Destination is a directory.");
        if (!replace) Errors.Require(!File.Exists(path), "OUTPUT_EXISTS", 4, "Destination already exists.");
        Directory(Path.GetDirectoryName(path)!);
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, ".uddl-" + Guid.NewGuid().ToString("N") + ".tmp");
        Checkpoint("before-write:" + Path.GetFileName(path));
        // Temp remnants are preserved on failure for explicit diagnosis/recovery.
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            stream.Write(bytes); stream.Flush(flushToDisk: true);
        }
        Checkpoint("after-flush:" + Path.GetFileName(path));
        Move(temporary, path, replace);
        Checkpoint("after-publish:" + Path.GetFileName(path));
    }
    public void Move(string source, string destination, bool replace = false)
    {
        Paths.Safe(source); Paths.Safe(destination); Directory(Path.GetDirectoryName(destination)!);
        Checkpoint("before-rename:" + Path.GetFileName(destination));
        if (OperatingSystem.IsWindows())
        {
            var drive = new DriveInfo(Path.GetPathRoot(destination)!);
            Errors.Require(drive.DriveType == DriveType.Fixed && drive.DriveFormat == "NTFS", "DURABILITY_UNCERTAIN", 6, "Initial Windows durability procedure is limited to local NTFS.");
            // No COPY_ALLOWED: transitions stay on the same volume. REPLACE_EXISTING only for explicitly replaceable config/manifest.
            if (!MoveFileExW(Extended(source), Extended(destination), 8u | (replace ? 1u : 0u)))
            {
                var error = Marshal.GetLastWin32Error();
                throw new LedgerException(error is 80 or 183 ? "OUTPUT_EXISTS" : "IO_ERROR", error is 80 or 183 ? 4 : 6,
                    "Windows rename failed (" + error + "): " + new Win32Exception(error).Message, new { path = destination, native_error = error });
            }
        }
        else
        {
            // Portable format, but no claim of durable directory publication on an unqualified platform.
            throw new LedgerException("DURABILITY_UNCERTAIN", 6, "Filesystem publication is currently qualified only for Windows NTFS.");
        }
        Checkpoint("after-rename:" + Path.GetFileName(destination));
    }
    private static string Extended(string path) => path.Length < 260 || path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileExW(string existing, string destination, uint flags);
}

public static class StrictJson
{
    public static JsonDocument Parse(byte[] bytes, int max = 4096)
    {
        try
        {
            Errors.Require(bytes.Length <= max && !bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }), "IDENTITY_MISMATCH", 4, "Invalid JSON length/BOM.");
            Binary.Utf8.GetString(bytes);
            var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            try { ValidateDuplicates(doc.RootElement); return doc; } catch { doc.Dispose(); throw; }
        }
        catch (Exception e) when (e is JsonException or DecoderFallbackException) { throw new LedgerException("IDENTITY_MISMATCH", 4, "Invalid JSON/UTF-8."); }
    }
    private static void ValidateDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject()) { Errors.Require(names.Add(property.Name), "IDENTITY_MISMATCH", 4, "Duplicate JSON field."); ValidateDuplicates(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) ValidateDuplicates(child);
    }
    public static void Fields(JsonElement root, params string[] expected)
    { Errors.Require(root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(expected), "IDENTITY_MISMATCH", 4, "Missing/unknown JSON fields."); }
    public static string Text(JsonElement root, string name)
    { var e = root.GetProperty(name); Errors.Require(e.ValueKind == JsonValueKind.String, "IDENTITY_MISMATCH", 4, "Expected string."); return e.GetString()!; }
    public static long Integer(JsonElement root, string name)
    {
        var e = root.GetProperty(name); Errors.Require(e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out _) && e.GetRawText().All(c => c is >= '0' and <= '9'), "IDENTITY_MISMATCH", 4, "Expected nonnegative JSON integer."); return e.GetInt64();
    }
    public static Guid Uuid(string text, string code = "IDENTITY_MISMATCH")
    { Errors.Require(Guid.TryParseExact(text, "D", out var id) && id != Guid.Empty && id.ToString("D") == text, code, code == "INVALID_ARGUMENT" ? 2 : 4, "Expected canonical nonzero UUID."); return id; }
    public static byte[] Hex(string text, int length, string code = "IDENTITY_MISMATCH")
    { Errors.Require(text.Length == length * 2 && text.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), code, code == "INVALID_ARGUMENT" ? 2 : 4, "Invalid lowercase hexadecimal."); return Convert.FromHexString(text); }
    public static byte[] Base64(string text, int length)
    {
        try { var bytes = Convert.FromBase64String(text); Errors.Require(bytes.Length == length && Convert.ToBase64String(bytes) == text, "IDENTITY_MISMATCH", 4, "Invalid Base64/length."); return bytes; }
        catch (FormatException) { throw new LedgerException("IDENTITY_MISMATCH", 4, "Invalid Base64."); }
    }
    public static byte[] Write(object value) => Binary.Utf8.GetBytes(JsonSerializer.Serialize(value) + "\n");
}

public sealed record LedgerBinding(Guid LedgerId, string GenesisHash);
public sealed record InstanceManifest(Guid InstanceId, string PublicKey, LedgerBinding? Binding)
{
    public byte[] Encode() => StrictJson.Write(new { format = "UDDL-INSTANCE-IDENTITY", version = 1, instance_guid = InstanceId.ToString("D"), public_key_hex = PublicKey,
        key_file = "signing-key.v1.json", ledger_binding = Binding == null ? null : new { ledger_guid = Binding.LedgerId.ToString("D"), genesis_sha512 = Binding.GenesisHash } });
    public static InstanceManifest Decode(byte[] bytes)
    {
        using var doc = StrictJson.Parse(bytes); var r = doc.RootElement;
        StrictJson.Fields(r, "format", "version", "instance_guid", "public_key_hex", "key_file", "ledger_binding");
        Errors.Require(StrictJson.Text(r,"format") == "UDDL-INSTANCE-IDENTITY" && StrictJson.Integer(r,"version") == 1 && StrictJson.Text(r,"key_file") == "signing-key.v1.json", "IDENTITY_MISMATCH", 4, "Unsupported manifest format.");
        var id = StrictJson.Uuid(StrictJson.Text(r,"instance_guid")); var key = StrictJson.Text(r,"public_key_hex"); StrictJson.Hex(key,32);
        var b = r.GetProperty("ledger_binding"); LedgerBinding? binding = null;
        if (b.ValueKind != JsonValueKind.Null) { StrictJson.Fields(b,"ledger_guid","genesis_sha512"); var hash = StrictJson.Text(b,"genesis_sha512"); StrictJson.Hex(hash,64); binding = new(StrictJson.Uuid(StrictJson.Text(b,"ledger_guid")), hash); }
        return new(id,key,binding);
    }
}

public sealed class SigningIdentity(Guid instanceId, byte[] seed) : IDisposable
{
    // Public, application-wide SEC-002 obfuscation key, SHA-256 of this documented domain. It is intentionally not secret.
    private static readonly byte[] WrapKey = SHA256.HashData("UserDimensions/SEC-002/application-wide-v1/public-obfuscation-key"u8);
    public Guid InstanceId { get; } = instanceId;
    public byte[] PublicKey => Signing.PublicKey(seed);
    public byte[] Sign(byte[] message) => Signing.Sign(seed, message);
    internal byte[] CreateBlock(Guid ledger, Guid block, long timestamp, byte[]? body, bool compress = false) => BlockCodec.Create(ledger, block, timestamp, seed, body, compress);
    public void Dispose() => CryptographicOperations.ZeroMemory(seed);
    public static byte[] Aad(Guid id, byte[] key)
    {
        using var s = new MemoryStream(); s.Write("UDDL-ED25519-KEY-v1"u8); s.Write(Binary.GuidBytes(id)); s.Write(key); s.Write([0,19]); s.Write("application-wide-v1"u8); return s.ToArray();
    }
    public byte[] Encode()
    {
        var nonce = RandomNumberGenerator.GetBytes(12); var ciphertext = new byte[32]; var tag = new byte[16];
        using var aes = new AesGcm(WrapKey,16); aes.Encrypt(nonce,seed,ciphertext,tag,Aad(InstanceId,PublicKey));
        return StrictJson.Write(new { format="UDDL-ED25519-KEY", version=1, instance_guid=InstanceId.ToString("D"), public_key_hex=Binary.Hex(PublicKey), wrap_key_id="application-wide-v1", encryption="AES-256-GCM", nonce_b64=Convert.ToBase64String(nonce), ciphertext_b64=Convert.ToBase64String(ciphertext), tag_b64=Convert.ToBase64String(tag) });
    }
    public static SigningIdentity Decode(byte[] bytes)
    {
        using var doc = StrictJson.Parse(bytes); var r = doc.RootElement;
        StrictJson.Fields(r,"format","version","instance_guid","public_key_hex","wrap_key_id","encryption","nonce_b64","ciphertext_b64","tag_b64");
        Errors.Require(StrictJson.Text(r,"format") == "UDDL-ED25519-KEY" && StrictJson.Integer(r,"version") == 1 && StrictJson.Text(r,"wrap_key_id") == "application-wide-v1" && StrictJson.Text(r,"encryption") == "AES-256-GCM", "IDENTITY_MISMATCH", 4, "Unsupported signing-key format/key identifier.");
        var id = StrictJson.Uuid(StrictJson.Text(r,"instance_guid")); var pk = StrictJson.Hex(StrictJson.Text(r,"public_key_hex"),32);
        var nonce = StrictJson.Base64(StrictJson.Text(r,"nonce_b64"),12); var ciphertext=StrictJson.Base64(StrictJson.Text(r,"ciphertext_b64"),32); var tag=StrictJson.Base64(StrictJson.Text(r,"tag_b64"),16); var seed=new byte[32];
        try
        {
            using var aes=new AesGcm(WrapKey,16); aes.Decrypt(nonce,ciphertext,tag,seed,Aad(id,pk));
            Errors.Require(CryptographicOperations.FixedTimeEquals(Signing.PublicKey(seed),pk), "IDENTITY_MISMATCH",4,"Derived public key differs."); return new(id,seed);
        }
        catch { CryptographicOperations.ZeroMemory(seed); throw new LedgerException("IDENTITY_MISMATCH",4,"Signing key authentication or identity mismatch."); }
    }
    public static SigningIdentity Create() => new(Guid.NewGuid(), RandomNumberGenerator.GetBytes(32));
}

internal sealed class IdentityLease : IDisposable
{
    private static readonly HashSet<string> Active = new(StringComparer.Ordinal);
    private readonly string keyName;
    private readonly Mutex mutex;
    public IdentityLease(string key)
    {
        keyName = key;
        lock (Active) Errors.Require(Active.Add(key), "DUPLICATE_INSTANCE", 4, "Instance already active in this process.");
        try { mutex = new Mutex(false, (OperatingSystem.IsWindows() ? "Global\\" : "") + "UDDL-" + Binary.Hex(SHA256.HashData(Encoding.UTF8.GetBytes(key)))); }
        catch { lock (Active) Active.Remove(key); throw; }
        bool acquired; try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) { mutex.Dispose(); lock (Active) Active.Remove(key); throw new LedgerException("DUPLICATE_INSTANCE",4,"Instance already active on this host."); }
    }
    public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); lock (Active) Active.Remove(keyName); }
}
