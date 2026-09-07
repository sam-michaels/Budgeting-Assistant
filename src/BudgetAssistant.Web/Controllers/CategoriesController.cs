using BudgetAssistant.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetAssistant.Web.Controllers;

public record CategoryDto(int Id, string Name, string Color);

public sealed class CategoriesController(ApplicationDbContext db) : BudgetControllerBase
{
    /// <summary>The categories transactions can be assigned to.</summary>
    [HttpGet]
    [ProducesResponseType<List<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CategoryDto>>> Get(CancellationToken ct)
        => await db.Categories.OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Color)).ToListAsync(ct);
}
