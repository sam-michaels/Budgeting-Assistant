using BudgetAssistant.Core.Entities;
using BudgetAssistant.Web.Data;
using BudgetAssistant.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetAssistant.Web.Controllers;

public record CreateTransactionRequest(int AccountId, DateOnly Date, string Description, decimal Amount);
public record RecategorizeRequest(int CategoryId);

public sealed class TransactionsController(
    ApplicationDbContext db,
    BudgetQueries queries,
    TransactionCategorizer categorizer,
    TransactionFlagger flagger,
    ILogger<TransactionsController> log) : BudgetControllerBase
{
    /// <summary>Transactions for the signed-in user, newest first.</summary>
    /// <param name="search">Case-insensitive match against the raw statement description.</param>
    /// <param name="onlyFlagged">Return only duplicates and uncategorized transactions.</param>
    [HttpGet]
    [ProducesResponseType<List<TransactionDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<TransactionDto>>> Get(
        [FromQuery] string? search, [FromQuery] bool onlyFlagged = false,
        [FromQuery] int take = 100, [FromQuery] int skip = 0, CancellationToken ct = default)
        => await queries.TransactionsAsync(UserId, Math.Clamp(take, 1, 500), Math.Max(skip, 0), search, onlyFlagged, ct);

    /// <summary>Records a transaction and classifies it from its description.</summary>
    [HttpPost]
    [ProducesResponseType<TransactionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TransactionDto>> Create(CreateTransactionRequest request, CancellationToken ct)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.AccountId && a.UserId == UserId, ct);
        if (account is null) return BadRequest($"Account {request.AccountId} not found.");
        if (string.IsNullOrWhiteSpace(request.Description)) return BadRequest("Description is required.");

        var txn = new Transaction
        {
            AccountId = account.Id,
            Date = request.Date,
            RawDescription = request.Description.Trim(),
            Amount = request.Amount,
        };
        await categorizer.ClassifyAsync(txn, UserId, ct);

        db.Transactions.Add(txn);
        account.Balance += txn.Amount;
        await db.SaveChangesAsync(ct);

        // A charge is only a duplicate relative to another charge, so the flags are
        // recomputed against the user's history once the new row is in it.
        await flagger.FlagAsync(UserId, ct);

        log.LogInformation("Created transaction {Id} for {Amount}, categorized as {Category} at {Confidence:P0}",
            txn.Id, txn.Amount, txn.CategoryId, txn.CategoryConfidence ?? 0);

        var dto = (await queries.TransactionsAsync(UserId, take: 1, ct: ct)).FirstOrDefault();
        return CreatedAtAction(nameof(Get), new { }, dto);
    }

    /// <summary>Corrects a category by hand. The correction is stored as a labelled example,
    /// so similar transactions classify better afterwards.</summary>
    [HttpPut("{id:int}/category")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Recategorize(int id, RecategorizeRequest request, CancellationToken ct)
    {
        var txn = await queries.ForUser(UserId).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (txn is null) return NotFound();
        if (!await db.Categories.AnyAsync(c => c.Id == request.CategoryId, ct))
            return BadRequest($"Category {request.CategoryId} not found.");

        txn.CategoryId = request.CategoryId;
        txn.CategorySource = CategorySource.Manual;
        txn.CategoryConfidence = null;
        txn.CategoryMatchedOn = null;
        await db.SaveChangesAsync(ct);

        log.LogInformation("Transaction {Id} manually recategorized to {Category}", id, request.CategoryId);
        return NoContent();
    }
}
