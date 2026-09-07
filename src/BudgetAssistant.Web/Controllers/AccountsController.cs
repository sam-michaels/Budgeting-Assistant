using BudgetAssistant.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace BudgetAssistant.Web.Controllers;

public sealed class AccountsController(BudgetQueries queries) : BudgetControllerBase
{
    /// <summary>Every account belonging to the signed-in user, with its current balance.</summary>
    [HttpGet]
    [ProducesResponseType<List<AccountDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<AccountDto>>> Get(CancellationToken ct)
        => await queries.AccountsAsync(UserId, ct);
}
