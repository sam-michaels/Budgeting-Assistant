namespace BudgetAssistant.Core.Analysis;

/// <summary>
/// Cosine-similarity thresholds, calibrated empirically against bge-micro-v2 on
/// normalized merchant strings. Measured bands:
///
///     identical        1.000
///     same merchant    0.83 - 0.86     "SPOTIFY P0A1B2C3" vs "SPOTIFY AB99XY12" = 0.826
///     same category    0.61 - 0.68     "SHELL OIL" vs "CHEVRON"                 = 0.612
///     unrelated        0.49 - 0.51
///
/// These are model-specific. Swapping the embedding model REQUIRES re-measuring them;
/// tests/BudgetAssistant.Core.Tests documents the expected ordering.
/// </summary>
public static class SimilarityBands
{
    /// <summary>Same merchant. Sits below the observed 0.826 floor for a real repeat
    /// charge, and well above the 0.68 ceiling for merely same-category pairs.</summary>
    public const float SameMerchant = 0.80f;

    /// <summary>Weakest evidence accepted for a category suggestion. Below the observed
    /// 0.61 same-category floor, above the 0.51 unrelated ceiling.</summary>
    public const float CategoryFloor = 0.55f;
}
