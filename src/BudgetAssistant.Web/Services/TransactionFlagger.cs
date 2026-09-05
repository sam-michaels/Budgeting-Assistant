using BudgetAssistant.Core.Analysis;
using BudgetAssistant.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BudgetAssistant.Web.Services;

/// <summary>
/// Marks duplicate charges and recurring subscriptions across a user's whole history.
///
/// Both detectors compare a transaction against its neighbours, so neither can run on a
/// single row in isolation the way categorization can — a charge is only a duplicate
/// relative to another charge. That is why this is a separate pass after the save rather
/// than part of <see cref="TransactionCategorizer"/>, and why every write path that adds
/// transactions has to call it: seeding, CSV import, and the create endpoint.
/// </summary>
public sealed class TransactionFlagger(ApplicationDbContext db, ILogger<TransactionFlagger> log)
{
    /// <summary>Recomputes duplicate and subscription flags for one user. Saves.</summary>
    // ponytail: re-scans the user's whole history on every call, and DuplicateDetector is
    // O(n²). Fine into five figures of transactions. Window to the last 90 days if that
    // stops holding.
    public async Task FlagAsync(string userId, CancellationToken ct = default)
    {
        var txns = await db.Transactions
            .Where(t => t.Account!.UserId == userId && t.Embedding != null)
            .ToListAsync(ct);

        var vectors = txns.Select(t => new TxnVector(t.Id, t.Amount, t.Date, t.Embedding!)).ToList();
        var byId = txns.ToDictionary(t => t.Id);

        foreach (var (dupId, originalId) in DuplicateDetector.Find(vectors))
            byId[dupId].DuplicateOfId = originalId;

        // Transfers are excluded: a standing monthly sweep into savings passes every
        // structural test for a subscription, and calling it one would be wrong on screen.
        var labelled = txns.Where(t => !t.IsTransfer)
            .Select(t => (new TxnVector(t.Id, t.Amount, t.Date, t.Embedding!), t.NormalizedMerchant)).ToList();
        var subs = SubscriptionDetector.Find(labelled);
        foreach (var id in subs.SelectMany(s => s.TransactionIds))
            byId[id].IsSubscription = true;

        await db.SaveChangesAsync(ct);
        log.LogInformation("Flagged {Dupes} duplicate charges and {Subs} subscriptions",
            txns.Count(t => t.DuplicateOfId is not null), subs.Count);
    }
}
