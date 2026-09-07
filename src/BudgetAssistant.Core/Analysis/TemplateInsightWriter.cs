using System.Globalization;
using System.Text;

namespace BudgetAssistant.Core.Analysis;

/// <summary>
/// Writes the same facts as the language model, deterministically.
///
/// This is the fallback when no API key is configured, and it is not a stub: the endpoint
/// returns the same shape either way, so a missing key degrades the prose rather than
/// breaking the feature. It is also what the tests assert against, since a model's exact
/// wording cannot be pinned.
/// </summary>
public static class TemplateInsightWriter
{
    public static string Write(InsightPayload p)
    {
        var money = CultureInfo.GetCultureInfo("en-US");
        string M(decimal d) => d.ToString("C0", money);

        var sb = new StringBuilder();
        sb.Append($"In {p.Month} you took in {M(p.Income)} and spent {M(p.Spending)}, ");
        sb.Append(p.Net >= 0 ? $"leaving {M(p.Net)} saved. " : $"overspending by {M(Math.Abs(p.Net))}. ");

        if (p.TopCategories.Count > 0)
        {
            var top = p.TopCategories[0];
            sb.Append($"{top.Category} was the largest category at {M(top.Amount)}");
            sb.Append(top.ChangeVsLastMonth is { } c && Math.Abs(c) >= 0.05m
                ? $", {(c > 0 ? "up" : "down")} {Math.Abs(c):P0} from last month. "
                : ". ");

            var climbing = p.TopCategories
                .Where(x => x.ChangeVsLastMonth >= 0.20m)
                .OrderByDescending(x => x.ChangeVsLastMonth)
                .Take(2).ToList();
            if (climbing.Count > 0)
                sb.Append($"Rising fastest: {string.Join(" and ", climbing.Select(x => $"{x.Category} (+{x.ChangeVsLastMonth:P0})"))}. ");
        }

        if (p.SubscriptionSpend > 0)
            sb.Append($"Recurring charges accounted for {M(p.SubscriptionSpend)}. ");

        if (p.DuplicateChargeCount > 0)
            sb.Append($"{p.DuplicateChargeCount} possible double {(p.DuplicateChargeCount == 1 ? "charge is" : "charges are")} worth checking.");

        return sb.ToString().TrimEnd();
    }
}
