namespace BudgetAssistant.Core.Analysis;

public readonly record struct TxnVector(int Id, decimal Amount, DateOnly Date, float[] Embedding);

/// <summary>
/// Flags probable double-charges: the same merchant, the same amount to the cent, close
/// together in time. All three must hold — two $4.75 coffees a week apart are just coffee.
/// </summary>
public static class DuplicateDetector
{
    public const int DefaultWindowDays = 3;

    /// <returns>(duplicate id, original id) pairs. The later transaction is the duplicate.</returns>
    // ponytail: O(n^2) over the window. Fine to ~10k transactions; if it ever matters,
    // bucket by (amount, week) first — the amount equality check is the cheap discriminator.
    public static List<(int DuplicateId, int OriginalId)> Find(
        IReadOnlyList<TxnVector> txns,
        int windowDays = DefaultWindowDays,
        float minSimilarity = SimilarityBands.SameMerchant)
    {
        var ordered = txns.OrderBy(t => t.Date).ThenBy(t => t.Id).ToList();
        var results = new List<(int, int)>();
        var alreadyFlagged = new HashSet<int>();

        for (var i = 0; i < ordered.Count; i++)
        for (var j = i + 1; j < ordered.Count; j++)
        {
            var (a, b) = (ordered[i], ordered[j]);
            if (b.Date.DayNumber - a.Date.DayNumber > windowDays) break; // ordered by date
            if (alreadyFlagged.Contains(b.Id)) continue;
            if (a.Amount != b.Amount) continue;
            if (Cosine.Between(a.Embedding, b.Embedding) < minSimilarity) continue;

            results.Add((b.Id, a.Id));
            alreadyFlagged.Add(b.Id);
        }
        return results;
    }
}
