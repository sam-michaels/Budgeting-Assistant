namespace BudgetAssistant.Core.Analysis;

public readonly record struct Subscription(IReadOnlyList<int> TransactionIds, decimal Amount, int MedianGapDays, string Label);

/// <summary>
/// Finds recurring monthly charges by clustering on merchant similarity rather than exact
/// string equality — which is the point of having embeddings at all.
///
/// Subscription references rotate ("SPOTIFY P0A1B2C3" then "SPOTIFY AB99XY12"), so exact
/// grouping sees two unrelated merchants and finds nothing. Those two strings measure 0.826
/// similar, comfortably inside the same-merchant band, so vector clustering catches what
/// string matching cannot.
/// </summary>
public static class SubscriptionDetector
{
    public const int MinOccurrences = 3;
    public const int MinGapDays = 26;
    public const int MaxGapDays = 35;

    public static List<Subscription> Find(
        IReadOnlyList<(TxnVector Txn, string Label)> txns,
        float minSimilarity = SimilarityBands.SameMerchant)
    {
        // Outflows only. A recurring credit — payroll, a standing transfer into savings —
        // is regular and same-amount, so it matches every structural test here, but money
        // arriving is not a subscription.
        var unassigned = txns.Where(t => t.Txn.Amount < 0).OrderBy(t => t.Txn.Date).ToList();
        var found = new List<Subscription>();
        var used = new HashSet<int>();

        foreach (var seed in unassigned)
        {
            if (used.Contains(seed.Txn.Id)) continue;

            // Cluster = same merchant AND same amount. A subscription whose price changes
            // mid-stream shows up as two runs, which is honest rather than wrong.
            var cluster = unassigned
                .Where(t => !used.Contains(t.Txn.Id)
                            && t.Txn.Amount == seed.Txn.Amount
                            && Cosine.Between(seed.Txn.Embedding, t.Txn.Embedding) >= minSimilarity)
                .OrderBy(t => t.Txn.Date)
                .ToList();

            if (cluster.Count < MinOccurrences) continue;

            var gaps = cluster.Zip(cluster.Skip(1), (a, b) => b.Txn.Date.DayNumber - a.Txn.Date.DayNumber).ToList();
            var monthly = gaps.Where(g => g is >= MinGapDays and <= MaxGapDays).ToList();

            // Most gaps must look monthly; a couple of irregular ones are tolerated.
            if (monthly.Count < gaps.Count - 1 || monthly.Count == 0) continue;

            foreach (var t in cluster) used.Add(t.Txn.Id);
            found.Add(new Subscription(
                cluster.Select(t => t.Txn.Id).ToList(),
                seed.Txn.Amount,
                monthly.OrderBy(g => g).ElementAt(monthly.Count / 2),
                seed.Label));
        }
        return found;
    }
}
