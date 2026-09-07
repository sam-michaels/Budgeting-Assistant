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

        // Weight by MARGIN over the floor, squared, rather than raw similarity.
        //
        // Raw similarity sums let a crowded category win on volume: five Housing examples
        // containing "PMT" at ~0.65 each outvoted a verbatim 1.00 match on "GEICO AUTO PMT",
        // because 5 x 0.65 > 1.00. Since every retrieved neighbour already clears the floor,
        // the informative quantity is how far past it each one reaches -- squaring that
        // margin makes a near-exact match dominate a crowd of marginal ones.
        var byCategory = considered
            .GroupBy(n => n.CategoryId)
            .Select(g => (CategoryId: g.Key,
                          Weight: g.Sum(n => Margin(n.Similarity, floor)),
                          Best: g.Max(n => n.Similarity)))
            .OrderByDescending(x => x.Weight)
            .ThenByDescending(x => x.Best)
            .ToList();

        var total = byCategory.Sum(x => x.Weight);
        var winner = byCategory[0];
        var matchedOn = considered.First(n => n.CategoryId == winner.CategoryId).Label;

        // Confidence combines two independent things, because either alone lies.
        //
        // Consensus alone ("all my neighbours agreed") reported 0.97 for ZUNI CAFE ->
        // Coffee: every neighbour agreed, but the nearest was only 0.72 away, and the
        // real answer was Restaurants. Scaling consensus by how close the best match
        // actually was makes the number mean "how sure, given what I have seen before".
        var confidence = (winner.Weight / total) * winner.Best;

        return new CategorySuggestion(winner.CategoryId, confidence, matchedOn);
    }

    static float Margin(float similarity, float floor)
    {
        var m = similarity - floor;
        return m * m;
    }
}
