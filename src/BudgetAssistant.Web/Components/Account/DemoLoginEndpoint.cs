using BudgetAssistant.Web.Data;
using BudgetAssistant.Web.Data.Seed;
using Microsoft.AspNetCore.Identity;

namespace BudgetAssistant.Web.Components.Account;

public static class DemoLoginEndpoint
{
    /// <summary>
    /// Signs a visitor into the seeded read-only demo account in one click.
    ///
    /// A login wall is where a reviewer's interest ends, and this app exists to be looked
    /// at. The endpoint is deliberately narrow: it authenticates one specific known
    /// account and ignores any input, so it cannot be used to reach anyone else's data.
    /// </summary>
    public static IEndpointConventionBuilder MapDemoLogin(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/Account/DemoLogin", async (
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ILoggerFactory loggerFactory) =>
        {
            var log = loggerFactory.CreateLogger("DemoLogin");
            var demo = await userManager.FindByEmailAsync(DatabaseSeeder.DemoEmail);
            if (demo is null)
            {
                log.LogWarning("Demo login attempted but the demo account has not been seeded");
                return Results.Redirect("/Account/Login?demoUnavailable=true");
            }

            await signInManager.SignInAsync(demo, isPersistent: false);
            log.LogInformation("Visitor signed in to the demo account");
            return Results.Redirect("/");
        });
    }
}
