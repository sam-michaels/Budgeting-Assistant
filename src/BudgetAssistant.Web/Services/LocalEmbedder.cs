using BudgetAssistant.Core.Abstractions;
using SmartComponents.LocalEmbeddings;

namespace BudgetAssistant.Web.Services;

/// <summary>
/// bge-micro-v2 running in-process through ONNX Runtime. Registered as a singleton:
/// the underlying embedder pools its inference sessions and loading the model per
/// request would dominate the cost of everything else.
///
/// The privacy property matters more than the performance one: no transaction text is
/// ever sent to a third-party API.
/// </summary>
public sealed class LocalTextEmbedder : IEmbedder, IDisposable
{
    readonly LocalEmbedder _inner = new();

    public int Dimensions => 384;

    public float[] Embed(string text)
        => string.IsNullOrWhiteSpace(text)
            ? new float[Dimensions]
            : _inner.Embed(text).Values.ToArray();

    public void Dispose() => _inner.Dispose();
}
