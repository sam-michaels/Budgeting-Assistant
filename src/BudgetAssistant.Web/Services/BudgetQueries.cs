using BudgetAssistant.Core.Analysis;
using BudgetAssistant.Core.Entities;
using BudgetAssistant.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BudgetAssistant.Web.Services;

/// <summary>A transaction as the UI and API see it. Deliberately omits Embedding —
/// serializing 384 floats per row would dwarf the payload and tell a caller nothing.</summary>
public record TransactionDto(
    int Id, DateOnly Date, string Description, decimal Amount, string Account,
    string? Category, string? CategoryColor, string CategorySource,
    float? Confidence, string? MatchedOn, bool IsDuplicate, bool IsSubscription);

public record AccountDto(int Id, string Name, string Type, decimal Balance, int TransactionCount);

/// <summary>Read paths shared by the Blazor components and the API controllers, so both
/// report the same numbers.</summary>
public sealed class BudgetQueries(ApplicationDbContext db)
{
    public IQueryable<Transaction> ForUser(string userId) =>
        db.Transactions.Where(t => t.Account!.UserId == userId);

    public async Task<List<AccountDto>> AccountsAsync(string userId, CancellationToken ct = default) =>
        await db.Accounts.Where(a => a.UserId == userId)
            .OrderBy(a => a.Type).ThenBy(a => a.Name)
            .Select(a => new AccountDto(a.Id, a.Name, a.Type.ToString(), a.Balance, a.Transactions.Count))
            .ToListAsync(ct);

    public async Task<List<TransactionDto>> TransactionsAsync(
        string userId, int take = 200, int skip = 0, string? search = null,
        bool onlyFlagged = false, CancellationToken ct = default)
    {
        var q = ForUser(userId);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(t => EF.Functions.ILike(t.RawDescription, $"%{search}%"));
        if (onlyFlagged)
            q = q.Where(t => t.DuplicateOfId != null || t.CategoryId == null);

        return await q.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Skip(skip).Take(take)
            .Select(t => new TransactionDto(
                t.Id, t.Date, t.RawDescription, t.Amount, t.Account!.Name,
                t.Category!.Name, t.Category.Color, t.CategorySource.ToString(),
                t.CategoryConfidence, t.CategoryMatchedOn,
                t.DuplicateOfId != null, t.IsSubscription))
            .ToListAsync(ct);
    }

    public async Task<MonthlySummary> SummaryAsync(string userId, DateOnly month, CancellationToken ct = default)
    {
        // Pull the month and the one before it, then let Core do the arithmetic.
        var from = new DateOnly(month.Year, month.Month, 1).AddMonths(-1);
        var to = new DateOnly(month.Year, month.Month, 1).AddMonths(1);

        var rows = await ForUser(userId)
            .Where(t => t.Date >= from && t.Date < to)
            .Select(t => new SummaryInput(
                t.Date, t.Amount, t.Category!.Name, t.Category.Color,
                t.IsSubscription, t.DuplicateOfId != null))
            .ToListAsync(ct);

        return SummaryBuilder.Build(rows, month);
    }

    public async Task<List<DateOnly>> AvailableMonthsAsync(string userId, CancellationToken ct = default)
    {
        var dates = await ForUser(userId).Select(t => t.Date).Distinct().ToListAsync(ct);
        return dates.Select(d => new DateOnly(d.Year, d.Month, 1)).Distinct()
                    .OrderByDescending(d => d).ToList();
    }
}
