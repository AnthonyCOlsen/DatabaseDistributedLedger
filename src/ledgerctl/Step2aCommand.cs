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
    private const byte Published = 1, Rejected = 2, Uncertain = 3;
    private static readonly Guid NotesId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Table = new("11111111-1111-1111-1111-111111111111");

#if UDDL_STORED_COMMIT_FIXTURE
    internal static int Run(string[] args, string fixtureRoot, byte[]? journalKey = null)
#else
    internal static int Run(string[] args)
#endif
    {
        try
        {
            if (args.Length is < 2 or > 3 || args[0] != "serve-projection-write") return 2;
            var file = args[1];
            Scenario[] scenarios = args.Length == 2
                ? [Scenario.Enrolled, Scenario.Unenrolled, Scenario.DuplicateSeed, Scenario.LostReply]
                : args[2] switch { "--rollback" => [Scenario.Rollback], "--commit-control" => [Scenario.Enrolled],
                    "--discard-at-savepoint" => [Scenario.Discard], "--retain-at-savepoint" => [Scenario.Retain],
                    "--outer-release" => [Scenario.Outer], "--outer-denied" => [Scenario.OuterDenied],
                    "--create-notes" => [Scenario.SchemaRollback, Scenario.SchemaUnsupported, Scenario.SchemaMixed, Scenario.SchemaCreate],
                    "--add-tag" => [Scenario.SchemaDuplicate, Scenario.SchemaAdd],
                    "--drop-notes" => [Scenario.SchemaDrop],
                    "--effective-dml" => [Scenario.Unchanged, Scenario.Missing, Scenario.UpdateRollback, Scenario.Update, Scenario.Delete],
#if UDDL_STORED_COMMIT_FIXTURE
                    "--stored-commit" => [Scenario.StoredCommit],
                    "--stored-refused" => [Scenario.StoredRefused],
                    "--stored-lost" => [Scenario.StoredLost],
#endif
                    "--outer-lost" => [Scenario.OuterLost], _ => [] };
            if (scenarios.Length == 0) return 2;
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
            foreach (var scenario in scenarios)
            {
#if UDDL_STORED_COMMIT_FIXTURE
                ServeChild(server, name, ledger, scenario, path, fixtureRoot, journalKey);
#else
                ServeChild(server, name, ledger, scenario, path);
#endif
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

    private enum Scenario { Enrolled, Unenrolled, DuplicateSeed, LostReply, Rollback, Discard, Retain, Outer, OuterDenied, OuterLost, Update, Delete, Unchanged, Missing, UpdateRollback,
        SchemaCreate, SchemaAdd, SchemaDrop, SchemaDuplicate, SchemaUnsupported, SchemaRollback, SchemaMixed,
#if UDDL_STORED_COMMIT_FIXTURE
        StoredCommit, StoredRefused, StoredLost,
#endif
    }

#if UDDL_STORED_COMMIT_FIXTURE
    private static void ServeChild(NamedPipeServerStream pipe, string name, LedgerInstance ledger, Scenario scenario,
        string path, string fixtureRoot, byte[]? journalKey)
#else
    private static void ServeChild(NamedPipeServerStream pipe, string name, LedgerInstance ledger, Scenario scenario, string path)
#endif
    {
        var instance = ledger.Manifest.InstanceId;
        var block = Guid.NewGuid();
        bool outer = scenario is Scenario.Outer or Scenario.OuterDenied or Scenario.OuterLost;
        bool lost = scenario is Scenario.LostReply or Scenario.OuterLost;
#if UDDL_STORED_COMMIT_FIXTURE
        bool stored = scenario is Scenario.StoredCommit or Scenario.StoredRefused or Scenario.StoredLost;
        lost |= scenario == Scenario.StoredLost;
#endif
        var enroll = scenario is not (Scenario.Unenrolled or Scenario.OuterDenied);
        bool update = scenario is Scenario.Update or Scenario.UpdateRollback or Scenario.Unchanged;
        bool schema = scenario is Scenario.SchemaCreate or Scenario.SchemaAdd or Scenario.SchemaDrop;
        bool silent = scenario is Scenario.SchemaMixed or Scenario.SchemaDuplicate or Scenario.SchemaUnsupported or Scenario.SchemaRollback or Scenario.Rollback or Scenario.Unchanged or Scenario.Missing or Scenario.UpdateRollback;
        var row = update || scenario == Scenario.DuplicateSeed ? new Guid("01000000-0000-0000-0000-000000000000") :
            scenario == Scenario.Delete ? new Guid("02000000-0000-0000-0000-000000000000") : Guid.NewGuid();
        bool savepoint = outer || scenario is Scenario.Discard or Scenario.Retain;
        bool barrier = savepoint || schema;
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "publication-client.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardInput = barrier };
        start.ArgumentList.Add(path);
        start.ArgumentList.Add(row.ToString("N"));
        if (update || scenario is Scenario.Delete or Scenario.Missing)
            start.ArgumentList.Add(scenario == Scenario.UpdateRollback ? "--update-rollback" : "--" + scenario.ToString().ToLowerInvariant());
        if (schema || scenario is Scenario.SchemaMixed or Scenario.SchemaRollback or Scenario.SchemaDuplicate or Scenario.SchemaUnsupported)
            start.ArgumentList.Add("--" + scenario.ToString().ToLowerInvariant());
        if (scenario == Scenario.LostReply) start.ArgumentList.Add("--lost-reply");
#if UDDL_STORED_COMMIT_FIXTURE
        if (scenario == Scenario.StoredLost) start.ArgumentList.Add("--lost-reply");
#endif
        if (scenario == Scenario.Rollback) start.ArgumentList.Add("--rollback");
        if (savepoint) start.ArgumentList.Add(outer ? "--outer-release" : scenario == Scenario.Discard ? "--discard-at-savepoint" : "--retain-at-savepoint");
        start.Environment["UDDL_PROJECTION"] = path;
        start.Environment["UDDL_SERVER_PID"] = Environment.ProcessId.ToString();
        start.Environment["UDDL_LOCAL_PIPE"] = name;
        start.Environment["UDDL_INSTANCE"] = instance.ToString("N");
        start.Environment["UDDL_EVENT_ID"] = block.ToString("N");
        Console.WriteLine($"LAUNCH_EXPECTED scenario={scenario} instance={instance} enrolled={enroll} block={block} table={(scenario.ToString().StartsWith("Schema", StringComparison.Ordinal) ? NotesId : Table)} row={row}");
        // Arm the single local pipe before launch. Any connection during rollback,
        // even without a complete or admissible request, fails the observation.
        using var observation = new CancellationTokenSource();
        var connection = silent || barrier ? pipe.WaitForConnectionAsync(observation.Token) : null;
        using var child = Process.Start(start) ?? throw new IOException("Producer launch failed");
        try
        {
            _ = child.SafeHandle;
            if (silent && connection is not null)
            {
                if (!child.WaitForExit(WaitMs)) throw new IOException($"{scenario} client timed out");
                Console.Write(child.StandardOutput.ReadToEnd());
                observation.Cancel();
                try
                {
                    connection.GetAwaiter().GetResult();
                    throw new IOException($"Unexpected {scenario} endpoint connection");
                }
                catch (OperationCanceledException) when (connection.IsCanceled) { }
                if (child.ExitCode != 0) throw new IOException($"{scenario} client failed");
                Console.WriteLine($"{(scenario == Scenario.Rollback ? "ROLLBACK_OBSERVED" : "NO_SUBMISSION")} scenario={scenario} pid={child.Id} connections=0 requests=0");
                return;
            }
            if (scenario == Scenario.DuplicateSeed)
            {
                if (!child.WaitForExit(WaitMs) || child.ExitCode != 5) throw new IOException("Expected seed constraint");
                Console.Write(child.StandardOutput.ReadToEnd());
                return;
            }
            using var deadline = new CancellationTokenSource(WaitMs);
            var token = deadline.Token;
            if (barrier)
            {
                // The child cannot enter COMMIT until the host releases stdin.
                string? line;
                do
                {
                    line = child.StandardOutput.ReadLineAsync(token).AsTask().GetAwaiter().GetResult();
                    if (line is null) throw new IOException("Client exited before COMMIT barrier");
                    Console.WriteLine(line);
                } while (line != (outer ? "READY_FOR_OUTER_RELEASE" : "READY_FOR_OUTER_COMMIT"));
                observation.Cancel();
                try
                {
                    connection!.GetAwaiter().GetResult();
                    throw new IOException("Unexpected submission before outer COMMIT");
                }
                catch (OperationCanceledException) when (connection!.IsCanceled) { }
                if ((outer || schema) && ledger.SelectExport().Blocks.Any(record => record.Id == block))
                    throw new IOException("Block already retained before transaction boundary");
                Console.WriteLine(outer ? "BEFORE_OUTER_RELEASE connections=0 requests=0 block=absent" :
                    "BEFORE_OUTER_COMMIT connections=0 requests=0");
            }
            var accept = pipe.WaitForConnectionAsync(token);
            if (barrier) { child.StandardInput.WriteLine("G"); child.StandardInput.Flush(); }
            accept.GetAwaiter().GetResult();
            var expectedBody = EventCodec.Encode(scenario switch
            {
                Scenario.SchemaCreate => new LedgerEvent(0x10, NotesId, Name: "notes",
                    Columns: [new ColumnDefinition(new Guid("44444444-4444-4444-4444-444444444444"), "v", "BLOB", true, null)]),
                Scenario.SchemaAdd => new LedgerEvent(0x13, NotesId,
                    Columns: [new ColumnDefinition(new Guid("55555555-5555-5555-5555-555555555555"), "tag", "TEXT", true, null)]),
                Scenario.SchemaDrop => new LedgerEvent(0x11, NotesId),
                Scenario.Update => new LedgerEvent(2, Table, row, Fields: [new RowField(new Guid("22222222-2222-2222-2222-222222222222"), LedgerValue.Blob([0, 255]))]),
                Scenario.Delete => new LedgerEvent(3, Table, row),
                _ => new LedgerEvent(1, Table, row)
            });
            var request = new byte[32 + expectedBody.Length];
            pipe.ReadExactlyAsync(request, token).AsTask().GetAwaiter().GetResult();
            Console.WriteLine($"SUBMISSION_RECEIVED scenario={scenario} block={new Guid(request.AsSpan(16,16),true)}");
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint pid))
                throw new System.ComponentModel.Win32Exception();
            using var host = WindowsIdentity.GetCurrent();
            SecurityIdentifier? peer = null;
            pipe.RunAsClient(() => { using var id = WindowsIdentity.GetCurrent(true); peer = id?.User; });
            bool sameLaunch = pid == child.Id && !child.HasExited && peer != null && host.User is {} hostSid && peer.Equals(hostSid);
            byte result = Rejected;
            byte[] body = request[32..];
            if (enroll && sameLaunch && new Guid(request.AsSpan(0,16), true) == instance &&
                new Guid(request.AsSpan(16,16), true) == block && body.AsSpan().SequenceEqual(expectedBody))
            {
                result = Uncertain;
                try
                {
                    // Only a verified, genesis-only history proves the initial replica case here.
                    // Absence of a local key from incomplete history is not a definitive refusal.
                    ledger.EnsureConfig();
                    var history = ledger.Snapshot();
                    var binding = ledger.Manifest.Binding!;
                    if (history.Blocks.Count == 1 && history.Blocks[0] is { Status: "retained", Authorization: "authorized" } initial &&
                        initial.Block.IsGenesis && initial.Block.LedgerId == binding.LedgerId &&
                        initial.Block.Hash == binding.GenesisHash && !history.AuthorizedKeys.Contains(ledger.PublicKey))
                    {
                        Console.WriteLine($"INITIAL_REPLICA_REFUSED instance={instance} block={block} genesis={binding.GenesisHash}");
                        result = Rejected;
                    }
                    else if (history.AuthorizedKeys.Contains(ledger.PublicKey))
                    {
#if UDDL_STORED_COMMIT_FIXTURE
                        if (stored)
                        {
                            result = FixtureStoredPublication.Run(fixtureRoot, ledger, row, block, body, pid,
                                scenario == Scenario.StoredRefused, journalKey) switch
                            {
                                FixtureStoredPublication.Result.Published => Published,
                                FixtureStoredPublication.Result.Refused => Rejected,
                                _ => Uncertain
                            };
                        }
                        else
#endif
                        {
                            var publication = ledger.Publish(body, block);
                            if (publication.Block.Id == block && publication.Block.EventBytes.AsSpan().SequenceEqual(body)
                                && publication.Authorization == "authorized" && publication.Status == "retained")
                            {
                                Console.WriteLine($"PUBLISH_RETURNED pid={pid} instance={instance} block={block} event={Convert.ToHexString(body)} hash={publication.Block.Hash}");
                                result = Published;
                            }
                        }
                    }
                }
                catch (LedgerException error) { Console.Error.WriteLine(error.Code); }
            }
#if UDDL_STORED_COMMIT_FIXTURE
            if (scenario != Scenario.StoredLost)
#endif
                Console.WriteLine($"REPLY pid={pid} enrolled={enroll} sameLaunch={sameLaunch} block={block} event={Convert.ToHexString(body)} status={result}");
            if (lost)
            {
                if (result != Published) throw new IOException("Expected publication before lost reply");
                pipe.Disconnect();
#if UDDL_STORED_COMMIT_FIXTURE
                if (scenario == Scenario.StoredLost)
                {
                    using var retryObservation = new CancellationTokenSource();
                    var retry = pipe.WaitForConnectionAsync(retryObservation.Token);
                    Console.WriteLine($"STORED_REPLY_DROPPED block={block}");
                    if (!child.WaitForExit(WaitMs)) throw new IOException("Stored lost-reply client timed out");
                    retryObservation.Cancel();
                    try
                    {
                        retry.GetAwaiter().GetResult();
                        throw new IOException("Unexpected reconnection after dropped reply");
                    }
                    catch (OperationCanceledException) when (retry.IsCanceled) { }
                    Console.WriteLine("STORED_NO_RESUBMISSION connections=1 requests=1");
                }
#endif
            }
            else
            {
                var response = new byte[1 + request.Length];
                response[0] = result;
                request.CopyTo(response, 1);
                pipe.WriteAsync(response, token).AsTask().GetAwaiter().GetResult();
            }
            int expected = lost ? 7 : result == Rejected ? 4 : 0;
            if (!child.WaitForExit(WaitMs) || !sameLaunch || child.ExitCode != expected)
                throw new IOException("Unexpected producer result");
            Console.Write(child.StandardOutput.ReadToEnd());
#if UDDL_STORED_COMMIT_FIXTURE
            if (stored && result != (scenario == Scenario.StoredRefused ? Rejected : Published))
                throw new IOException("Unexpected stored-COMMIT publication outcome");
#endif
        }
        finally
        {
            if (!child.HasExited) { child.Kill(); if (!child.WaitForExit(WaitMs)) throw new IOException("Child teardown timeout"); }
            Console.WriteLine($"PRODUCER_EXIT pid={child.Id} exit={child.ExitCode}");
        }
    }

}
