using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BudgetAssistant.Web.Controllers;

/// <summary>
/// Every endpoint is scoped to the signed-in user. Taking the id from the authenticated
/// principal rather than a route or query parameter means one user's data cannot be read
/// by asking for another user's id.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
[Produces("application/json")]
public abstract class BudgetControllerBase : ControllerBase
{
    protected string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Authenticated request has no user id claim.");
}
