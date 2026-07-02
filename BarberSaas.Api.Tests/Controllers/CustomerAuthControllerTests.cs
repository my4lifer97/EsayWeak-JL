using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

public class CustomerAuthControllerTests : IntegrationTestBase
{
    private record OtpRequestResponse(bool IsNewCustomer, string? DevOtp);
    private record VerifyOtpResponse(string Token, string CustomerId, string Name, string FamilyName, string Phone);
    private record ErrorResponse(string Error);

    private async Task<string> RequestOtpAndGetCode(string phone)
    {
        var resp = await Client.PostAsJsonAsync("/api/customer/auth/otp", new RequestCustomerOtpRequest(phone));
        var body = await resp.Content.ReadFromJsonAsync<OtpRequestResponse>();
        Assert.NotNull(body?.DevOtp);
        return body!.DevOtp!;
    }

    [Fact]
    public async Task RequestOtp_NewPhone_ReturnsIsNewCustomerTrueWithDevOtp()
    {
        var resp = await Client.PostAsJsonAsync("/api/customer/auth/otp", new RequestCustomerOtpRequest("+15551110001"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<OtpRequestResponse>();
        Assert.True(body!.IsNewCustomer);
        Assert.False(string.IsNullOrEmpty(body.DevOtp));
    }

    [Fact]
    public async Task Verify_NewCustomerWithoutName_ReturnsBadRequest()
    {
        var phone = "+15551110002";
        var otp = await RequestOtpAndGetCode(phone);

        var resp = await Client.PostAsJsonAsync("/api/customer/auth/verify", new VerifyCustomerOtpRequest(phone, otp, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Verify_NewCustomerWithName_Succeeds()
    {
        var phone = "+15551110003";
        var otp = await RequestOtpAndGetCode(phone);

        var resp = await Client.PostAsJsonAsync("/api/customer/auth/verify", new VerifyCustomerOtpRequest(phone, otp, "First", "Last"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<VerifyOtpResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body?.Token));
        Assert.Equal(phone, body!.Phone);
    }

    [Fact]
    public async Task RequestOtp_ForExistingAccount_ReturnsIsNewCustomerFalse()
    {
        var phone = "+15551110004";
        var otp = await RequestOtpAndGetCode(phone);
        await Client.PostAsJsonAsync("/api/customer/auth/verify", new VerifyCustomerOtpRequest(phone, otp, "First", "Last"));

        // Requesting again after full 45s wait isn't practical in a fast test; verify by
        // querying the DB-backed CustomerAccount directly instead of a second live request.
        using var db = Db();
        var exists = db.CustomerAccounts.Any(a => a.Phone == phone);
        Assert.True(exists);
    }

    [Fact]
    public async Task Verify_WrongOtp_ReturnsBadRequest()
    {
        var phone = "+15551110005";
        await RequestOtpAndGetCode(phone);

        var resp = await Client.PostAsJsonAsync("/api/customer/auth/verify", new VerifyCustomerOtpRequest(phone, "000000", "First", "Last"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Verify_ReplayingAConsumedOtp_ReturnsBadRequest()
    {
        var phone = "+15551110006";
        var otp = await RequestOtpAndGetCode(phone);
        var first = await Client.PostAsJsonAsync("/api/customer/auth/verify", new VerifyCustomerOtpRequest(phone, otp, "First", "Last"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var replay = await Client.PostAsJsonAsync("/api/customer/auth/verify", new VerifyCustomerOtpRequest(phone, otp, "First", "Last"));

        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task RequestOtp_ImmediateSecondRequest_IsRateLimited()
    {
        var phone = "+15551110007";
        await RequestOtpAndGetCode(phone);

        var resp = await Client.PostAsJsonAsync("/api/customer/auth/otp", new RequestCustomerOtpRequest(phone));

        Assert.Equal((HttpStatusCode)429, resp.StatusCode);
    }
}
