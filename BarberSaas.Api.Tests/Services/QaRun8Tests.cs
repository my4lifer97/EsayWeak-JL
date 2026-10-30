using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Services;
using Xunit;

namespace BarberSaas.Api.Tests.Services;

// QA run 8 fixes that come down to one endpoint or helper.
public class QaRun8Tests : IntegrationTestBase
{
    // SEC-20: MailAddress also parses display forms, which were stored as typed.
    [Theory]
    [InlineData("<a@b.com>", false)]
    [InlineData("a(x)@b.com", false)]
    [InlineData("\"a\"@b.com", false)]
    [InlineData("a@b..com", false)]
    [InlineData("dana.cohen+shop@example.co.il", true)]
    public void EmailAddress_PlainAddressOnly(string email, bool valid)
    {
        Assert.Equal(valid, EmailAddress.IsValid(email));
    }

    // PUB-30 / PUB-33
    [Theory]
    [InlineData("‪+972 50-123-4567‬", "+972501234567")]
    [InlineData("‎050-123-4567", "+972501234567")]
    [InlineData("972 050 123 4567", "+972501234567")]
    public void Phone_PastedWithMarks_OrLocalZeroAfterTheCountryCode(string input, string expected)
    {
        Assert.True(PhoneNormalizer.IsValid(input));
        Assert.Equal(expected, PhoneNormalizer.Normalize(input));
    }

    // OWN-36
    [Theory]
    [InlineData("  Dana\nCohen ", "Dana Cohen")]
    [InlineData("​​", "")]
    [InlineData("‮Dana", "Dana")]
    [InlineData("می‌نا", "می‌نا")]
    public void TextClean_Name(string input, string expected)
    {
        Assert.Equal(expected, TextClean.Name(input));
    }

    // CHAT-31 / CHAT-32
    [Theory]
    [InlineData("‍‌", "file")]
    [InlineData("⠀﷐.pdf", ".pdf")]
    [InlineData("a.pdf.", "a.pdf")]
    [InlineData(". .", "file")]
    public void SafeFileName_InvisibleAndTrailingDots(string input, string expected)
    {
        Assert.Equal(expected, ChatFiles.SafeFileName(input));
    }

    // PA-20: "All" is the documented way to list everything.
    [Fact]
    public async Task OwnerRequests_StatusAll_ListsEverything_AndAListIsRefused()
    {
        var bootstrap = await Client.PostAsJsonAsync("/api/platform-admin/bootstrap",
            new PlatformAdminBootstrapRequest("qa8-admin@example.com", "supersecret123", "Owner"));
        var token = (await bootstrap.Content.ReadFromJsonAsync<PlatformAdminLoginResponse>())!.Token;
        Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var all = await Client.GetAsync("/api/platform-admin/business-owner-requests?status=All");
        var two = await Client.GetAsync("/api/platform-admin/business-owner-requests?status=Pending,Approved");

        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, two.StatusCode);
    }
}
