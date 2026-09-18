using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

public class CustomerAuthControllerTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);
    private record ItemIdDto(string Id);
    private record ErrorResponse(string Error);

    private async Task<(string BusinessId, string Slug, string ItemId)> SeedBusinessAndService(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));
        var businessBody = await verify.Content.ReadFromJsonAsync<LoginResponse>();

        Authorize(Client, businessBody!.Token);
        var svcResp = await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Haircut", "Haircut", "Haircut", 30, 50m));
        var svc = await svcResp.Content.ReadFromJsonAsync<ItemIdDto>();
        Client.DefaultRequestHeaders.Authorization = null;

        using var db = Db();
        var businessId = await db.Businesses.Where(b => b.Slug == slug).Select(b => b.Id).FirstAsync();
        return (businessId, slug, svc!.Id);
    }

    private async Task<string> CreateBookingToken(string businessId, string itemId, string phone, string? profileName = null)
    {
        using var scope = Factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<WhatsAppBookingTokenService>();
        var token = await tokens.CreateAsync(businessId, itemId, phone, profileName);
        return token.Id;
    }

    [Fact]
    public async Task LoginWithWhatsApp_ValidToken_NewCustomer_ReturnsSessionAndSplitsProfileName()
    {
        var (businessId, slug, itemId) = await SeedBusinessAndService("wa-login-1@example.com", "wa-login-1");
        var token = await CreateBookingToken(businessId, itemId, "+15557770001", "Jane Doe");

        var resp = await Client.PostAsJsonAsync("/api/customer/auth/whatsapp", new WhatsAppLoginRequest(token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<WhatsAppLoginResult>();
        Assert.False(string.IsNullOrWhiteSpace(body?.Token));
        Assert.Equal("Jane", body!.Name);
        Assert.Equal("Doe", body.FamilyName);
        Assert.Equal(slug, body.BusinessSlug);
        Assert.Equal(itemId, body.ItemId);
    }

    [Fact]
    public async Task LoginWithWhatsApp_NoProfileName_FallsBackToGenericName()
    {
        var (businessId, _, itemId) = await SeedBusinessAndService("wa-login-2@example.com", "wa-login-2");
        var token = await CreateBookingToken(businessId, itemId, "+15557770002", null);

        var resp = await Client.PostAsJsonAsync("/api/customer/auth/whatsapp", new WhatsAppLoginRequest(token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<WhatsAppLoginResult>();
        Assert.Equal("Customer", body!.Name);
        Assert.Equal("", body.FamilyName);
    }

    [Fact]
    public async Task LoginWithWhatsApp_ExistingAccount_ReusesIt()
    {
        var (businessId, _, itemId) = await SeedBusinessAndService("wa-login-3@example.com", "wa-login-3");
        var phone = "+15557770003";
        var firstToken = await CreateBookingToken(businessId, itemId, phone, "Jane Doe");
        var first = await Client.PostAsJsonAsync("/api/customer/auth/whatsapp", new WhatsAppLoginRequest(firstToken));
        var firstBody = await first.Content.ReadFromJsonAsync<WhatsAppLoginResult>();

        // A second, unrelated token for the same phone (e.g. picking a different service later)
        // must resolve to the same CustomerAccount, not create a duplicate.
        var secondToken = await CreateBookingToken(businessId, itemId, phone, "Someone Else");
        var second = await Client.PostAsJsonAsync("/api/customer/auth/whatsapp", new WhatsAppLoginRequest(secondToken));
        var secondBody = await second.Content.ReadFromJsonAsync<WhatsAppLoginResult>();

        Assert.Equal(firstBody!.CustomerId, secondBody!.CustomerId);
        Assert.Equal("Jane", secondBody.Name); // unchanged by the second (unused) profile name
    }

    [Fact]
    public async Task LoginWithWhatsApp_RemembersACorrectionMadeWhenBookingWithThisBusiness()
    {
        // Regression: the account's own Name/FamilyName is only ever auto-filled from a WhatsApp
        // profile name once, on the account's first-ever creation, and is never updated after
        // that -- a customer whose WhatsApp display name has no second word (like "Luna" here)
        // gets an empty FamilyName on the account forever. A correction they type when actually
        // booking with a business (BookingController always saves it onto the per-business
        // Customer row) must be what a later WhatsApp login for that same business returns, not
        // the account's still-empty one.
        var (businessId, _, itemId) = await SeedBusinessAndService("wa-login-5@example.com", "wa-login-5");
        var phone = "+15557770005";
        var firstToken = await CreateBookingToken(businessId, itemId, phone, "Luna");
        var first = await Client.PostAsJsonAsync("/api/customer/auth/whatsapp", new WhatsAppLoginRequest(firstToken));
        var firstBody = await first.Content.ReadFromJsonAsync<WhatsAppLoginResult>();
        Assert.Equal("Luna", firstBody!.Name);
        Assert.Equal("", firstBody.FamilyName);

        using (var db = Db())
        {
            db.Customers.Add(new Customer { BusinessId = businessId, Phone = phone, Name = "Luna", FamilyName = "Khwaja" });
            await db.SaveChangesAsync();
        }

        var secondToken = await CreateBookingToken(businessId, itemId, phone, "Luna");
        var second = await Client.PostAsJsonAsync("/api/customer/auth/whatsapp", new WhatsAppLoginRequest(secondToken));
        var secondBody = await second.Content.ReadFromJsonAsync<WhatsAppLoginResult>();

        Assert.Equal("Khwaja", secondBody!.FamilyName);
    }

    [Fact]
    public async Task LoginWithWhatsApp_ReusableWithinWindow_BothRequestsSucceed()
    {
        var (businessId, _, itemId) = await SeedBusinessAndService("wa-login-4@example.com", "wa-login-4");
        var token = await CreateBookingToken(businessId, itemId, "+15557770004", "Jane Doe");

        var first = await Client.PostAsJsonAsync("/api/customer/auth/whatsapp", new WhatsAppLoginRequest(token));
        var second = await Client.PostAsJsonAsync("/api/customer/auth/whatsapp", new WhatsAppLoginRequest(token));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task LoginWithWhatsApp_ExpiredToken_ReturnsBadRequest()
    {
        var (businessId, _, itemId) = await SeedBusinessAndService("wa-login-5@example.com", "wa-login-5");
        var token = await CreateBookingToken(businessId, itemId, "+15557770005", "Jane Doe");

        using (var db = Db())
        {
            var row = await db.WhatsAppBookingTokens.FirstAsync(t => t.Id == token);
            row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var resp = await Client.PostAsJsonAsync("/api/customer/auth/whatsapp", new WhatsAppLoginRequest(token));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task LoginWithWhatsApp_UnknownToken_ReturnsBadRequest()
    {
        var resp = await Client.PostAsJsonAsync("/api/customer/auth/whatsapp", new WhatsAppLoginRequest("not-a-real-token"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task LoginWithWhatsApp_ServiceDeactivatedSinceLinkSent_ReturnsNotFound()
    {
        // Services are soft-deleted (IsActive = false) by the business, never hard-deleted --
        // this is the realistic "service no longer bookable" case, not a hard row delete.
        var (businessId, _, itemId) = await SeedBusinessAndService("wa-login-6@example.com", "wa-login-6");
        var token = await CreateBookingToken(businessId, itemId, "+15557770006", "Jane Doe");

        using (var db = Db())
        {
            var item = await db.Items.FirstAsync(s => s.Id == itemId);
            item.IsActive = false;
            await db.SaveChangesAsync();
        }

        var resp = await Client.PostAsJsonAsync("/api/customer/auth/whatsapp", new WhatsAppLoginRequest(token));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ─── Phone+OTP (second, parallel entry point alongside WhatsApp) ──────────

    private record RequestOtpResponse(bool IsNewCustomer, string? DevOtp);
    private record VerifyOtpResponse(string Token, string CustomerId, string Name, string FamilyName, string Phone);

    [Fact]
    public async Task RequestOtp_NewPhone_ReturnsIsNewCustomerTrueWithDevCode()
    {
        var resp = await Client.PostAsJsonAsync("/api/customer/auth/otp", new RequestCustomerOtpRequest("+15558880001"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<RequestOtpResponse>();
        Assert.True(body!.IsNewCustomer);
        Assert.False(string.IsNullOrWhiteSpace(body.DevOtp)); // Development env -- see AuthController's devCode convention
    }

    [Fact]
    public async Task RequestOtp_CalledTwiceImmediately_SecondCallIsRateLimited()
    {
        var phone = "+15558880002";
        await Client.PostAsJsonAsync("/api/customer/auth/otp", new RequestCustomerOtpRequest(phone));
        var second = await Client.PostAsJsonAsync("/api/customer/auth/otp", new RequestCustomerOtpRequest(phone));

        Assert.Equal((HttpStatusCode)429, second.StatusCode);
    }

    [Fact]
    public async Task VerifyOtp_NewCustomer_MissingName_ReturnsBadRequest()
    {
        var phone = "+15558880003";
        var request = await Client.PostAsJsonAsync("/api/customer/auth/otp", new RequestCustomerOtpRequest(phone));
        var code = (await request.Content.ReadFromJsonAsync<RequestOtpResponse>())!.DevOtp!;

        var resp = await Client.PostAsJsonAsync("/api/customer/auth/verify", new VerifyCustomerOtpRequest(phone, code, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task VerifyOtp_NewCustomer_ValidCode_CreatesAccountAndReturnsSession()
    {
        var phone = "+15558880004";
        var request = await Client.PostAsJsonAsync("/api/customer/auth/otp", new RequestCustomerOtpRequest(phone));
        var code = (await request.Content.ReadFromJsonAsync<RequestOtpResponse>())!.DevOtp!;

        var resp = await Client.PostAsJsonAsync("/api/customer/auth/verify", new VerifyCustomerOtpRequest(phone, code, "First", "Last"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<VerifyOtpResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));
        Assert.Equal("First", body.Name);
        Assert.Equal("Last", body.FamilyName);
    }

    [Fact]
    public async Task VerifyOtp_WrongCode_ReturnsBadRequest()
    {
        var phone = "+15558880005";
        await Client.PostAsJsonAsync("/api/customer/auth/otp", new RequestCustomerOtpRequest(phone));

        var resp = await Client.PostAsJsonAsync("/api/customer/auth/verify", new VerifyCustomerOtpRequest(phone, "000000", "First", "Last"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task VerifyOtp_ExistingAccount_ReusesItAndIgnoresNameArgs()
    {
        var phone = "+15558880006";
        var firstRequest = await Client.PostAsJsonAsync("/api/customer/auth/otp", new RequestCustomerOtpRequest(phone));
        var firstCode = (await firstRequest.Content.ReadFromJsonAsync<RequestOtpResponse>())!.DevOtp!;
        var first = await Client.PostAsJsonAsync("/api/customer/auth/verify", new VerifyCustomerOtpRequest(phone, firstCode, "First", "Last"));
        var firstBody = await first.Content.ReadFromJsonAsync<VerifyOtpResponse>();

        // A second real /otp request would hit the 45s cooldown immediately after the first --
        // insert the next code directly, same as CreateBookingToken bypasses the WhatsApp webhook
        // for the equivalent WhatsApp-login "reuses it" test above.
        const string secondCode = "654321";
        using (var db = Db())
        {
            db.CustomerOtps.Add(new CustomerOtp
            {
                Phone = phone,
                CodeHash = BCrypt.Net.BCrypt.HashPassword(secondCode),
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            });
            await db.SaveChangesAsync();
        }

        var second = await Client.PostAsJsonAsync("/api/customer/auth/verify", new VerifyCustomerOtpRequest(phone, secondCode, "Someone", "Else"));
        var secondBody = await second.Content.ReadFromJsonAsync<VerifyOtpResponse>();

        Assert.Equal(firstBody!.CustomerId, secondBody!.CustomerId);
        Assert.Equal("First", secondBody.Name); // unchanged by the second (unused) name args
    }

    [Fact]
    public async Task VerifyOtp_BackfillsCustomerAccountIdOnExistingGuestBookedCustomerRow()
    {
        var (businessId, _, _) = await SeedBusinessAndService("otp-backfill@example.com", "otp-backfill");
        var phone = "+15558880007";

        using (var db = Db())
        {
            db.Customers.Add(new Customer { BusinessId = businessId, Phone = phone, Name = "Guest", FamilyName = "Booker" });
            await db.SaveChangesAsync();
        }

        var request = await Client.PostAsJsonAsync("/api/customer/auth/otp", new RequestCustomerOtpRequest(phone));
        var code = (await request.Content.ReadFromJsonAsync<RequestOtpResponse>())!.DevOtp!;
        var verify = await Client.PostAsJsonAsync("/api/customer/auth/verify", new VerifyCustomerOtpRequest(phone, code, "First", "Last"));
        var body = await verify.Content.ReadFromJsonAsync<VerifyOtpResponse>();

        using var db2 = Db();
        var customer = await db2.Customers.FirstAsync(c => c.BusinessId == businessId && c.Phone == phone);
        Assert.Equal(body!.CustomerId, customer.CustomerAccountId);
    }
}
