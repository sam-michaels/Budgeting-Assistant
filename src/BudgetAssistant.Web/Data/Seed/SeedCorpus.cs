namespace BudgetAssistant.Web.Data.Seed;

/// <summary>
/// Labelled merchant strings used as k-NN training data. Without this a brand new user's
/// first transaction has no neighbours to vote on, so every prediction would be a miss.
///
/// Entries are written the way a real statement writes them, because that is what gets
/// embedded at query time.
/// </summary>
public static class SeedCorpus
{
    /// <summary>Assigned by rule rather than by vote, so it has no entry in
    /// <see cref="Merchants"/>: seeding transfer examples would only give the classifier a
    /// way to pull real merchants into it. See <see cref="Core.Analysis.TransferDetector"/>.</summary>
    public const string TransferCategory = "Transfer";

    public static readonly (string Name, string Color)[] Categories =
    [
        ("Groceries",     "#4C9F70"), ("Coffee",        "#B07D48"),
        ("Restaurants",   "#D2694A"), ("Gas",           "#7A6FF0"),
        ("Transit",       "#5B8DEF"), ("Utilities",     "#6C8A9A"),
        ("Housing",       "#8E6BB5"), ("Streaming",     "#D14D72"),
        ("Fitness",       "#3FA796"), ("Shopping",      "#E0A458"),
        ("Healthcare",    "#5AA9C9"), ("Insurance",     "#8593A8"),
        ("Travel",        "#3D9BD1"), ("Entertainment", "#C0679B"),
        ("Income",        "#2E9E5B"), (TransferCategory, "#6B7A8F"),
    ];

    public static readonly (string Category, string[] Examples)[] Merchants =
    [
        ("Groceries", ["SAFEWAY #1234", "TRADER JOES 178", "WHOLE FOODS MKT", "KROGER 4471",
                       "ALDI 62910", "COSTCO WHSE #0421", "SPROUTS FARMERS MKT", "PUBLIX SUPER MARKET"]),
        ("Coffee", ["BLUE BOTTLE COFFEE", "SQ *BLUE BOTTLE 4471", "STARBUCKS STORE #8842",
                    "PEETS COFFEE 0192", "DUNKIN #339021", "SQ *LOCAL GROUNDS", "PHILZ COFFEE"]),
        ("Restaurants", ["TST* PIZZA HUT", "CHIPOTLE 1882", "SQ *TAQUERIA LUNA", "DOORDASH*WENDYS",
                         "UBER EATS", "GRUBHUB*THAI BASIL", "OLIVE GARDEN 7712", "SHAKE SHACK 0034"]),
        ("Gas", ["SHELL OIL 574839201", "CHEVRON 00923845", "EXXONMOBIL 8847", "BP#9928371",
                 "ARCO AM/PM 4471", "76 GAS STATION 221"]),
        ("Transit", ["UBER *TRIP", "LYFT *RIDE", "BART CLIPPER", "MTA*METROCARD",
                     "SP PARKING METER", "CITY PARKING GARAGE"]),
        ("Utilities", ["PG&E ELECTRIC", "COMCAST XFINITY", "AT&T WIRELESS", "VERIZON WIRELESS",
                       "CITY WATER DEPT", "WASTE MANAGEMENT"]),
        ("Housing", ["GREYSTAR RENT PMT", "ZILLOW RENTAL PMT", "HOA MONTHLY DUES",
                     "PROPERTY MGMT LLC", "MORTGAGE PMT WELLS"]),
        ("Streaming", ["NETFLIX.COM", "SPOTIFY USA", "HULU 8773", "DISNEY PLUS",
                       "MAX.COM SUBSCRIPTION", "YOUTUBE PREMIUM", "APPLE.COM/BILL"]),
        ("Fitness", ["PLANET FITNESS 021", "EQUINOX SF", "SQ *YOGA STUDIO", "PELOTON MEMBERSHIP",
                     "24 HOUR FITNESS 88"]),
        ("Shopping", ["AMAZON MKTPL XXXX9931", "TARGET 00021882", "BEST BUY #221",
                      "IKEA PALO ALTO", "ETSY.COM ORDER", "NORDSTROM 0192", "WALMART 3391"]),
        ("Healthcare", ["CVS/PHARMACY #4471", "WALGREENS 09182", "ONE MEDICAL",
                        "QUEST DIAGNOSTICS", "KAISER PERMANENTE"]),
        ("Insurance", ["GEICO AUTO PMT", "STATE FARM INS", "PROGRESSIVE INS",
                       "LEMONADE RENTERS", "BLUE SHIELD PREM"]),
        ("Travel", ["UNITED AIRLINES 016", "DELTA AIR LINES", "MARRIOTT HOTELS",
                    "AIRBNB * HMX8821", "HERTZ RENT A CAR", "EXPEDIA*FLIGHT"]),
        ("Entertainment", ["AMC THEATRES #440", "STEAM GAMES", "TICKETMASTER",
                           "SQ *COMEDY CELLAR", "EVENTBRITE*SHOW"]),
        ("Income", ["ACME CORP PAYROLL", "DIRECT DEP PAYROLL", "IRS TREAS TAX REF",
                    "VENMO CASHOUT", "INTEREST PAYMENT"]),
    ];
}
