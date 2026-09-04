namespace BudgetAssistant.Core.Entities;

public enum AccountType { Checking, Savings, Credit }

/// <summary>How a transaction's category was decided. Surfaced in the UI so the
/// classifier shows its work rather than acting as a black box.</summary>
public enum CategorySource { Uncategorized, Manual, VectorKnn }

public class Account
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public AccountType Type { get; set; }
    // decimal, never double: binary floating point cannot represent cents exactly.
    public decimal Balance { get; set; }
    public List<Transaction> Transactions { get; set; } = [];
}

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#888888";
}

/// <summary>A labelled merchant string used as training data for k-NN categorization.
/// Without a seeded corpus the classifier has nothing to vote on for a new user's
/// very first transaction.</summary>
public class MerchantExample
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    /// <summary>384-dim bge-micro-v2 embedding. Plain float[] keeps Core free of any
    /// database type; the DbContext converts it to a pgvector column.</summary>
    public float[]? Embedding { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
}

public class Transaction
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account? Account { get; set; }

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }

    /// <summary>Exactly as it appeared on the statement, e.g. "SQ *BLUE BOTTLE 4471".</summary>
    public string RawDescription { get; set; } = "";
    /// <summary>Output of <see cref="Analysis.MerchantNormalizer"/>; what actually gets embedded.</summary>
    public string NormalizedMerchant { get; set; } = "";

    /// <summary>384-dim embedding of <see cref="NormalizedMerchant"/>. See MerchantExample.Embedding.</summary>
    public float[]? Embedding { get; set; }

    public CategorySource CategorySource { get; set; } = CategorySource.Uncategorized;
    /// <summary>Share of the weighted k-NN vote won by the chosen category, 0..1.</summary>
    public float? CategoryConfidence { get; set; }

    public int? DuplicateOfId { get; set; }
    public bool IsSubscription { get; set; }
}
