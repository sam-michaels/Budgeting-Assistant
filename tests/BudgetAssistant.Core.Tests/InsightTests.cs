using BudgetAssistant.Core.Analysis;
// Aliased rather than imported: Web.Services has its own TemplateInsightWriter (the
// IInsightWriter adapter around the Core one), and importing the namespace makes every
// unqualified use in this file ambiguous.
using OllamaInsightWriter = BudgetAssistant.Web.Services.OllamaInsightWriter;

namespace BudgetAssistant.Core.Tests;

public class InsightPayloadTests
{
    static MonthlySummary BuildSummary()
    {
        var rows = new SummaryInput[]
        {
            new(new DateOnly(2026,3,15),  3120.55m, null,          null,      false, false),
            new(new DateOnly(2026,2,10),  -100.00m, "Restaurants", "#D2694A", false, false),
            new(new DateOnly(2026,3,10),  -160.00m, "Restaurants", "#D2694A", false, false),
            new(new DateOnly(2026,3,4),    -15.99m, "Streaming",   "#D14D72", true,  false),
            new(new DateOnly(2026,3,12),   -42.00m, "Groceries",   "#4C9F70", false, true),
        };
        return SummaryBuilder.Build(rows, new DateOnly(2026, 3, 1));
    }

    [Fact]
    public void PayloadCarriesNoMerchantTextOrTransactionDetail()
    {
        // The guarantee is structural: the payload type has no field that could hold a
        // statement description, so raw merchant strings cannot reach a third party.
        var json = InsightPayload.From(BuildSummary()).ToJson();

        foreach (var leak in new[] { "SQ *", "BLUE BOTTLE", "NETFLIX", "4471", "demo@", "Everyday Checking" })
            Assert.DoesNotContain(leak, json, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("Restaurants", json);   // category names are a fixed system list
        Assert.Contains("March 2026", json);
    }

    [Fact]
    public void PayloadKeepsOnlyTheTopCategories()
    {
        var many = Enumerable.Range(1, 12)
            .Select(i => new SummaryInput(new DateOnly(2026, 3, i), -i * 10m, $"Cat{i}", "#888888", false, false))
            .ToArray();

        var payload = InsightPayload.From(SummaryBuilder.Build(many, new DateOnly(2026, 3, 1)), topN: 6);
        Assert.Equal(6, payload.TopCategories.Count);
        Assert.Equal("Cat12", payload.TopCategories[0].Category);   // largest first
    }

    [Fact]
    public void TemplateWriterStatesTheHeadlineNumbers()
    {
        var text = TemplateInsightWriter.Write(InsightPayload.From(BuildSummary()));

        Assert.Contains("March 2026", text);
        Assert.Contains("$3,121", text);          // income, rounded for prose
        Assert.Contains("Restaurants", text);     // largest category
        Assert.Contains("double charge", text);   // the flagged duplicate
    }

    [Fact]
    public void TemplateWriterHandlesAnEmptyMonthWithoutCrashing()
    {
        var empty = SummaryBuilder.Build([], new DateOnly(2026, 3, 1));
        var text = TemplateInsightWriter.Write(InsightPayload.From(empty));
        Assert.Contains("March 2026", text);
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public void TemplateWriterReportsOverspending()
    {
        var rows = new SummaryInput[] { new(new DateOnly(2026,3,10), -500m, "Housing", "#8E6BB5", false, false) };
        var text = TemplateInsightWriter.Write(InsightPayload.From(SummaryBuilder.Build(rows, new DateOnly(2026,3,1))));
        Assert.Contains("overspending by $500", text);
    }
}

/// <summary>
/// DeepSeek-R1 is a configured tier, and it wraps its scratchpad in a think block. Anything
/// that leaks through lands verbatim on the dashboard, so the stripping is load-bearing.
/// </summary>
public class ReasoningStrippingTests
{
    const string Prose = "You spent $4,210 in September, $340 more than August.";

    [Fact]
    public void Removes_a_closed_think_block()
        => Assert.Equal(Prose, OllamaInsightWriter.StripReasoning(
            $"<think>The user wants three sentences. Spending is 4210.</think>\n\n{Prose}"));

    [Fact]
    public void Removes_a_think_block_spanning_newlines()
        => Assert.Equal(Prose, OllamaInsightWriter.StripReasoning(
            $"<think>\nStep one.\nStep two.\n</think>{Prose}"));

    /// <summary>A reply truncated mid-reasoning has no prose in it at all. Stripping to
    /// empty is correct: the writer then falls back to the deterministic template rather
    /// than publishing half a thought.</summary>
    [Fact]
    public void Removes_an_unclosed_think_block_from_a_truncated_reply()
        => Assert.Equal("", OllamaInsightWriter.StripReasoning(
            "<think>The user wants three sentences. Let me check the duplicate"));

    [Fact]
    public void Leaves_a_reply_that_never_reasoned_alone()
        => Assert.Equal(Prose, OllamaInsightWriter.StripReasoning($"  {Prose}  "));
}
