using BudgetAssistant.Core.Analysis;

namespace BudgetAssistant.Core.Tests;

public class KnnCategorizerTests
{
    const int Coffee = 1, Groceries = 2, Gas = 3;

    [Fact]
    public void PicksCategoryWithHighestWeightedVote()
    {
        var s = KnnCategorizer.Suggest([
            new(Coffee,    "BLUE BOTTLE", 0.86f),
            new(Coffee,    "STARBUCKS",   0.71f),
            new(Groceries, "SAFEWAY",     0.60f),
        ]);
        Assert.Equal(Coffee, s!.Value.CategoryId);
        Assert.Equal("BLUE BOTTLE", s.Value.MatchedOn);
    }

    [Fact]
    public void OneStrongMatchOutvotesSeveralMarginalOnes()
    {
        // Barely-clearing neighbours must not win on volume.
        var s = KnnCategorizer.Suggest([
            new(Coffee,    "BLUE BOTTLE", 0.86f),
            new(Groceries, "SAFEWAY",     0.56f),
            new(Groceries, "KROGER",      0.57f),
        ]);
        Assert.Equal(Coffee, s!.Value.CategoryId);
    }

    [Fact]
    public void ExactMatchBeatsACrowdedNeighbouringCategory()
    {
        // The real failure this rule exists for: "GEICO AUTO PMT" is a verbatim Insurance
        // example, but five Housing examples containing "PMT" summed higher under plain
        // similarity weighting and stole the category.
        const int Insurance = 4, Housing = 5;
        var s = KnnCategorizer.Suggest([
            new(Insurance, "GEICO AUTO PMT",    1.00f),
            new(Housing,   "MORTGAGE PMT WELLS",0.66f),
            new(Housing,   "GREYSTAR RENT PMT", 0.65f),
            new(Housing,   "ZILLOW RENTAL PMT", 0.64f),
            new(Housing,   "HOA MONTHLY DUES",  0.63f),
            new(Housing,   "PROPERTY MGMT LLC", 0.62f),
        ], k: 6);
        Assert.Equal(Insurance, s!.Value.CategoryId);
        Assert.Equal("GEICO AUTO PMT", s.Value.MatchedOn);
    }

    [Fact]
    public void ReturnsNullWhenNothingClearsTheFloor()
    {
        // Unrelated merchants measure ~0.49-0.51. Guessing here would silently
        // corrupt a budget; leaving it blank asks the user instead.
        var s = KnnCategorizer.Suggest([
            new(Coffee,    "BLUE BOTTLE", 0.51f),
            new(Groceries, "SAFEWAY",     0.49f),
        ]);
        Assert.Null(s);
    }

    [Fact]
    public void AcceptsSameCategoryBandMatches()
    {
        // "SHELL OIL" vs "CHEVRON" measures 0.612 — a real same-category signal that a
        // 0.60 floor would have rejected. This is why the floor is 0.55.
        var s = KnnCategorizer.Suggest([new(Gas, "CHEVRON", 0.612f)]);
        Assert.Equal(Gas, s!.Value.CategoryId);
    }

    [Fact]
    public void UnanimousButDistantNeighborsDoNotYieldHighConfidence()
    {
        // Every neighbour agrees, so consensus is total — but the closest labelled example
        // is still 0.72 away, and confidence must say so. This is the ZUNI CAFE case: all
        // Coffee neighbours agreed on a restaurant.
        var s = KnnCategorizer.Suggest([
            new(Coffee, "PHILZ COFFEE", 0.72f),
            new(Coffee, "STARBUCKS",    0.70f),
        ]);
        Assert.Equal(Coffee, s!.Value.CategoryId);
        Assert.Equal(0.72f, s.Value.Confidence, 2);   // consensus 1.0, scaled by best match
    }

    [Fact]
    public void ConfidenceApproachesCertaintyOnlyForNearExactMatches()
    {
        var s = KnnCategorizer.Suggest([
            new(Coffee, "BLUE BOTTLE COFFEE", 1.00f),
            new(Coffee, "STARBUCKS",          0.72f),
        ]);
        Assert.Equal(1.0f, s!.Value.Confidence, 2);
    }

    [Fact]
    public void RespectsK()
    {
        // k=1 keeps only the single best neighbour, flipping the winner. Both Groceries
        // matches are strong here, so together they legitimately outweigh one better match.
        var neighbors = new Neighbor[] {
            new(Coffee,    "BLUE BOTTLE", 0.90f),
            new(Groceries, "SAFEWAY",     0.85f),
            new(Groceries, "KROGER",      0.84f),
        };
        Assert.Equal(Groceries, KnnCategorizer.Suggest(neighbors)!.Value.CategoryId);
        Assert.Equal(Coffee,    KnnCategorizer.Suggest(neighbors, k: 1)!.Value.CategoryId);
    }

    [Fact]
    public void EmptyNeighborsYieldNoSuggestion()
        => Assert.Null(KnnCategorizer.Suggest([]));
}
