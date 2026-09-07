using System.Globalization;
using BudgetAssistant.Core.Abstractions;
using BudgetAssistant.Core.Analysis;
using BudgetAssistant.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace BudgetAssistant.Web.Controllers;

/// <param name="Source">What wrote the summary: a model id such as "llama3.2:3b", or
/// "template" when the deterministic writer did.</param>
public record InsightResponse(
    string Month, string Summary, bool ModelGenerated, string Source, InsightPayload Figures);

public sealed class InsightsController(
    BudgetQueries queries,
    IInsightWriter writer,
    IMemoryCache cache,
    ILogger<InsightsController> log) : BudgetControllerBase
{
    /// <summary>
    /// A short natural-language summary of a month's spending.
    /// </summary>
    /// <param name="month">Month as yyyy-MM. Defaults to the current month.</param>
    /// <remarks>
    /// The summary is written by whichever model is configured — a local one under Ollama
    /// by default — and by a deterministic template when none is available. <c>source</c>
    /// names it. The figures the summary describes are returned alongside it, so the prose
    /// is always checkable against the numbers.
    /// </remarks>
    [HttpGet("monthly-summary")]
    [ProducesResponseType<InsightResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<InsightResponse>> MonthlySummary(
        [FromQuery] string? month, CancellationToken ct)
    {
        if (!TryParseMonth(month, out var parsed))
            return BadRequest($"Could not read '{month}' as a month. Use yyyy-MM, for example 2026-03.");

        // Cached per user and month: the figures for a past month do not change, and a
        // dashboard visit should not bill an API call every render.
        var key = $"insight:{UserId}:{parsed:yyyy-MM}";
        if (cache.TryGetValue(key, out InsightResponse? cached) && cached is not null)
            return cached;

        var summary = await queries.SummaryAsync(UserId, parsed, ct);
        var payload = InsightPayload.From(summary);
        var text = await writer.WriteAsync(payload, ct);

        var response = new InsightResponse(
            payload.Month, text, writer.Source != IInsightWriter.Template, writer.Source, payload);
        cache.Set(key, response, TimeSpan.FromHours(6));

        log.LogInformation("Generated {Source} insight for {Month}", writer.Source, payload.Month);

        return response;
    }

    static bool TryParseMonth(string? raw, out DateOnly month)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            var now = DateTime.UtcNow;
            month = new DateOnly(now.Year, now.Month, 1);
            return true;
        }
        if (DateOnly.TryParseExact($"{raw}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                   DateTimeStyles.None, out month))
            return true;
        month = default;
        return false;
    }
}
