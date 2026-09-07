using BudgetAssistant.Core.Analysis;

namespace BudgetAssistant.Core.Tests;

public class TransferDetectorTests
{
    static bool Detect(string raw) => TransferDetector.IsTransfer(MerchantNormalizer.Normalize(raw));

    [Theory]
    [InlineData("TRANSFER TO SAVINGS")]
    [InlineData("ONLINE TRANSFER FROM CHK 4471")]      // digits survive normalization as noise
    [InlineData("XFER TO SAV XXXX9931")]
    [InlineData("ACH TRANSFERS INTERNAL")]             // plural
    [InlineData("CHASE CREDIT CRD AUTOPAY PAYMENT THANK YOU")]
    [InlineData("CARDMEMBER PAYMENT ELECTRONIC")]
    public void RecognizesMoneyMovingBetweenOwnAccounts(string raw) => Assert.True(Detect(raw));

    [Theory]
    [InlineData("TRANSFERWISE INC")]                   // a merchant that starts with the word
    [InlineData("ACME CORP PAYROLL")]
    [InlineData("GREYSTAR RENT PMT 4471")]
    [InlineData("SQ *BLUE BOTTLE 4471")]
    [InlineData("")]
    public void LeavesEverythingElseAlone(string raw) => Assert.False(Detect(raw));
}

public class TransferSummaryTests
{
    static SummaryInput Txn(int day, decimal amount, string? cat = null, bool transfer = false)
        => new(new DateOnly(2026, 3, day), amount, cat, "#888888", false, false, transfer);

    static readonly DateOnly March = new(2026, 3, 1);

    [Fact]
    public void TransfersCountAsNeitherIncomeNorSpending()
    {
        // The sweep nets to zero. Counted, it would report $600 more earned and $600 more
        // spent than actually happened, and leave Net accidentally correct for the wrong
        // reason -- which is why both sides are asserted, not just Net.
        var s = SummaryBuilder.Build([
            Txn(15,  3120.55m, "Income"),
            Txn(16,  -600.00m, "Transfer", transfer: true),
            Txn(16,   600.00m, "Transfer", transfer: true),
            Txn(17,   -42.10m, "Groceries"),
        ], March);

        Assert.Equal(3120.55m, s.Income);
        Assert.Equal(42.10m, s.Spending);
        Assert.Equal(600.00m, s.Transfers);          // reported once, from the outgoing leg
        Assert.DoesNotContain(s.ByCategory, c => c.Category == "Transfer");
    }

    [Fact]
    public void PreviousMonthComparisonExcludesThemToo()
    {
        // Otherwise February carries a phantom $600 of "spending" and every category
        // delta on the dashboard is measured against a month that never happened.
        var s = SummaryBuilder.Build([
            new(new DateOnly(2026, 2, 16), -600m, "Transfer", "#888888", false, false, true),
            new(new DateOnly(2026, 2, 17), -100m, "Groceries", "#888888", false, false, false),
            Txn(17, -150m, "Groceries"),
        ], March);

        var groceries = Assert.Single(s.ByCategory);
        Assert.Equal(150m, groceries.Amount);
        Assert.Equal(100m, groceries.PreviousAmount);
    }
}
