using BudgetAssistant.Core.Abstractions;
using BudgetAssistant.Core.Analysis;
using BudgetAssistant.Core.Entities;
using BudgetAssistant.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BudgetAssistant.Web.Data.Seed;

/// <summary>
/// Populates an empty database with the labelled corpus and a demo account.
///
/// Transactions are seeded WITHOUT categories and then run through the real
/// classification pipeline, so the confidence scores on the dashboard are genuinely
/// inferred rather than copied from the generator. Duplicate and subscription flags come
/// from the real detectors for the same reason.
/// </summary>
public sealed class DatabaseSeeder(
    ApplicationDbContext db,
    IEmbedder embedder,
    TransactionCategorizer categorizer,
    TransactionFlagger flagger,
    UserManager<ApplicationUser> users,
    ILogger<DatabaseSeeder> log)
{
    public const string DemoEmail = "demo@budgetassistant.app";
    public const string DemoPassword = "Demo!2345";

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedCategoriesAndCorpusAsync(ct);
        await SeedDemoUserAsync(ct);
    }

    async Task SeedCategoriesAndCorpusAsync(CancellationToken ct)
    {
        if (!await db.Categories.AnyAsync(ct))
        {
            db.Categories.AddRange(SeedCorpus.Categories.Select(c => new Category { Name = c.Name, Color = c.Color }));
            await db.SaveChangesAsync(ct);
            log.LogInformation("Seeded {Count} categories", SeedCorpus.Categories.Length);
        }

        if (await db.MerchantExamples.AnyAsync(ct)) return;

        var byName = await db.Categories.ToDictionaryAsync(c => c.Name, c => c.Id, ct);
        var examples = SeedCorpus.Merchants
            .SelectMany(m => m.Examples.Select(text => new MerchantExample
            {
                Text = text,
                CategoryId = byName[m.Category],
                // Embed the NORMALIZED form: queries are normalized too, and comparing a
                // normalized query against a raw example would compare different things.
                Embedding = embedder.Embed(MerchantNormalizer.Normalize(text)),
            }))
            .ToList();

        db.MerchantExamples.AddRange(examples);
        await db.SaveChangesAsync(ct);
        log.LogInformation("Seeded {Count} labelled merchant examples", examples.Count);
    }

    async Task SeedDemoUserAsync(CancellationToken ct)
    {
        if (await users.FindByEmailAsync(DemoEmail) is not null) return;

        var user = new ApplicationUser { UserName = DemoEmail, Email = DemoEmail, EmailConfirmed = true };
        var created = await users.CreateAsync(user, DemoPassword);
        if (!created.Succeeded)
        {
            log.LogError("Could not create demo user: {Errors}",
                string.Join("; ", created.Errors.Select(e => e.Description)));
            return;
        }

        var accounts = new List<Account>
        {
            new() { UserId = user.Id, Name = "Everyday Checking", Type = AccountType.Checking },
            new() { UserId = user.Id, Name = "Emergency Savings", Type = AccountType.Savings },
            new() { UserId = user.Id, Name = "Rewards Credit",    Type = AccountType.Credit   },
        };
        db.Accounts.AddRange(accounts);
        await db.SaveChangesAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var generated = TransactionGenerator.Generate(today);

        // ponytail: classifies one transaction per round trip (~800 short queries, a few
        // seconds, once, on an empty database). Batch the embeddings and do a single
        // lateral-join query if seeding ever needs to be fast.
        var txns = new List<Transaction>(generated.Count);
        foreach (var g in generated)
        {
            var txn = new Transaction
            {
                AccountId = accounts[g.AccountIndex].Id,
                Amount = g.Amount,
                Date = g.Date,
                RawDescription = g.Description,
            };
            await categorizer.ClassifyAsync(txn, user.Id, ct);
            txns.Add(txn);
        }

        db.Transactions.AddRange(txns);
        await db.SaveChangesAsync(ct);

        await flagger.FlagAsync(user.Id, ct);
        await RecalculateBalancesAsync(ct);

        var categorized = txns.Count(t => t.CategoryId is not null);
        log.LogInformation(
            "Seeded demo user with {Total} transactions; {Categorized} auto-categorized ({Pct:P0}), {Unknown} left for review",
            txns.Count, categorized, (double)categorized / txns.Count, txns.Count - categorized);
    }

    async Task RecalculateBalancesAsync(CancellationToken ct)
    {
        foreach (var account in await db.Accounts.Include(a => a.Transactions).ToListAsync(ct))
            account.Balance = account.Transactions.Sum(t => t.Amount);
        await db.SaveChangesAsync(ct);
    }
}
