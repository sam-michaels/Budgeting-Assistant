using BudgetAssistant.Web.Services;

namespace BudgetAssistant.Core.Tests;

public class CsvTransactionReaderTests
{
    static CsvParseResult Read(string csv) => CsvTransactionReader.Read(new StringReader(csv));

    [Fact]
    public void ReadsASignedAmountColumn()
    {
        var r = Read("""
            Date,Description,Amount
            2026-03-14,SQ *BLUE BOTTLE 4471,-4.75
            2026-03-15,ACME CORP PAYROLL,3120.55
            """);

        Assert.Empty(r.Errors);
        Assert.Equal(2, r.Rows.Count);
        Assert.Equal(-4.75m, r.Rows[0].Amount);
        Assert.Equal(3120.55m, r.Rows[1].Amount);
        Assert.Equal(new DateOnly(2026, 3, 14), r.Rows[0].Date);
    }

    [Fact]
    public void ReadsSeparateDebitAndCreditColumns()
    {
        // A debit is money leaving, so it must land negative even though the column is positive.
        var r = Read("""
            Posted Date,Merchant,Debit,Credit
            2026-03-14,SAFEWAY #1234,52.10,
            2026-03-15,REFUND,,18.00
            """);

        Assert.Empty(r.Errors);
        Assert.Equal(-52.10m, r.Rows[0].Amount);
        Assert.Equal(18.00m, r.Rows[1].Amount);
    }

    [Fact]
    public void HandlesCurrencySymbolsThousandsAndAccountingNegatives()
    {
        var r = Read("""
            Date,Description,Amount
            2026-03-01,RENT,"($2,150.00)"
            2026-03-02,BONUS,"$1,000.00"
            """);

        Assert.Empty(r.Errors);
        Assert.Equal(-2150.00m, r.Rows[0].Amount);
        Assert.Equal(1000.00m, r.Rows[1].Amount);
    }

    [Fact]
    public void ReportsMissingColumnsInsteadOfThrowing()
    {
        var r = Read("""
            Foo,Bar
            1,2
            """);

        Assert.Empty(r.Rows);
        Assert.Equal(3, r.Errors.Count);
        Assert.Contains(r.Errors, e => e.Contains("date column"));
        Assert.Contains(r.Errors, e => e.Contains("description column"));
        Assert.Contains(r.Errors, e => e.Contains("amount column"));
    }

    [Fact]
    public void SkipsBadRowsAndKeepsTheGoodOnes()
    {
        var r = Read("""
            Date,Description,Amount
            2026-03-14,GOOD ROW,-10.00
            not-a-date,BAD DATE,-10.00
            2026-03-16,,-10.00
            2026-03-17,ANOTHER GOOD,-20.00
            """);

        Assert.Equal(2, r.Rows.Count);
        Assert.Equal(2, r.Errors.Count);
        Assert.Contains(r.Errors, e => e.Contains("Row 3") && e.Contains("date"));
        Assert.Contains(r.Errors, e => e.Contains("Row 4") && e.Contains("empty"));
    }

    [Fact]
    public void AcceptsAlternateHeaderNamesAndCasing()
    {
        var r = Read("""
            TRANSACTION DATE,Payee,Value
            2026-03-14,NETFLIX.COM,-15.99
            """);

        Assert.Empty(r.Errors);
        Assert.Equal("NETFLIX.COM", Assert.Single(r.Rows).Description);
    }

    [Fact]
    public void EmptyFileIsAnErrorNotACrash()
    {
        var r = Read("");
        Assert.Empty(r.Rows);
        Assert.Single(r.Errors);
    }
}
