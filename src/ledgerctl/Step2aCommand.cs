using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using System.Security.Principal;
using UserDimensions.Ledger;

[SupportedOSPlatform("windows")]
internal static class Step2aCommand
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafePipeHandle CreateNamedPipeW(string name, uint openMode,
        uint pipeMode, uint instances, uint outSize, uint inSize, uint timeout, IntPtr security);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
    private const int WaitMs = 10000;
    private const byte Published = 1, Unenrolled = 2, Uncertain = 3;
    private static readonly Guid Table = new("11111111-1111-1111-1111-111111111111");

    internal static int Run(string[] args)
    {
        try
        {
            if (args is not ["serve-projection-write", var file]) return 2;
            var path = Path.GetFullPath(file);
            if (!Path.GetExtension(path).Equals(".DLDB", StringComparison.OrdinalIgnoreCase)) return 2;
            var root = Path.ChangeExtension(path, null);
            using var frozen = Freeze(path);
            using var ledger = LedgerInstance.Open(root);
            if (ledger.Manifest.Binding is null) return 4;
            string name = "uddl-step2a-" + Guid.NewGuid().ToString("N");
            // Duplex, overlapped, first instance; byte mode, reject remote clients.
            using var handle = CreateNamedPipeW(@"\\.\pipe\" + name, 0x40080003, 8, 1, 256, 256, 0, IntPtr.Zero);
            if (handle.IsInvalid) throw new System.ComponentModel.Win32Exception();
            using var server = new NamedPipeServerStream(PipeDirection.InOut, true, false, handle);
            foreach (var scenario in new[] { Scenario.Enrolled, Scenario.Unenrolled, Scenario.DuplicateSeed, Scenario.LostReply })
            {
                ServeChild(server, name, ledger, scenario, path);
                if (server.IsConnected) server.Disconnect();
            }
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Step2a stopped: " + e.GetType().Name + " " + e.Message);
            return 6;
        }
    }

    // Hold the clean fixture stable across both children. This slice accepts a
    // local rollback-format database of at most 1 MiB with no recovery sidecars.
    private static FileStream Freeze(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\") ||
            new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Fixed)
            throw new IOException("Expected local fixed-volume projection");
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            Span<byte> header = stackalloc byte[20];
            file.ReadExactly(header);
            if (file.Length > 1048576 || !header[..16].SequenceEqual("UDDL database 1\0"u8) ||
                header[18] != 1 || header[19] != 1) throw new IOException("Unsupported projection");
            var names = Directory.GetFileSystemEntries(Path.GetDirectoryName(path)!);
            foreach (var suffix in new[] { "-journal", "-wal", "-shm" })
                if (names.Contains(path + suffix, StringComparer.OrdinalIgnoreCase))
                    throw new IOException("Projection has a sidecar");
            return file;
        }
        catch { file.Dispose(); throw; }
    }

    private enum Scenario { Enrolled, Unenrolled, DuplicateSeed, LostReply }

    private static void ServeChild(NamedPipeServerStream pipe, string name, LedgerInstance ledger, Scenario scenario, string path)
    {
        var instance = ledger.Manifest.InstanceId;
        var block = Guid.NewGuid();
        var enroll = scenario != Scenario.Unenrolled;
        var row = scenario == Scenario.DuplicateSeed ? new Guid("01000000-0000-0000-0000-000000000000") : Guid.NewGuid();
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "publication-client.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(path);
        start.ArgumentList.Add(row.ToString("N"));
        start.Environment["UDDL_PROJECTION"] = path;
        start.Environment["UDDL_SERVER_PID"] = Environment.ProcessId.ToString();
        start.Environment["UDDL_LOCAL_PIPE"] = name;
        start.Environment["UDDL_INSTANCE"] = instance.ToString("N");
        start.Environment["UDDL_EVENT_ID"] = block.ToString("N");
        Console.WriteLine($"LAUNCH_EXPECTED scenario={scenario} instance={instance} enrolled={enroll} block={block} table={Table} row={row}");
        using var child = Process.Start(start) ?? throw new IOException("Producer launch failed");
        try
        {
            _ = child.SafeHandle;
            if (scenario == Scenario.DuplicateSeed)
            {
                if (!child.WaitForExit(WaitMs) || child.ExitCode != 5) throw new IOException("Expected seed constraint");
                Console.Write(child.StandardOutput.ReadToEnd());
                return;
            }
            using var deadline = new CancellationTokenSource(WaitMs);
            var token = deadline.Token;
            pipe.WaitForConnectionAsync(token).GetAwaiter().GetResult();
            var request = new byte[66]; // instance, stable block id, fixed-size INSERT
            pipe.ReadExactlyAsync(request, token).AsTask().GetAwaiter().GetResult();
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint pid))
                throw new System.ComponentModel.Win32Exception();
            using var host = WindowsIdentity.GetCurrent();
            SecurityIdentifier? peer = null;
            pipe.RunAsClient(() => { using var id = WindowsIdentity.GetCurrent(true); peer = id?.User; });
            bool sameLaunch = pid == child.Id && !child.HasExited && peer != null && host.User is {} hostSid && peer.Equals(hostSid);
            byte result = Unenrolled;
            byte[] body = request[32..];
            if (enroll && sameLaunch && new Guid(request.AsSpan(0,16), true) == instance &&
                new Guid(request.AsSpan(16,16), true) == block && body[0] == 1 && body[33] == 0 &&
                new Guid(body.AsSpan(1,16),true) == Table && new Guid(body.AsSpan(17,16),true) == row)
            {
                result = Uncertain;
                try
                {
                    var publication = ledger.Publish(body, block);
                    if (publication.Block.Id == block && publication.Block.EventBytes.AsSpan().SequenceEqual(body)
                        && publication.Authorization == "authorized" && publication.Status == "retained")
                    {
                        Console.WriteLine($"PUBLISH_RETURNED pid={pid} instance={instance} block={block} event={Convert.ToHexString(body)} hash={publication.Block.Hash}");
                        result = Published;
                    }
                }
                catch (LedgerException error) { Console.Error.WriteLine(error.Code); }
            }
            Console.WriteLine($"REPLY pid={pid} enrolled={enroll} sameLaunch={sameLaunch} block={block} event={Convert.ToHexString(body)} status={result}");
            if (scenario == Scenario.LostReply)
            {
                if (result != Published) throw new IOException("Expected publication before lost reply");
                pipe.Disconnect();
            }
            else
            {
                var response = new byte[67];
                response[0] = result;
                request.CopyTo(response, 1);
                pipe.WriteAsync(response, token).AsTask().GetAwaiter().GetResult();
            }
            int expected = scenario == Scenario.LostReply ? 7 : enroll ? 0 : 4;
            if (!child.WaitForExit(WaitMs) || !sameLaunch || child.ExitCode != expected)
                throw new IOException("Unexpected producer result");
            Console.Write(child.StandardOutput.ReadToEnd());
        }
        finally
        {
            if (!child.HasExited) { child.Kill(); if (!child.WaitForExit(WaitMs)) throw new IOException("Child teardown timeout"); }
            Console.WriteLine($"PRODUCER_EXIT pid={child.Id} exit={child.ExitCode}");
        }
    }

}
