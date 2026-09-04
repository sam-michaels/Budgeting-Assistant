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

// The pgvector type handler is registered on the data source, not through an EF plugin.
// Pgvector.EntityFrameworkCore has no EF Core 10 build (its latest targets net8.0 against
// Npgsql EF 9), so binding at the ADO.NET layer avoids coupling to the EF major version.
// Similarity search is issued as raw SQL by VectorSearch.
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
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
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
builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddMemoryCache();

// The LLM summary is optional. With a key, Claude writes it; without one, a deterministic
// template writes the same facts. Same endpoint, same response shape, no crash either way —
// so the build and the deployed demo never depend on a third-party key being present.
var anthropicKey = builder.Configuration["Anthropic:ApiKey"]
    ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
if (!string.IsNullOrWhiteSpace(anthropicKey))
{
    builder.Services.AddSingleton(new AnthropicClient { ApiKey = anthropicKey });
    builder.Services.AddScoped<IInsightWriter, ClaudeInsightWriter>();
}
else
{
    builder.Services.AddScoped<IInsightWriter, TemplateInsightWriter>();
}

var app = builder.Build();

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

app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapAdditionalIdentityEndpoints();
app.MapDemoLogin();

app.Run();
