using BudgetAssistant.Core.Analysis;

namespace BudgetAssistant.Core.Tests;

/// <summary>
/// Detectors are exercised with hand-built unit vectors so similarity is exact and the
/// tests never load an ONNX model. Angle controls similarity: cos(0deg)=1.0 for an
/// identical merchant, cos(60deg)=0.5 for an unrelated one.
/// </summary>
public class DetectorTests
{
    static float[] AtAngle(double degrees)
    {
        var r = degrees * Math.PI / 180.0;
        return [(float)Math.Cos(r), (float)Math.Sin(r), 0f];
    }
    static readonly float[] Netflix  = AtAngle(0);    // vs Netflix   : 1.000
    static readonly float[] Netflix2 = AtAngle(20);   // vs Netflix   : 0.940  same merchant
    static readonly float[] Hulu     = AtAngle(55);   // vs Netflix   : 0.574  same category
    static readonly float[] Safeway  = AtAngle(80);   // vs Netflix   : 0.174  unrelated

    static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact]
    public void FlagsSameMerchantSameAmountWithinWindow()
    {
        var dupes = DuplicateDetector.Find([
            new(1, 4.75m, D(2026, 3, 10), Netflix),
            new(2, 4.75m, D(2026, 3, 11), Netflix2),
        ]);
        Assert.Equal([(2, 1)], dupes);   // the later one is the duplicate
    }

    [Fact]
    public void IgnoresDifferentAmounts()
    {
        var dupes = DuplicateDetector.Find([
            new(1, 4.75m, D(2026, 3, 10), Netflix),
            new(2, 4.76m, D(2026, 3, 10), Netflix),   // one cent apart
        ]);
        Assert.Empty(dupes);
    }

    [Fact]
    public void IgnoresSameMerchantOutsideTimeWindow()
    {
        // Two identical coffees a week apart are just two coffees.
        var dupes = DuplicateDetector.Find([
            new(1, 4.75m, D(2026, 3, 10), Netflix),
            new(2, 4.75m, D(2026, 3, 20), Netflix),
        ]);
        Assert.Empty(dupes);
    }

    [Fact]
    public void IgnoresDifferentMerchantsThatMerelyShareACategory()
    {
        var dupes = DuplicateDetector.Find([
            new(1, 9.99m, D(2026, 3, 10), Netflix),
            new(2, 9.99m, D(2026, 3, 10), Hulu),
        ]);
        Assert.Empty(dupes);
    }

    [Fact]
    public void ChargedThriceOnlyFlagsTwoDuplicatesNotAChain()
    {
        var dupes = DuplicateDetector.Find([
            new(1, 20m, D(2026, 3, 10), Netflix),
            new(2, 20m, D(2026, 3, 10), Netflix),
            new(3, 20m, D(2026, 3, 10), Netflix),
        ]);
        Assert.Equal(2, dupes.Count);
        Assert.All(dupes, d => Assert.Equal(1, d.OriginalId));   // all point at the original
    }

    [Fact]
    public void FindsMonthlySubscriptionAcrossRotatingReferenceCodes()
    {
        // The whole point of clustering on vectors: these are the same subscription with
        // different reference strings, which exact string grouping would never join.
        var subs = SubscriptionDetector.Find([
            (new(1, -15.99m, D(2026, 1, 5),  Netflix),  "NETFLIX COM"),
            (new(2, -15.99m, D(2026, 2, 4),  Netflix2), "NETFLIX COM"),
            (new(3, -15.99m, D(2026, 3, 6),  Netflix),  "NETFLIX COM"),
        ]);
        var sub = Assert.Single(subs);
        Assert.Equal(-15.99m, sub.Amount);
        Assert.Equal([1, 2, 3], sub.TransactionIds);
        Assert.InRange(sub.MedianGapDays, 26, 35);
    }

    [Fact]
    public void DoesNotCallTwoChargesASubscription()
    {
        var subs = SubscriptionDetector.Find([
            (new(1, -15.99m, D(2026, 1, 5), Netflix), "NETFLIX COM"),
            (new(2, -15.99m, D(2026, 2, 4), Netflix), "NETFLIX COM"),
        ]);
        Assert.Empty(subs);
    }

    [Fact]
    public void DoesNotCallIrregularSpendingASubscription()
    {
        // Same merchant, same amount, but weekly — a habit, not a subscription.
        var subs = SubscriptionDetector.Find([
            (new(1, -5m, D(2026, 1, 5),  Netflix), "COFFEE"),
            (new(2, -5m, D(2026, 1, 12), Netflix), "COFFEE"),
            (new(3, -5m, D(2026, 1, 19), Netflix), "COFFEE"),
        ]);
        Assert.Empty(subs);
    }

    [Fact]
    public void RecurringIncomeIsNotASubscription()
    {
        // A standing transfer into savings is monthly, same amount, same "merchant" —
        // structurally identical to a subscription, but money coming in.
        var subs = SubscriptionDetector.Find([
            (new(1, 600m, D(2026, 1, 16), Netflix), "TRANSFER TO SAVINGS"),
            (new(2, 600m, D(2026, 2, 16), Netflix), "TRANSFER TO SAVINGS"),
            (new(3, 600m, D(2026, 3, 16), Netflix), "TRANSFER TO SAVINGS"),
        ]);
        Assert.Empty(subs);
    }

    [Fact]
    public void SeparatesTwoDifferentSubscriptionsAtTheSamePrice()
    {
        var subs = SubscriptionDetector.Find([
            (new(1, -9.99m, D(2026, 1, 5),  Netflix), "NETFLIX"),
            (new(2, -9.99m, D(2026, 2, 4),  Netflix), "NETFLIX"),
            (new(3, -9.99m, D(2026, 3, 6),  Netflix), "NETFLIX"),
            (new(4, -9.99m, D(2026, 1, 20), Safeway), "SPOTIFY"),
            (new(5, -9.99m, D(2026, 2, 19), Safeway), "SPOTIFY"),
            (new(6, -9.99m, D(2026, 3, 21), Safeway), "SPOTIFY"),
        ]);
        Assert.Equal(2, subs.Count);
        Assert.All(subs, s => Assert.Equal(3, s.TransactionIds.Count));
    }
}
