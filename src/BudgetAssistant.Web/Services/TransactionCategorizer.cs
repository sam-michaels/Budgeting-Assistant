using BudgetAssistant.Core.Abstractions;
using BudgetAssistant.Core.Analysis;
using BudgetAssistant.Core.Entities;

namespace BudgetAssistant.Web.Services;

/// <summary>
/// normalize -> embed -> nearest labelled neighbours -> weighted vote.
/// Used by both seeding and CSV import so imported data is classified the same way.
/// </summary>
public sealed class TransactionCategorizer(IEmbedder embedder, VectorSearch search)
{
    /// <summary>Fills in the normalized merchant, embedding and category fields. Does not save.</summary>
    public async Task ClassifyAsync(Transaction txn, string userId, CancellationToken ct = default)
    {
        txn.NormalizedMerchant = MerchantNormalizer.Normalize(txn.RawDescription);
        txn.Embedding = embedder.Embed(txn.NormalizedMerchant);

        var neighbors = await search.FindLabeledNeighborsAsync(txn.Embedding, userId, ct: ct);

        if (KnnCategorizer.Suggest(neighbors) is { } s)
        {
            txn.CategoryId = s.CategoryId;
            txn.CategorySource = CategorySource.VectorKnn;
            txn.CategoryConfidence = s.Confidence;
            txn.CategoryMatchedOn = s.MatchedOn;
        }
        else
        {
            // Nothing cleared the floor. A wrong category silently corrupts a budget;
            // a blank one asks the user.
            txn.CategoryId = null;
            txn.CategorySource = CategorySource.Uncategorized;
            txn.CategoryConfidence = null;
            txn.CategoryMatchedOn = null;
        }
    }
}
