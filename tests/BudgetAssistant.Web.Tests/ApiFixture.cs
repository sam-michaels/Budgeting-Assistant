using System.Security.Claims;
using System.Text.Encodings.Web;
using BudgetAssistant.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector.Npgsql;
using Testcontainers.PostgreSql;

namespace BudgetAssistant.Web.Tests;

/// <summary>
/// Boots the real application against a real Postgres. Nothing is substituted except the
/// authentication scheme: the point of these tests is that the query layer scopes data to
/// the caller, and an in-memory provider or a mocked DbContext would not prove that.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    // The image docker-compose.yml uses. pgvector has to be present for the migration's
    // CREATE EXTENSION to succeed, so the stock postgres image will not do.
    readonly PostgreSqlContainer _db = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();

    public WebApplicationFactory<Program> App { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();

        // Migrate BEFORE the host starts. Program.cs seeds during startup, which would
        // throw against a database that has no tables yet.
        //
        // Built through a service collection rather than `new ApplicationDbContext(...)`:
        // Identity reads its schema version from IdentityOptions while building the model,
        // so a context without those options configured produces a different model and EF
        // reports every migration as pending.
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(_db.GetConnectionString());
        dataSourceBuilder.UseVector();
        await using var dataSource = dataSourceBuilder.Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<IdentityOptions>(o => o.Stores.SchemaVersion = ApplicationDbContext.IdentitySchemaVersion);
        services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(dataSource, x => x.UseVector()));
        await using (var provider = services.BuildServiceProvider())
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();

        App = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:DefaultConnection", _db.GetConnectionString());
            // Pin the writer instead of letting `auto` probe: the probe is a second of
            // waiting per run, and a developer who happens to have Ollama running should
            // not get different tests from CI.
            b.UseSetting("Insights:Provider", "template");
            b.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning));
            b.ConfigureTestServices(s => s.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { }));
        });

        // Force startup now, so the seeding it does is not billed to the first test.
        App.CreateClient().Dispose();
    }

    public async Task DisposeAsync()
    {
        await App.DisposeAsync();
        await _db.DisposeAsync();
    }

    /// <summary>An HTTP client authenticated as <paramref name="userId"/>, or anonymous
    /// when it is null.</summary>
    public HttpClient ClientFor(string? userId)
    {
        var client = App.CreateClient();
        if (userId is not null) client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId);
        return client;
    }
}

/// <summary>
/// Authenticates whoever the <c>X-Test-User</c> header names.
///
/// Only the sign-in step is replaced. Authorization still runs, the controllers still read
/// the id from the principal, and a request with no header stays anonymous — so the
/// [Authorize] path is exercised for real rather than assumed.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var userId) || string.IsNullOrEmpty(userId))
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId!)], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
