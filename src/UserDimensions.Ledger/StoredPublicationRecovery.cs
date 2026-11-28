using System.Security.Cryptography;

namespace UserDimensions.Ledger;

internal static class StoredPublicationRecovery
{
    // The service supplies the original request and the journal credential. A restart
    // can recognize an authenticated completed event, but cannot publish or repair it.
    internal static bool Recognize(string storage, LedgerInstance ledger, Guid row, Guid block,
        byte[] body, ReadOnlySpan<byte> journalKey, DurableFiles files)
    {
        try
        {
            var intent = FrozenIntent.Capture(new(block, row, ledger.Manifest, 0), [new(block, body)]);
            var root = Paths.Absolute(storage);
            var store = new FrozenIntentStore(Path.Combine(root, "intents"), files);
            if (store.Observe(intent) != FrozenReadiness.ObservedExact) return false;
            using var journal = new IntentProgressJournal(Path.Combine(root, "journal"), intent, journalKey, files);
            if (journal.Read() is not { State: ProgressState.ObservedExact, MatchedHead.Sequence: 1,
                    Ahead: null, VerifiedRecords: [var record] }) return false;
            return record.EventPosition == 0 && record.BlockId == block &&
                record.BodyDigest == Convert.ToHexString(SHA256.HashData(body)) &&
                ledger.RecognizePublication(block, body) is not null;
        }
        catch (Exception e) when (e is LedgerException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
