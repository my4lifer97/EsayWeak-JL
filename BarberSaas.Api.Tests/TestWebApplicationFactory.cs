using BarberSaas.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BarberSaas.Api.Tests;

// Development environment so CustomerAuthController exposes devOtp, matching real local-dev
// behavior; Program.cs's IsNpgsql() guard means the Development-only auto-migrate is skipped
// for this provider (schema is created directly below via EnsureCreated instead, since the
// real migrations' raw-SQL backfill is Postgres-specific and wouldn't run on SQLite).
//
// SQLite (not the EF InMemory provider) because CustomerAuthController.VerifyOtp uses
// ExecuteUpdateAsync, a relational-only bulk operation the InMemory provider can't execute.
// The connection is kept open for the factory's lifetime — SQLite's in-memory database is
// destroyed the moment its one connection closes.
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public const string JwtSecret = "test-jwt-signing-secret-at-least-32-chars-long";
    public const string JwtIssuer = "barbersaas-api-test";
    public const string JwtAudience = "barbersaas-frontend-test";
    public const string CronSecret = "test-cron-secret";

    public TestWebApplicationFactory()
    {
        _connection.Open();

        // Program.cs reads Jwt:Secret into a plain variable at the top of its top-level
        // statements (for the JWT bearer signing key), before WebApplicationFactory's
        // ConfigureAppConfiguration hook gets a chance to run for minimal-API apps — so an
        // AddInMemoryCollection override here arrives too late and JwtService (which reads
        // IConfiguration live, per-request) ends up signing with a different secret than the
        // bearer middleware validates with. Environment variables are read synchronously by
        // WebApplicationBuilder.CreateBuilder() itself, before any of that, so they apply in time.
        Environment.SetEnvironmentVariable("Jwt__Secret", JwtSecret);
        Environment.SetEnvironmentVariable("Jwt__Issuer", JwtIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", JwtAudience);
        Environment.SetEnvironmentVariable("CronSecret", CronSecret);
        Environment.SetEnvironmentVariable("AllowedOrigin", "http://localhost:5173");
        Environment.SetEnvironmentVariable("AppUrl", "http://localhost:5173");

        // Force DevEmailSender regardless of the developer's local `dotnet user-secrets` store —
        // Development-environment user secrets share the same UserSecretsId as the real backend
        // project and get auto-loaded here too, so a locally-configured Resend:ApiKey would
        // otherwise leak into the test run and make Program.cs wire up the real ResendEmailSender,
        // which then fails for test-only addresses instead of returning a devCode.
        Environment.SetEnvironmentVariable("Resend__ApiKey", "");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            // AddDbContext registers its options-configuration delegate additively
            // (IDbContextOptionsConfiguration<T>), so removing only DbContextOptions<T>
            // leaves Program.cs's UseNpgsql delegate queued up alongside ours below,
            // producing "two database providers registered" at runtime. Strip both.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();

            services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(_connection));

            using var scope = services.BuildServiceProvider().CreateScope();
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        });
    }

    public AppDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
