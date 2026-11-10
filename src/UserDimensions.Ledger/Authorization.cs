namespace UserDimensions.Ledger;

public sealed record BlockDisposition(SignedBlock Block, string Status, string Authorization, string Reason);
public sealed record AuthorizationSnapshot(IReadOnlyList<BlockDisposition> Blocks, IReadOnlySet<string> AuthorizedKeys);

public static class AuthorizationReplay
{
    public static AuthorizationSnapshot Evaluate(IEnumerable<SignedBlock> input, LedgerBinding? binding = null)
    {
        var result = new List<BlockDisposition>(); var finalAuthorized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ledger in input.DistinctBy(b => b.Hash).GroupBy(b => b.LedgerId))
        {
            var blocks = SignedBlock.Ordered(ledger).ToList();
            var disputes = blocks.GroupBy(b => b.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
            var geneses = blocks.Where(b => b.IsGenesis).ToList();
            var genesis = geneses.Count == 1 && !disputes.Contains(geneses[0].Id) ? geneses[0] : null;
            if (binding != null && (binding.LedgerId != ledger.Key || genesis?.Hash != binding.GenesisHash)) genesis = null;
            var authorized = new HashSet<string>(StringComparer.Ordinal); var everAuthorized = new HashSet<string>(StringComparer.Ordinal);
            var provisional = new List<(SignedBlock Block, bool Valid, string[] Required)>();
            foreach (var block in blocks)
            {
                var signer = Binary.Hex(block.Signer); var required = block.Event?.Kind == 0x21 ? new[] { signer, Binary.Hex(block.Event.Countersigner!) } : new[] { signer };
                var valid = !disputes.Contains(block.Id) && (block == genesis || (!block.IsGenesis && required.All(authorized.Contains)));
                provisional.Add((block, valid, required));
                if (!valid) continue;
                if (block == genesis) { authorized.Add(signer); everAuthorized.Add(signer); }
                else if (block.Event?.Kind == 0x20) { var key = Binary.Hex(block.Event.PublicKey!); authorized.Add(key); everAuthorized.Add(key); }
                else if (block.Event?.Kind == 0x21) authorized.Remove(Binary.Hex(block.Event.PublicKey!));
            }
            foreach (var (block, valid, required) in provisional)
            {
                var auth = valid ? "authorized" : required.All(everAuthorized.Contains) ? "unauthorized" : "pending";
                if (disputes.Contains(block.Id) || (block.IsGenesis && block != genesis)) result.Add(new(block,"quarantined",auth,"BLOCK_ID_DISPUTED"));
                else if (valid) result.Add(new(block,"retained","authorized","AUTHORIZED_RETAINED"));
                else if (auth == "pending") result.Add(new(block,"pending","pending","PENDING_AUTHORITY"));
                else result.Add(new(block,"quarantined","unauthorized","UNAUTHORIZED_AT_POSITION"));
            }
            if (binding == null || binding.LedgerId == ledger.Key) finalAuthorized.UnionWith(authorized);
        }
        return new(result,finalAuthorized);
    }
}
