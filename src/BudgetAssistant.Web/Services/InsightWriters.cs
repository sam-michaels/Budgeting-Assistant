using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Anthropic;
using Anthropic.Models.Messages;
using BudgetAssistant.Core.Abstractions;
using BudgetAssistant.Core.Analysis;

namespace BudgetAssistant.Web.Services;

/// <summary>
/// The instructions every model-backed writer sends. Shared rather than duplicated: the
/// point of having three writers is that they produce the same kind of paragraph, so the
/// prose should not drift depending on which one answered.
/// </summary>
static class InsightPrompt
{
    public const string System = """
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
}

/// <summary>Deterministic fallback. Always available, never fails, no key required.</summary>
public sealed class TemplateInsightWriter : IInsightWriter
{
    public string Source => IInsightWriter.Template;

    public Task<string> WriteAsync(InsightPayload payload, CancellationToken ct = default)
        => Task.FromResult(Core.Analysis.TemplateInsightWriter.Write(payload));
}

/// <summary>
/// Asks a model running locally under Ollama to narrate the month's aggregate.
///
/// One class serves both local tiers — a small instruct model writes the monthly summary,
/// and DeepSeek-R1 handles anything that needs reasoning — because the only thing that
/// differs between them is the model name and whether the reply arrives wrapped in a
/// thinking block. See <see cref="ReasoningBlock"/>.
///
/// This is the default writer, and it is the strongest version of the privacy property the
/// rest of the app already has: embeddings never leave the machine, and on this path the
/// month's figures do not either. Nothing is sent anywhere.
///
/// Any failure falls back to the deterministic writer rather than surfacing an error — an
/// unavailable model should cost you the prose, not the page.
/// </summary>
public sealed partial class OllamaInsightWriter(
    HttpClient http,
    string model,
    ILogger<OllamaInsightWriter> log) : IInsightWriter
{
    public string Source => model;

    // Reasoning models emit their scratchpad inline in the message content. Newer Ollama
    // builds split it into a separate `thinking` field for models it has a parser for, but
    // that is not guaranteed for every model or every version, and a leaked <think> block
    // would land straight on the dashboard.
    //
    // The second branch catches a response truncated mid-reasoning, where the closing tag
    // never arrives — that yields no prose at all, so the caller falls back to the template.
    //
    // Note we deliberately do NOT send Ollama's `think: false`: it is rejected outright by
    // models with no thinking support, which includes the default summary model. Stripping
    // handles both kinds of model with one mechanism and no capability list to maintain.
    [GeneratedRegex(@"<think>.*?</think>|<think>.*$", RegexOptions.Singleline)]
    private static partial Regex ReasoningBlock();

    public static string StripReasoning(string text) => ReasoningBlock().Replace(text, "").Trim();

    record ChatMessage(string Role, string Content);
    record ChatOptions(double Temperature, int Num_Predict);
    record ChatRequest(string Model, ChatMessage[] Messages, bool Stream, ChatOptions Options);
    record ChatResponseMessage(string? Content);
    record ChatResponse(ChatResponseMessage? Message);

    public async Task<string> WriteAsync(InsightPayload payload, CancellationToken ct = default)
    {
        try
        {
            var response = await http.PostAsJsonAsync("/api/chat", new ChatRequest(
                Model: model,
                Messages:
                [
                    new("system", InsightPrompt.System),
                    new("user", payload.ToJson()),
                ],
                Stream: false,
                // Low temperature: this is narration over figures that are already decided,
                // and a creative rephrasing of someone's bank balance is not an improvement.
                //
                // The token cap is a stop against a model that will not shut up, not a
                // length target — an instruct model finishes three sentences in well under
                // 100 tokens. It has to clear reasoning models by a wide margin because
                // their thinking counts against it: measured, R1 spends ~365 tokens on this
                // payload before writing a word, so a 400 cap truncates it mid-thought and
                // yields no prose at all.
                Options: new ChatOptions(Temperature: 0.2, Num_Predict: 1200)),
                JsonSerializerOptions.Web, ct);

            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonSerializerOptions.Web, ct);
            var text = StripReasoning(body?.Message?.Content ?? "");

            if (string.IsNullOrWhiteSpace(text))
            {
                log.LogWarning("{Model} returned no usable text for {Month}; using the template summary", model, payload.Month);
                return Core.Analysis.TemplateInsightWriter.Write(payload);
            }
            return text;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Insight generation via {Model} failed for {Month}; using the template summary", model, payload.Month);
            return Core.Analysis.TemplateInsightWriter.Write(payload);
        }
    }
}

/// <summary>
/// Asks Claude to narrate the month's aggregate.
///
/// Not part of the automatic ladder: both tiers of that are local (a small model for the
/// summary, DeepSeek-R1 for reasoning). This runs only when Insights:Provider names it, for
/// analysis that outgrows what a local model can carry.
///
/// Only ever sends <see cref="InsightPayload"/>, which by construction contains no merchant
/// text and no individual transactions — the model sees category totals, not a statement.
///
/// Any failure falls back to the deterministic writer, same rule as the local writer.
/// </summary>
public sealed class ClaudeInsightWriter(
    AnthropicClient client,
    string model,
    ILogger<ClaudeInsightWriter> log) : IInsightWriter
{
    public string Source => model;

    public async Task<string> WriteAsync(InsightPayload payload, CancellationToken ct = default)
    {
        try
        {
            var response = await client.Messages.Create(new MessageCreateParams
            {
                Model = model,
                MaxTokens = 1024,
                // Low effort: this is a short summary over pre-computed figures, not a
                // reasoning task. Keeps latency and cost down without hurting the output.
                OutputConfig = new OutputConfig { Effort = Effort.Low },
                System = InsightPrompt.System,
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
