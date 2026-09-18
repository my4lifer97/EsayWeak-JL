using BarberSaas.Api.Data;
using BarberSaas.Api.Services;
using BarberSaas.Api.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BarberSaas.Api.Tests;

// Development environment so AuthController/CustomerAuthController-adjacent flows expose devCode,
// matching real local-dev behavior; Program.cs's IsNpgsql() guard means the Development-only
// auto-migrate is skipped for this provider (schema is created directly below via EnsureCreated
// instead, since the real migrations' raw-SQL backfill is Postgres-specific and wouldn't run on
// SQLite).
//
// SQLite (not the EF InMemory provider) because CustomerAuthController.LoginWithWhatsApp uses
// ExecuteUpdateAsync, a relational-only bulk operation the InMemory provider can't execute.
// The connection is kept open for the factory's lifetime — SQLite's in-memory database is
// destroyed the moment its one connection closes.
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly bool _configureCardcom;
    private readonly bool _configureOpenAi;

    public const string JwtSecret = "test-jwt-signing-secret-at-least-32-chars-long";
    public const string JwtIssuer = "businessesaas-api-test";
    public const string JwtAudience = "businessesaas-frontend-test";
    public const string CronSecret = "test-cron-secret";
    public const string BridgeSecret = "test-bridge-secret";
    public const string CardcomTerminalNumber = "1000";
    public const string CardcomApiName = "test-api-name";
    public const string CardcomApiPassword = "test-api-password";
    public const string OpenAiApiKey = "test-openai-api-key";

    // configureCardcom/configureOpenAi: opt-in per test class (default false) so the existing
    // "not configured" tests (Cardcom 503, WhatsApp's rule-based fallback) keep exercising those
    // paths by default, while tests that need the real flow can request the relevant config via
    // ConfigureAppConfiguration below -- read lazily per-request (unlike Jwt:Secret), so this
    // timing is safe (see the ConfigureAppConfiguration note further down).
    public TestWebApplicationFactory(bool configureCardcom = false, bool configureOpenAi = false)
    {
        _configureCardcom = configureCardcom;
        _configureOpenAi = configureOpenAi;
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
        // Matches WhatsAppControllerTests' TwilioToken constant -- one platform-owned Twilio
        // account now signs/validates for every business (see TwilioWhatsAppSender), so this env var
        // is what WhatsAppController.Webhook checks inbound signatures against in tests.
        Environment.SetEnvironmentVariable("Twilio__AccountSid", "AC_test_sid");
        Environment.SetEnvironmentVariable("Twilio__AuthToken", "test_auth_token");
        // Shared secret WhatsAppController.BridgeInbound checks against -- matches
        // WhatsAppBridgeInboundTests' constant.
        Environment.SetEnvironmentVariable("WhatsAppBridge__Secret", BridgeSecret);
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

        if (_configureCardcom)
        {
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cardcom:TerminalNumber"] = CardcomTerminalNumber,
                ["Cardcom:ApiName"] = CardcomApiName,
                ["Cardcom:ApiPassword"] = CardcomApiPassword,
            }));
        }

        if (_configureOpenAi)
        {
            // Only set for tests that explicitly opt in (WhatsAppAiChatbotTests) -- every other
            // test leaves OpenAI:ApiKey unset, so WhatsAppController.ProcessMessageAsync's
            // dispatcher goes straight to the rule-based path, same as today.
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI:ApiKey"] = OpenAiApiKey,
            }));
        }

        builder.ConfigureServices(services =>
        {
            // AddDbContext registers its options-configuration delegate additively
            // (IDbContextOptionsConfiguration<T>), so removing only DbContextOptions<T>
            // leaves Program.cs's UseNpgsql delegate queued up alongside ours below,
            // producing "two database providers registered" at runtime. Strip both.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();

            services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(_connection));

            // Real Twilio calls would fail/hang in tests (no live creds) -- swap in a fake that
            // just records what would have been sent, so tests can assert on it directly.
            services.RemoveAll<IWhatsAppSender>();
            services.AddSingleton<IWhatsAppSender, FakeWhatsAppSender>();

            // Real whatsapp-bridge calls (outbound sends via BridgeWhatsAppSender, and the
            // platform-admin link/status/unlink endpoints) would dial out to a service that
            // doesn't exist in tests -- swap in a fake.
            services.RemoveAll<IWhatsAppBridgeClient>();
            services.AddSingleton<IWhatsAppBridgeClient, FakeWhatsAppBridgeClient>();

            // Real OpenAI calls would dial out to a real API -- swap in a fake, configurable per
            // test (canned tool call / canned text / throw). Registered regardless of
            // _configureOpenAi since WhatsAppController never invokes it unless OpenAI:ApiKey is
            // also set.
            services.RemoveAll<IOpenAiChatClient>();
            services.AddSingleton<IOpenAiChatClient, FakeOpenAiChatClient>();

            // Same idea for email -- replace whichever sender Program.cs picked (DevEmailSender
            // here, since no Smtp/Resend config) with one that records, so tests can assert that
            // e.g. an approved business owner was mailed their temp password.
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender, FakeEmailSender>();

            // Real Cardcom calls would dial out to the real gateway -- always use the fake
            // regardless of _configureCardcom, which only controls whether Cardcom:* config keys
            // are present (i.e. whether BillingController/CronController even attempt a call).
            services.RemoveAll<ICardcomService>();
            services.AddSingleton<ICardcomService, FakeCardcomService>();

            using var scope = services.BuildServiceProvider().CreateScope();
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        });
    }

    public AppDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }

    public FakeWhatsAppSender WhatsAppSender => (FakeWhatsAppSender)Services.GetRequiredService<IWhatsAppSender>();
    public FakeWhatsAppBridgeClient WhatsAppBridge => (FakeWhatsAppBridgeClient)Services.GetRequiredService<IWhatsAppBridgeClient>();
    public FakeOpenAiChatClient OpenAi => (FakeOpenAiChatClient)Services.GetRequiredService<IOpenAiChatClient>();
    public FakeCardcomService Cardcom => (FakeCardcomService)Services.GetRequiredService<ICardcomService>();
    public FakeEmailSender Email => (FakeEmailSender)Services.GetRequiredService<IEmailSender>();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
