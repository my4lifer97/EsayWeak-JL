using System.Net.Http.Headers;
using BarberSaas.Api.Data;

namespace BarberSaas.Api.Tests;

// Each test class gets its own factory (and therefore its own isolated InMemory database),
// since xUnit creates a fresh instance of the test class per [Fact].
public abstract class IntegrationTestBase : IDisposable
{
    protected readonly TestWebApplicationFactory Factory;
    protected readonly HttpClient Client;

    protected IntegrationTestBase()
    {
        Factory = new TestWebApplicationFactory();
        Client = Factory.CreateClient();
    }

    protected AppDbContext Db() => Factory.CreateDbContext();

    protected static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }
}
