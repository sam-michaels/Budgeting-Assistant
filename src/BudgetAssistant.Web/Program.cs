using BudgetAssistant.Web.Components;
using BudgetAssistant.Web.Components.Account;
using Anthropic;
using BudgetAssistant.Core.Abstractions;
using BudgetAssistant.Web.Data;
using BudgetAssistant.Web.Data.Seed;
using BudgetAssistant.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector.Npgsql;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Structured logging. Console only: on a container host stdout IS the log sink, and a
// rolling file inside an ephemeral container is a file nobody will ever read.
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// Two registrations, and both are needed. UseVector() on the data source teaches the
// ADO.NET layer to read and write the pgvector wire format; UseVector() on the EF options
// (below) is what gives EF a store mapping for the type, which a value converter alone
// cannot supply. Pgvector.EntityFrameworkCore 0.3.0 does work against EF Core 10 — an
// earlier note here claimed otherwise and was wrong.
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.UseVector();
var dataSource = dataSourceBuilder.Build();
builder.Services.AddSingleton(dataSource);

builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(dataSource, o => o.UseVector()));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        // No email infrastructure in this demo, so requiring confirmation would make
        // registration a dead end.
        options.SignIn.RequireConfirmedAccount = false;
        options.Stores.SchemaVersion = ApplicationDbContext.IdentitySchemaVersion;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// Singleton: the ONNX session and its model are expensive to construct and safe to share.
builder.Services.AddSingleton<IEmbedder, LocalTextEmbedder>();
builder.Services.AddScoped<VectorSearch>();
builder.Services.AddScoped<BudgetQueries>();
builder.Services.AddScoped<TransactionCategorizer>();
builder.Services.AddScoped<TransactionFlagger>();
builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddMemoryCache();

// Readiness, not liveness: the app is only useful if it can reach its database, and Fly
// uses this to decide whether a rolling machine should take traffic.
builder.Services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>();

// The insight summary is optional at every tier. A small model on Ollama writes it locally,
// DeepSeek-R1 (also local) is configured for analysis that needs to reason, and a
// deterministic template writes the same facts when nothing is running. Claude is wired up
// but never chosen automatically — set Insights:Provider to "claude" for work that outgrows
// a local model.
//
// Same endpoint, same response shape, no crash on any path, so neither the build nor the
// deployed demo depends on a model or a third-party key being present.
var insights = builder.Configuration.GetSection("Insights");
var ollamaUrl = insights["Ollama:BaseUrl"] ?? "http://localhost:11434";
var anthropicKey = builder.Configuration["Anthropic:ApiKey"]
    ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

builder.Services.AddHttpClient("ollama", c =>
{
    c.BaseAddress = new Uri(ollamaUrl);
    // Generous: a reasoning model on consumer hardware is slow, and the dashboard already
    // renders its figures before the prose arrives, so a long wait costs nothing but prose.
    c.Timeout = TimeSpan.FromSeconds(120);
});

var provider = insights["Provider"] ?? "auto";
if (provider.Equals("auto", StringComparison.OrdinalIgnoreCase))
{
    // ponytail: reachability is probed once, here. Starting Ollama afterwards needs an app
    // restart; set Insights:Provider="ollama" to bind to it regardless and skip the probe.
    var ollamaUp = false;
    try
    {
        using var probe = new HttpClient { BaseAddress = new Uri(ollamaUrl), Timeout = TimeSpan.FromSeconds(1) };
        ollamaUp = (await probe.GetAsync("/api/tags")).IsSuccessStatusCode;
    }
    catch { /* Not running. Fall through the ladder. */ }

    provider = ollamaUp ? "ollama"
        : !string.IsNullOrWhiteSpace(anthropicKey) ? "claude"
        : "template";
}

switch (provider.ToLowerInvariant())
{
    case "ollama":
        builder.Services.AddScoped<IInsightWriter>(sp => new OllamaInsightWriter(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("ollama"),
            insights["Ollama:SummaryModel"] ?? "llama3.2:3b",
            sp.GetRequiredService<ILogger<OllamaInsightWriter>>()));
        break;

    case "claude" when !string.IsNullOrWhiteSpace(anthropicKey):
        builder.Services.AddSingleton(new AnthropicClient { ApiKey = anthropicKey });
        builder.Services.AddScoped<IInsightWriter>(sp => new ClaudeInsightWriter(
            sp.GetRequiredService<AnthropicClient>(),
            insights["Claude:Model"] ?? "claude-opus-5",
            sp.GetRequiredService<ILogger<ClaudeInsightWriter>>()));
        break;

    default:
        builder.Services.AddScoped<IInsightWriter, TemplateInsightWriter>();
        provider = "template";
        break;
}

var app = builder.Build();

app.Logger.LogInformation("Insight writer: {Provider}", provider);

// `dotnet BudgetAssistant.Web.dll --migrate` applies migrations and exits, so a release
// pipeline can run schema changes as their own reviewable step against the same image
// that will serve traffic. Migrations are never applied implicitly on startup: an app
// instance rolling out should not silently alter a production schema.
if (args.Contains("--migrate"))
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    var db = migrationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    app.Logger.LogInformation("Applying migrations…");
    await db.Database.MigrateAsync();
    app.Logger.LogInformation("Migrations applied.");
    return;
}

// Seeding is idempotent and only fills an empty database.
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

// Anonymous: a health check that needs a login is a health check that always fails.
app.MapHealthChecks("/health").AllowAnonymous();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapAdditionalIdentityEndpoints();
app.MapDemoLogin();

app.Run();

/// <summary>
/// Top-level statements compile to an internal `Program`, which
/// <c>WebApplicationFactory&lt;Program&gt;</c> cannot name. This makes it public without
/// otherwise changing it. It exists for the integration tests and nothing else.
/// </summary>
public partial class Program;
