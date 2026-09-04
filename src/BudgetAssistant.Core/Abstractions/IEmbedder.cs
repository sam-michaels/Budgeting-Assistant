namespace BudgetAssistant.Core.Abstractions;

/// <summary>Turns merchant text into a vector. Implemented in the Web project so Core
/// stays free of ONNX and Npgsql; tests substitute a deterministic fake.</summary>
public interface IEmbedder
{
    int Dimensions { get; }
    float[] Embed(string text);
}
