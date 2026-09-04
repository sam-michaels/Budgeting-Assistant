using System.Text.Json;
using System.Text.Json.Serialization;

namespace BudgetAssistant.Core.Analysis;

public record InsightCategoryLine(string Category, decimal Amount, decimal? ChangeVsLastMonth);

/// <summary>
/// The aggregate sent to the language model.
///
/// Deliberately carries no merchant text, no account names, no dates, and no individual
/// transactions — only category names from a fixed system list and rounded totals. A
/// spending summary does not require sending anyone's statement to a third party, and in
/// a financial application the burden is on the code to make that impossible rather than
/// unlikely. <c>InsightPayloadTests</c> asserts it.
/// </summary>
public record InsightPayload(
    string Month,
    decimal Income,
    decimal Spending,
    decimal Net,
    IReadOnlyList<InsightCategoryLine> TopCategories,
    int DuplicateChargeCount,
    decimal SubscriptionSpend)
{
    public static InsightPayload From(MonthlySummary s, int topN = 6) => new(
        Month: s.Month.ToString("MMMM yyyy"),
        Income: decimal.Round(s.Income, 2),
        Spending: decimal.Round(s.Spending, 2),
        Net: decimal.Round(s.Net, 2),
        TopCategories: s.ByCategory.Take(topN)
            .Select(c => new InsightCategoryLine(
                c.Category,
                decimal.Round(c.Amount, 2),
                c.DeltaFraction is { } f ? decimal.Round(f, 3) : null))
            .ToList(),
        DuplicateChargeCount: s.DuplicateCount,
        SubscriptionSpend: decimal.Round(s.SubscriptionSpend, 2));

    static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);
}
