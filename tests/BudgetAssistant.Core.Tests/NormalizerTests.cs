using BudgetAssistant.Core.Analysis;

namespace BudgetAssistant.Core.Tests;

public class MerchantNormalizerTests
{
    [Theory]
    [InlineData("SQ *BLUE BOTTLE 4471", "BLUE BOTTLE")]
    [InlineData("TST* PIZZA HUT 03/14", "PIZZA HUT")]
    [InlineData("SHELL OIL 574839201", "SHELL OIL")]
    [InlineData("NETFLIX.COM 8667797", "NETFLIX COM")]
    [InlineData("SAFEWAY #1234 GROCERY", "SAFEWAY GROCERY")]
    [InlineData("PAYPAL *SPOTIFY USA", "SPOTIFY USA")]
    [InlineData("AMAZON MKTPL XXXX9931", "AMAZON MKTPL")]
    // Reference codes are stripped whole, not reduced to letter noise.
    [InlineData("SPOTIFY USA P0A1B2C3", "SPOTIFY USA")]
    [InlineData("AIRBNB * HMX8821", "AIRBNB")]
    // A processor prefix only counts when a separator follows it: "SP" must not eat
    // the start of "SPOTIFY", nor "POS" the start of "POSTMATES".
    [InlineData("SPOTIFY USA", "SPOTIFY USA")]
    [InlineData("POSTMATES DELIVERY", "POSTMATES DELIVERY")]
    [InlineData("SP * CORNER STORE", "CORNER STORE")]
    public void StripsProcessorNoiseButKeepsMerchant(string raw, string expected)
        => Assert.Equal(expected, MerchantNormalizer.Normalize(raw));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    public void HandlesDegenerateInput(string raw)
        => Assert.Equal("", MerchantNormalizer.Normalize(raw));

    [Fact]
    public void IsIdempotent()
    {
        var once = MerchantNormalizer.Normalize("SQ *BLUE BOTTLE 4471");
        Assert.Equal(once, MerchantNormalizer.Normalize(once));
    }
}
