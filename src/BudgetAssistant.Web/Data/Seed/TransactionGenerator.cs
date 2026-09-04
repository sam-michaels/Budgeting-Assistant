namespace BudgetAssistant.Web.Data.Seed;

public readonly record struct GeneratedTxn(string Description, decimal Amount, DateOnly Date, int AccountIndex);

/// <summary>
/// Builds six months of plausible statement data.
///
/// Two properties matter more than volume:
///
/// 1. The merchant strings are deliberately NOT copies of <see cref="SeedCorpus"/>. Some
///    are unseen merchants in a known category and some are obscure enough to fall below
///    the categorization floor, so the demo shows a real spread of confidence instead of
///    a wall of 100% matches that proves nothing.
/// 2. Duplicates and subscriptions are planted on purpose, including subscriptions whose
///    reference codes rotate between charges — the case exact string matching cannot
///    group and vector clustering can.
///
/// Deterministic: the same seed always produces the same statement, so the demo and any
/// screenshots stay reproducible.
/// </summary>
public static class TransactionGenerator
{
    const int Checking = 0, Savings = 1, Credit = 2;

    // Merchants the seeded corpus has never seen. These should still categorize, but at
    // visibly lower confidence than a merchant with a direct match.
    static readonly string[] UnseenGroceries  = ["BI-RITE MARKET", "RAINBOW GROCERY COOP", "MOLLIE STONES MKT"];
    static readonly string[] UnseenCoffee     = ["SIGHTGLASS COFFEE", "RITUAL COFFEE ROASTERS", "SQ *ANDYTOWN"];
    static readonly string[] UnseenRestaurant = ["SQ *NOPALITO", "ZUNI CAFE", "TST* SUPER DUPER BURGER", "LA TAQUERIA SF"];

    static readonly string[] Groceries  = ["SAFEWAY #0918", "TRADER JOES #452", "WHOLE FOODS MKT 10281", "COSTCO WHSE #1188"];
    static readonly string[] Coffee     = ["SQ *BLUE BOTTLE 9902", "STARBUCKS STORE #1174", "PEETS COFFEE 4408"];
    static readonly string[] Restaurant = ["CHIPOTLE 4471", "DOORDASH*THAI HOUSE", "UBER EATS", "TST* PIZZA HUT 8821"];
    static readonly string[] Gas        = ["SHELL OIL 118273645", "CHEVRON 00447182", "ARCO AM/PM 9931"];
    static readonly string[] Transit    = ["UBER *TRIP", "LYFT *RIDE", "BART CLIPPER", "SP * PARKING METER"];
    static readonly string[] Shopping   = ["AMAZON MKTPL XK4471QQ", "TARGET 00119288", "BEST BUY #884", "IKEA EMERYVILLE"];
    static readonly string[] Health     = ["CVS/PHARMACY #8821", "WALGREENS 44710", "ONE MEDICAL"];

    public static List<GeneratedTxn> Generate(DateOnly today, int months = 6, int seed = 20260904)
    {
        var rng = new Random(seed);
        var txns = new List<GeneratedTxn>();
        // Run through the CURRENT month, not up to it: a dashboard whose current month is
        // empty reads as broken. Dates after today are dropped below, so the newest month
        // is a realistic partial one.
        var start = today.AddMonths(-(months - 1));

        string Pick(string[] pool) => pool[rng.Next(pool.Length)];
        decimal Money(double lo, double hi) => Math.Round((decimal)(lo + rng.NextDouble() * (hi - lo)), 2);
        // Rotating merchant reference, e.g. "P0A1B2C3" — different every charge.
        string Ref() => string.Concat(Enumerable.Range(0, 8)
            .Select(_ => "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789"[rng.Next(34)]));

        for (var m = 0; m < months; m++)
        {
            var month = start.AddMonths(m);
            DateOnly On(int day) => new(month.Year, month.Month, Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)));

            // --- fixed monthly obligations ---
            txns.Add(new("GREYSTAR RENT PMT 4471", -2150.00m, On(1), Checking));
            txns.Add(new("PG&E ELECTRIC", -Money(78, 164), On(9), Checking));
            txns.Add(new("COMCAST XFINITY", -89.99m, On(12), Checking));
            txns.Add(new("AT&T WIRELESS", -75.00m, On(18), Checking));
            txns.Add(new("GEICO AUTO PMT", -128.44m, On(22), Checking));
            txns.Add(new("TRANSFER TO SAVINGS", 600.00m, On(16), Savings));

            // --- income, twice monthly ---
            txns.Add(new("ACME CORP PAYROLL", 3120.55m, On(15), Checking));
            txns.Add(new("ACME CORP PAYROLL", 3120.55m, On(DateTime.DaysInMonth(month.Year, month.Month)), Checking));

            // --- subscriptions. The first two rotate their reference code every charge,
            //     so exact string grouping sees six merchants where there are two. ---
            txns.Add(new($"NETFLIX.COM {Ref()}", -15.99m, On(4), Credit));
            txns.Add(new($"SPOTIFY USA {Ref()}", -11.99m, On(7), Credit));
            txns.Add(new("HULU 8773", -17.99m, On(11), Credit));
            txns.Add(new("PLANET FITNESS 021", -24.99m, On(3), Credit));
            txns.Add(new("NYTIMES DIGITAL SUB", -25.00m, On(19), Credit));

            // --- everyday spending ---
            for (var i = 0; i < 8; i++)
                txns.Add(new(rng.NextDouble() < .30 ? Pick(UnseenGroceries) : Pick(Groceries),
                             -Money(22, 168), On(rng.Next(1, 29)), Credit));
            for (var i = 0; i < 12; i++)
                txns.Add(new(rng.NextDouble() < .35 ? Pick(UnseenCoffee) : Pick(Coffee),
                             -Money(3.75, 9.50), On(rng.Next(1, 29)), Credit));
            for (var i = 0; i < 9; i++)
                txns.Add(new(rng.NextDouble() < .40 ? Pick(UnseenRestaurant) : Pick(Restaurant),
                             -Money(14, 82), On(rng.Next(1, 29)), Credit));
            for (var i = 0; i < 4; i++)
                txns.Add(new(Pick(Gas), -Money(38, 74), On(rng.Next(1, 29)), Credit));
            for (var i = 0; i < 6; i++)
                txns.Add(new(Pick(Transit), -Money(2.75, 34), On(rng.Next(1, 29)), Credit));
            for (var i = 0; i < 5; i++)
                txns.Add(new(Pick(Shopping), -Money(12, 240), On(rng.Next(1, 29)), Credit));
            if (rng.NextDouble() < .7)
                txns.Add(new(Pick(Health), -Money(11, 96), On(rng.Next(1, 29)), Credit));
        }

        PlantDuplicates(txns, rng);

        // No statement contains transactions that have not happened yet.
        return txns.Where(t => t.Date <= today)
                   .OrderBy(t => t.Date).ThenBy(t => t.Description).ToList();
    }

    /// <summary>Re-charges a handful of existing transactions on the same day for the same
    /// amount — an accidental double-swipe, which is what the detector should surface.</summary>
    static void PlantDuplicates(List<GeneratedTxn> txns, Random rng)
    {
        var candidates = txns.Where(t => t.Amount < 0 && t.Amount > -120).ToList();
        foreach (var original in candidates.OrderBy(_ => rng.Next()).Take(4))
            txns.Add(original);
    }
}
