using System.Net;
using System.Net.Http.Json;
using BudgetAssistant.Core.Entities;
using BudgetAssistant.Web.Data;
using BudgetAssistant.Web.Data.Seed;
using BudgetAssistant.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BudgetAssistant.Web.Tests;

/// <summary>
/// The README claims no user can read another's data by guessing an id. These tests are
/// what makes that a claim about the code rather than about the author's intentions.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class ApiSecurityTests(ApiFixture fixture)
{
    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        using var client = fixture.ClientFor(null);

        var response = await client.GetAsync("/api/transactions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task One_users_transactions_are_invisible_to_another()
    {
        var (mallory, _, _) = await NewUserWithTransactionAsync("MALLORY PRIVATE CHARGE", -42.00m);
        var demo = await DemoUserIdAsync();

        using var asDemo = fixture.ClientFor(demo);
        using var asMallory = fixture.ClientFor(mallory);

        var demoSees = await asDemo.GetFromJsonAsync<List<TransactionDto>>(
            "/api/transactions?search=MALLORY PRIVATE CHARGE");
        var mallorySees = await asMallory.GetFromJsonAsync<List<TransactionDto>>(
            "/api/transactions?search=MALLORY PRIVATE CHARGE");

        Assert.Empty(demoSees!);
        Assert.Single(mallorySees!);
    }

    [Fact]
    public async Task Accounts_are_scoped_to_the_caller()
    {
        var (mallory, accountId, _) = await NewUserWithTransactionAsync("MALLORY ACCOUNT PROBE", -7.00m);

        using var client = fixture.ClientFor(mallory);
        var accounts = await client.GetFromJsonAsync<List<AccountDto>>("/api/accounts");

        Assert.Equal([accountId], accounts!.Select(a => a.Id));
    }

    [Fact]
    public async Task Recategorizing_someone_elses_transaction_is_not_found()
    {
        var (_, _, transactionId) = await NewUserWithTransactionAsync("MALLORY UNTOUCHABLE", -19.00m);
        var demo = await DemoUserIdAsync();

        using var asDemo = fixture.ClientFor(demo);
        var response = await asDemo.PutAsJsonAsync(
            $"/api/transactions/{transactionId}/category", new { CategoryId = 1 });

        // 404 rather than 403: whether that id exists at all is not the caller's business.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var scope = Scope();
        var unchanged = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Transactions.AsNoTracking().FirstAsync(t => t.Id == transactionId);
        Assert.NotEqual(CategorySource.Manual, unchanged.CategorySource);
    }

    /// <summary>
    /// Regression lock. Both detectors once ran only during seeding, so a transaction
    /// created through the API was never compared against anything and came back unflagged.
    /// </summary>
    [Fact]
    public async Task Posting_a_repeat_charge_flags_it_as_a_duplicate()
    {
        var (user, accountId, _) = await NewUserWithTransactionAsync("DOUBLE BILLED DINER", -31.50m);
        using var client = fixture.ClientFor(user);

        var response = await client.PostAsJsonAsync("/api/transactions", new
        {
            AccountId = accountId,
            Date = new DateOnly(2026, 5, 3),
            Description = "DOUBLE BILLED DINER",
            Amount = -31.50m,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TransactionDto>();
        Assert.True(created!.IsDuplicate, "the repeat charge should have been flagged against the first one");
    }

    // ---- helpers ----

    AsyncServiceScope Scope() => fixture.App.Services.CreateAsyncScope();

    async Task<string> DemoUserIdAsync()
    {
        await using var scope = Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var demo = await users.FindByEmailAsync(DatabaseSeeder.DemoEmail);
        Assert.NotNull(demo);
        return demo!.Id;
    }

    /// <summary>A fresh user owning one account and one classified transaction.</summary>
    async Task<(string UserId, int AccountId, int TransactionId)> NewUserWithTransactionAsync(
        string description, decimal amount)
    {
        await using var scope = Scope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser { UserName = $"u{Guid.NewGuid():N}@test.local" };
        user.Email = user.UserName;
        Assert.True((await users.CreateAsync(user, "Test!2345")).Succeeded);

        var account = new Account { UserId = user.Id, Name = "Test Checking", Type = AccountType.Checking };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        // Classified through the real pipeline so it carries an embedding — the detectors
        // compare vectors, so a transaction without one is invisible to them.
        var txn = new Transaction
        {
            AccountId = account.Id,
            Date = new DateOnly(2026, 5, 1),
            RawDescription = description,
            Amount = amount,
        };
        await sp.GetRequiredService<TransactionCategorizer>().ClassifyAsync(txn, user.Id);
        db.Transactions.Add(txn);
        await db.SaveChangesAsync();

        return (user.Id, account.Id, txn.Id);
    }
}
