using System.Text.RegularExpressions;

namespace BudgetAssistant.Core.Analysis;

/// <summary>
/// Strips payment-processor noise from a raw statement description so that only the
/// merchant identity is embedded.
///
/// This is not cosmetic. Measured against bge-micro-v2, raw descriptions rank
/// *different* merchants above the *same* merchant, because shared digit-noise
/// dominates the embedding:
///
///     raw   "SQ *BLUE BOTTLE 4471" vs "BLUE BOTTLE COFFEE"  = 0.728   (same merchant)
///     raw   "SHELL OIL 574839201"  vs "CHEVRON 00923845"    = 0.764   (different!)
///
/// After normalization the bands separate cleanly (0.856 vs 0.612), which is what
/// makes <see cref="SimilarityBands"/> usable at all.
/// </summary>
public static partial class MerchantNormalizer
{
    // The separator is required: without it "SP" swallows the start of "SPOTIFY".
    [GeneratedRegex(@"^(SQ|TST|SP|PY|PAYPAL|POS|DEBIT|CREDIT|PURCHASE)(\s*\*+\s*|\s+)")] private static partial Regex Processor();
    [GeneratedRegex(@"[#*]\s*\d+")]        private static partial Regex StoreNumber();
    [GeneratedRegex(@"\b[X\*]{2,}\d+\b")]  private static partial Regex MaskedCard();
    [GeneratedRegex(@"\b\d{1,2}/\d{1,2}(/\d{2,4})?\b")] private static partial Regex Date();
    [GeneratedRegex(@"\b\d{3,}\b")]        private static partial Regex LongDigits();
    // Reference codes: 4+ char alphanumeric runs containing a digit ("P0A1B2C3", "XXXX9931").
    // Stripped whole, otherwise the digits vanish and the letters survive as noise.
    [GeneratedRegex(@"\b(?=[A-Z0-9]*\d)[A-Z0-9]{4,}\b")] private static partial Regex ReferenceCode();
    [GeneratedRegex(@"[^A-Z ]")]           private static partial Regex NonAlpha();
    [GeneratedRegex(@"\s+")]               private static partial Regex Whitespace();

    public static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.ToUpperInvariant();
        s = Processor().Replace(s, "");
        s = StoreNumber().Replace(s, " ");
        s = MaskedCard().Replace(s, " ");
        s = Date().Replace(s, " ");
        s = ReferenceCode().Replace(s, " ");
        s = LongDigits().Replace(s, " ");
        s = NonAlpha().Replace(s, " ");
        return Whitespace().Replace(s, " ").Trim();
    }
}
