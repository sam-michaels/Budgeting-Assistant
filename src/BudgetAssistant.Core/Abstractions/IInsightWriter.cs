using BudgetAssistant.Core.Analysis;

namespace BudgetAssistant.Core.Abstractions;

/// <summary>Turns a month's aggregate into a short readable summary.</summary>
public interface IInsightWriter
{
    /// <summary>True when a language model produced the text, false when the deterministic
    /// template did. Surfaced in the API so a caller can tell which they received.</summary>
    bool IsModelBacked { get; }

    Task<string> WriteAsync(InsightPayload payload, CancellationToken ct = default);
}
