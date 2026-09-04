namespace BudgetAssistant.Core.Analysis;

public readonly record struct CategoryTotal(string Category, string Color, decimal Amount, decimal PreviousAmount)
{
    public decimal Delta => Amount - PreviousAmount;
    /// <summary>Change vs the previous month, or null when there is no prior spend to
    /// compare against — a jump from nothing is not a percentage.</summary>
    public decimal? DeltaFraction => PreviousAmount == 0 ? null : (Amount - PreviousAmount) / PreviousAmount;
}

public readonly record struct SummaryInput(DateOnly Date, decimal Amount, string? Category, string? Color, bool IsSubscription, bool IsDuplicate);

public readonly record struct MonthlySummary(
    DateOnly Month,
    decimal Income,
    decimal Spending,
    IReadOnlyList<CategoryTotal> ByCategory,
    int DuplicateCount,
    decimal DuplicateAmount,
    decimal SubscriptionSpend)
{
    public decimal Net => Income - Spending;
}

/// <summary>
/// Aggregates a month of transactions and compares it with the month before.
/// Pure: takes rows, returns totals, touches nothing.
/// </summary>
public static class SummaryBuilder
{
    public static MonthlySummary Build(IReadOnlyList<SummaryInput> all, DateOnly month)
    {
        var start = new DateOnly(month.Year, month.Month, 1);
        var prevStart = start.AddMonths(-1);

        var current = all.Where(t => InMonth(t.Date, start)).ToList();
        var previous = all.Where(t => InMonth(t.Date, prevStart)).ToList();

        // Duplicates are excluded from spend totals: counting a double charge twice
        // overstates the budget, which is the opposite of what flagging it is for.
        var spend = current.Where(t => t.Amount < 0 && !t.IsDuplicate).ToList();
        var prevSpend = previous.Where(t => t.Amount < 0 && !t.IsDuplicate).ToList();

        var prevByCategory = prevSpend
            .Where(t => t.Category is not null)
            .GroupBy(t => t.Category!)
            .ToDictionary(g => g.Key, g => g.Sum(t => -t.Amount));

        var byCategory = spend
            .Where(t => t.Category is not null)
            .GroupBy(t => (t.Category!, t.Color ?? "#888888"))
            .Select(g => new CategoryTotal(g.Key.Item1, g.Key.Item2, g.Sum(t => -t.Amount),
                                           prevByCategory.GetValueOrDefault(g.Key.Item1)))
            .OrderByDescending(c => c.Amount)
            .ToList();

        var dupes = current.Where(t => t.IsDuplicate).ToList();

        return new MonthlySummary(
            Month: start,
            Income: current.Where(t => t.Amount > 0).Sum(t => t.Amount),
            Spending: spend.Sum(t => -t.Amount),
            ByCategory: byCategory,
            DuplicateCount: dupes.Count,
            DuplicateAmount: dupes.Sum(t => -t.Amount),
            SubscriptionSpend: spend.Where(t => t.IsSubscription).Sum(t => -t.Amount));
    }

    static bool InMonth(DateOnly d, DateOnly monthStart) => d.Year == monthStart.Year && d.Month == monthStart.Month;
}
