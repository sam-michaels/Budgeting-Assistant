using Anthropic;
using Anthropic.Models.Messages;
using BudgetAssistant.Core.Abstractions;
using BudgetAssistant.Core.Analysis;

namespace BudgetAssistant.Web.Services;

/// <summary>Deterministic fallback. Always available, never fails, no key required.</summary>
public sealed class TemplateInsightWriter : IInsightWriter
{
    public bool IsModelBacked => false;

    public Task<string> WriteAsync(InsightPayload payload, CancellationToken ct = default)
        => Task.FromResult(Core.Analysis.TemplateInsightWriter.Write(payload));
}

/// <summary>
/// Asks Claude to narrate the month's aggregate.
///
/// Only ever sends <see cref="InsightPayload"/>, which by construction contains no merchant
/// text and no individual transactions — the model sees category totals, not a statement.
///
/// Any failure falls back to the deterministic writer rather than surfacing an error: an
/// unavailable third party should cost you the prose, not the page.
/// </summary>
public sealed class ClaudeInsightWriter(
    AnthropicClient client,
    ILogger<ClaudeInsightWriter> log) : IInsightWriter
{
    public bool IsModelBacked => true;

    const string SystemPrompt = """
        You write one short paragraph summarising a month of personal spending for the
        person whose money it is.

        Rules:
        - Three sentences at most. No preamble, no sign-off, no bullet points.
        - Use only the figures given. Never invent a merchant, a transaction, or a trend.
        - Lead with whatever is most worth acting on, not with the largest number.
        - Plain second person ("you spent"), no financial advice, no recommendations to
          buy or invest in anything.
        - If a duplicate charge is flagged, mention it — it is the most actionable item.
        """;

    public async Task<string> WriteAsync(InsightPayload payload, CancellationToken ct = default)
    {
        try
        {
            var response = await client.Messages.Create(new MessageCreateParams
            {
                Model = "claude-opus-5",
                MaxTokens = 1024,
                // Low effort: this is a short summary over pre-computed figures, not a
                // reasoning task. Keeps latency and cost down without hurting the output.
                OutputConfig = new OutputConfig { Effort = Effort.Low },
                System = SystemPrompt,
                Messages = [new() { Role = Role.User, Content = payload.ToJson() }],
            }, cancellationToken: ct);

            var text = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)).Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                log.LogWarning("Claude returned no text for {Month}; using the template summary", payload.Month);
                return Core.Analysis.TemplateInsightWriter.Write(payload);
            }
            return text;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Insight generation failed for {Month}; using the template summary", payload.Month);
            return Core.Analysis.TemplateInsightWriter.Write(payload);
        }
    }
}
