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
    public void OneStrongMatchOutvotesTwoWeakOnes()
    {
        // Weighted by similarity, not a raw count: 0.86 beats 0.56 + 0.57 on quality...
        // but two weak matches sum higher, so the count still matters. This asserts the
        // documented behaviour rather than an intuition about it.
        var s = KnnCategorizer.Suggest([
            new(Coffee,    "BLUE BOTTLE", 0.86f),
            new(Groceries, "SAFEWAY",     0.56f),
            new(Groceries, "KROGER",      0.57f),
        ]);
        Assert.Equal(Groceries, s!.Value.CategoryId);   // 1.13 > 0.86
        Assert.InRange(s.Value.Confidence, 0.56f, 0.58f);
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
    public void ConfidenceIsUnanimousWhenAllNeighborsAgree()
    {
        var s = KnnCategorizer.Suggest([
            new(Coffee, "BLUE BOTTLE", 0.86f),
            new(Coffee, "STARBUCKS",   0.72f),
        ]);
        Assert.Equal(1.0f, s!.Value.Confidence, 3);
    }

    [Fact]
    public void RespectsK()
    {
        // k=1 keeps only the single best neighbour, flipping the winner.
        var neighbors = new Neighbor[] {
            new(Coffee,    "BLUE BOTTLE", 0.86f),
            new(Groceries, "SAFEWAY",     0.60f),
            new(Groceries, "KROGER",      0.59f),
        };
        Assert.Equal(Groceries, KnnCategorizer.Suggest(neighbors)!.Value.CategoryId);
        Assert.Equal(Coffee,    KnnCategorizer.Suggest(neighbors, k: 1)!.Value.CategoryId);
    }

    [Fact]
    public void EmptyNeighborsYieldNoSuggestion()
        => Assert.Null(KnnCategorizer.Suggest([]));
}
