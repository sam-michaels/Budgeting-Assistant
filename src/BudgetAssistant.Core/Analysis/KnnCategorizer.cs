namespace BudgetAssistant.Core.Analysis;

/// <summary>One scored neighbour returned by the vector store.</summary>
public readonly record struct Neighbor(int CategoryId, string Label, float Similarity);

/// <summary>The classifier's answer, including what it matched so the UI can show its work.</summary>
public readonly record struct CategorySuggestion(int CategoryId, float Confidence, string MatchedOn);

/// <summary>
/// Similarity-weighted k-NN vote over neighbours already retrieved from the vector store.
///
/// Retrieval lives in the data layer; this stays a pure function so it tests without a
/// database, an embedding model, or a mock.
/// </summary>
public static class KnnCategorizer
{
    public const int DefaultK = 5;

    /// <returns>null when no neighbour clears <see cref="SimilarityBands.CategoryFloor"/> —
    /// leaving a transaction uncategorized is correct behaviour, not a failure. A wrong
    /// category silently corrupts a budget; a blank one asks the user.</returns>
    public static CategorySuggestion? Suggest(
        IReadOnlyList<Neighbor> neighbors,
        int k = DefaultK,
        float floor = SimilarityBands.CategoryFloor)
    {
        var considered = neighbors
            .Where(n => n.Similarity >= floor)
            .OrderByDescending(n => n.Similarity)
            .Take(k)
            .ToList();

        if (considered.Count == 0) return null;

        // Weight by similarity: a 0.86 match should outvote two 0.56 matches.
        var byCategory = considered
            .GroupBy(n => n.CategoryId)
            .Select(g => (CategoryId: g.Key, Weight: g.Sum(n => n.Similarity), Best: g.Max(n => n.Similarity)))
            .OrderByDescending(x => x.Weight)
            .ThenByDescending(x => x.Best)
            .ToList();

        var total = byCategory.Sum(x => x.Weight);
        var winner = byCategory[0];
        var matchedOn = considered.First(n => n.CategoryId == winner.CategoryId).Label;

        return new CategorySuggestion(winner.CategoryId, winner.Weight / total, matchedOn);
    }
}
