using System.Security.Cryptography;
using UserDimensions.Ledger;

// This fixture runs once per enrolled native submission in a disposable ledger.
// Its scratch storage and journal credential belong to the running test host.
internal static class FixtureStoredPublication
{
    internal enum Result { Refused, Uncertain, Published }

    internal static Result Run(string scratch, LedgerInstance ledger, Guid row, Guid block, byte[] body,
        uint producerPid, bool failBeforeReady, byte[]? serviceKey)
    {
        var files = new DurableFiles(point =>
        {
            if (failBeforeReady && point == "before-write:ready")
            {
                Console.WriteLine($"STORED_READY_FAULT block={block}");
                throw new IOException("Injected fixture readiness write failure");
            }
        });
        // The enrolled submission already binds its instance, block GUID and row.
        // Epoch 0 is local to this fresh fixture; it is never treated as signing authority.
        var intent = FrozenIntent.Capture(new(block, row, ledger.Manifest, 0), [new(block, body)]);
        bool publishStarted = false;
        Span<byte> journalKey = stackalloc byte[32];
        if (serviceKey is null) RandomNumberGenerator.Fill(journalKey);
        else serviceKey.AsSpan().CopyTo(journalKey);
        try
        {
            var scratchPath = Paths.Absolute(scratch);
            if (!Directory.Exists(scratchPath)) return Result.Refused;
            var intentRoot = Path.Combine(scratchPath, "intents");
            var journalRoot = Path.Combine(scratchPath, "journal");
            files.Directory(intentRoot);
            var store = new FrozenIntentStore(intentRoot, files);
            var readiness = store.Stage(intent);
            if (readiness != FrozenReadiness.WriteCompleted || store.Observe(intent) != FrozenReadiness.ObservedExact)
            {
                Console.WriteLine($"STORED_INTENT_REFUSED request={intent.Request} readiness={readiness}");
                return Result.Refused;
            }

            var payloadPath = Path.Combine(intentRoot, intent.Request.ToString("N"), "payload");
            var storedBody = Paths.ReadBounded(payloadPath, body.Length);
            if (!storedBody.AsSpan().SequenceEqual(body)) return Result.Refused;
            var bodyDigest = Convert.ToHexString(SHA256.HashData(storedBody));
            Console.WriteLine($"STORED_INTENT_READY request={intent.Request} bodySha256={bodyDigest}");

            files.Directory(journalRoot);
            using var journal = new IntentProgressJournal(journalRoot, intent, journalKey, files);
            if (journal.Initialize() != ProgressWrite.WriteCompleted) return Result.Refused;
            var opening = journal.Read();
            if (opening is not { State: ProgressState.ObservedExact, MatchedHead: { Sequence: 0 } initial,
                    VerifiedRecords.Count: 0, Ahead: null }) return Result.Refused;
            Console.WriteLine($"STORED_GATE block={block} sequence={initial.Sequence} headSha256={initial.HeadDigest}");

            publishStarted = true;
            var publication = ledger.Publish(storedBody, block);
            var durable = ledger.SelectExport().Blocks.SingleOrDefault(item => item.Id == block);
            if (publication.Authorization != "authorized" || publication.Status != "retained" ||
                publication.Block.Id != block || durable is null || durable.Hash != publication.Block.Hash ||
                !durable.EventBytes.AsSpan().SequenceEqual(storedBody)) return Result.Uncertain;
            Console.WriteLine($"PUBLISH_RETURNED pid={producerPid} instance={ledger.Manifest.InstanceId} block={block} event={Convert.ToHexString(storedBody)} hash={durable.Hash}");

            var record = Guid.NewGuid();
            if (journal.Append(initial, record, 0) != ProgressWrite.WriteCompleted) return Result.Uncertain;
            var complete = journal.Read();
            if (complete is not { State: ProgressState.ObservedExact, MatchedHead: { Sequence: 1 } final,
                    VerifiedRecords.Count: 1, Ahead: null } ||
                complete.VerifiedRecords[0] != new ProgressEntry(record, 0, block, bodyDigest)) return Result.Uncertain;
            Console.WriteLine($"STORED_PROGRESS block={block} record={record} sequence={final.Sequence} headSha256={final.HeadDigest}");
            return Result.Published;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or LedgerException)
        {
            Console.Error.WriteLine($"Stored-COMMIT fixture stopped: {error.GetType().Name}");
            return publishStarted ? Result.Uncertain : Result.Refused;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(journalKey);
        }
    }
}
