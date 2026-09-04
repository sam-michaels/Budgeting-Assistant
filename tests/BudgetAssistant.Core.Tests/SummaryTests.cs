using BudgetAssistant.Core.Analysis;

namespace BudgetAssistant.Core.Tests;

public class SummaryBuilderTests
{
    static DateOnly D(int y, int m, int d) => new(y, m, d);
    static SummaryInput Txn(DateOnly date, decimal amount, string? cat = null,
                            bool sub = false, bool dup = false)
        => new(date, amount, cat, "#888888", sub, dup);

    static readonly DateOnly March = D(2026, 3, 1);

    [Fact]
    public void SeparatesIncomeFromSpending()
    {
        var s = SummaryBuilder.Build([
            Txn(D(2026,3,15),  3120.55m),
            Txn(D(2026,3,16), -2150.00m, "Housing"),
            Txn(D(2026,3,17),   -42.10m, "Groceries"),
        ], March);

        Assert.Equal(3120.55m, s.Income);
        Assert.Equal(2192.10m, s.Spending);
        Assert.Equal(928.45m, s.Net);
    }

    [Fact]
    public void ExcludesDuplicatesFromSpend()
    {
        // A flagged double charge must not inflate the budget it is warning about.
        var s = SummaryBuilder.Build([
            Txn(D(2026,3,10), -50m, "Groceries"),
            Txn(D(2026,3,10), -50m, "Groceries", dup: true),
        ], March);

        Assert.Equal(50m, s.Spending);
        Assert.Equal(1, s.DuplicateCount);
        Assert.Equal(50m, s.DuplicateAmount);
    }

    [Fact]
    public void ComparesAgainstThePreviousMonth()
    {
        var s = SummaryBuilder.Build([
            Txn(D(2026,2,12), -100m, "Restaurants"),
            Txn(D(2026,3,12), -160m, "Restaurants"),
        ], March);

        var cat = Assert.Single(s.ByCategory);
        Assert.Equal(160m, cat.Amount);
        Assert.Equal(100m, cat.PreviousAmount);
        Assert.Equal(60m, cat.Delta);
        Assert.Equal(0.60m, cat.DeltaFraction);
    }

    [Fact]
    public void NewCategoryHasNoPercentageChange()
    {
        // Spending 80 dollars where you previously spent nothing is not "infinity percent".
        var s = SummaryBuilder.Build([Txn(D(2026,3,12), -80m, "Travel")], March);
        var cat = Assert.Single(s.ByCategory);
        Assert.Equal(80m, cat.Delta);
        Assert.Null(cat.DeltaFraction);
    }

    [Fact]
    public void OrdersCategoriesByLargestSpend()
    {
        var s = SummaryBuilder.Build([
            Txn(D(2026,3,2),  -20m, "Coffee"),
            Txn(D(2026,3,3), -900m, "Housing"),
            Txn(D(2026,3,4), -140m, "Groceries"),
        ], March);

        Assert.Equal(["Housing", "Groceries", "Coffee"], s.ByCategory.Select(c => c.Category));
    }

    [Fact]
    public void TotalsSubscriptionSpendSeparately()
    {
        var s = SummaryBuilder.Build([
            Txn(D(2026,3,4), -15.99m, "Streaming", sub: true),
            Txn(D(2026,3,7), -11.99m, "Streaming", sub: true),
            Txn(D(2026,3,9), -42.00m, "Groceries"),
        ], March);

        Assert.Equal(27.98m, s.SubscriptionSpend);
        Assert.Equal(69.98m, s.Spending);
    }

    [Fact]
    public void IgnoresOtherMonths()
    {
        var s = SummaryBuilder.Build([
            Txn(D(2026,2,28), -500m, "Housing"),
            Txn(D(2026,4,1),  -500m, "Housing"),
            Txn(D(2026,3,15),  -25m, "Coffee"),
        ], March);

        Assert.Equal(25m, s.Spending);
    }

    [Fact]
    public void UncategorizedSpendCountsInTheTotalButNotInAnyCategory()
    {
        var s = SummaryBuilder.Build([
            Txn(D(2026,3,5), -30m, "Coffee"),
            Txn(D(2026,3,6), -70m),
        ], March);

        Assert.Equal(100m, s.Spending);
        Assert.Equal(30m, Assert.Single(s.ByCategory).Amount);
    }
}
