using BudgetAssistant.Core.Abstractions;
using BudgetAssistant.Core.Analysis;
using BudgetAssistant.Core.Entities;
using BudgetAssistant.Web.Data;
using BudgetAssistant.Web.Data.Seed;
using Microsoft.EntityFrameworkCore;

namespace BudgetAssistant.Web.Services;

/// <summary>
/// normalize -> transfer rule -> embed -> nearest labelled neighbours -> weighted vote.
/// Used by both seeding and CSV import so imported data is classified the same way.
/// </summary>
public sealed class TransactionCategorizer(IEmbedder embedder, VectorSearch search, ApplicationDbContext db)
{
    /// <summary>Fills in the normalized merchant, embedding and category fields. Does not save.</summary>
    public async Task ClassifyAsync(Transaction txn, string userId, CancellationToken ct = default)
    {
        txn.NormalizedMerchant = MerchantNormalizer.Normalize(txn.RawDescription);
        txn.Embedding = embedder.Embed(txn.NormalizedMerchant);

        // Decided before the vote, not after it. A transfer is the owner's own money
        // moving, so it is settled by a rule and kept out of income and spend entirely.
        if (TransferDetector.IsTransfer(txn.NormalizedMerchant))
        {
            txn.IsTransfer = true;
            txn.CategoryId = await TransferCategoryIdAsync(ct);
            txn.CategorySource = CategorySource.Rule;
            // No confidence and nothing matched: a rule has neither, and showing a
            // fabricated 100% next to a real k-NN score would make both meaningless.
            txn.CategoryConfidence = null;
            txn.CategoryMatchedOn = null;
            return;
        }

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

    int? transferCategoryId;

    async Task<int?> TransferCategoryIdAsync(CancellationToken ct) =>
        transferCategoryId ??= await db.Categories
            .Where(c => c.Name == SeedCorpus.TransferCategory)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(ct);
}
