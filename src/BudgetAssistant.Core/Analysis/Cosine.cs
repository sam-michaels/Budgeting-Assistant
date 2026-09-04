using System.Numerics.Tensors;

namespace BudgetAssistant.Core.Analysis;

public static class Cosine
{
    /// <summary>Cosine similarity in [-1, 1]. Uses the vectorized tensor primitives
    /// rather than a hand-rolled loop.</summary>
    public static float Between(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        => a.Length != b.Length
            ? throw new ArgumentException($"dimension mismatch: {a.Length} vs {b.Length}")
            : TensorPrimitives.CosineSimilarity(a, b);
}
