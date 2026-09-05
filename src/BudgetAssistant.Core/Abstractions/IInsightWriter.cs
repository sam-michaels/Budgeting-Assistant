using BudgetAssistant.Core.Analysis;

namespace BudgetAssistant.Core.Abstractions;

/// <summary>Turns a month's aggregate into a short readable summary.</summary>
public interface IInsightWriter
{
    /// <summary>What wrote the text: a model id such as "llama3.2:3b" or "claude-opus-5",
    /// or <see cref="Template"/> when the deterministic writer did. Surfaced in the API and
    /// the UI so a reader can tell which they received — with a local model, a hosted one
    /// and a template all in play, a boolean cannot say.</summary>
    string Source { get; }

    Task<string> WriteAsync(InsightPayload payload, CancellationToken ct = default);

    /// <summary>The <see cref="Source"/> value meaning "no model was involved".</summary>
    public const string Template = "template";
}
