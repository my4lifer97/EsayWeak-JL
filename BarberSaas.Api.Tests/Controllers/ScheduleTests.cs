using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.DTOs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// Covers the "block a range of dates" addition to Blocked Dates -- the existing single-date
// path (AddBlockedSlot) is unchanged and untested here; this only exercises the new range
// endpoint (AddBlockedRange).
public class ScheduleTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);

    private async Task<string> RegisterAndLoginBusiness(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));
        var body = await verify.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    [Fact]
    public async Task BlockedRange_CreatesOneRowPerDateInclusive()
    {
        var token = await RegisterAndLoginBusiness("schedule-range-1@example.com", "schedule-range-1");
        Authorize(Client, token);

        var resp = await Client.PostAsJsonAsync("/api/admin/schedule/blocked/range",
            new CreateBlockedRangeRequest("2026-12-24", "2026-12-27", null, null, "Christmas break"));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var created = await resp.Content.ReadFromJsonAsync<List<BlockedSlotDto>>();
        Assert.Equal(4, created!.Count); // 24, 25, 26, 27 inclusive
        Assert.Equal(["2026-12-24", "2026-12-25", "2026-12-26", "2026-12-27"], created.Select(s => s.Date));
        Assert.All(created, s => Assert.Equal("Christmas break", s.Reason));
        Assert.All(created, s => Assert.Null(s.StartTime)); // full-day block

        var list = await Client.GetFromJsonAsync<ScheduleResponse>("/api/admin/schedule");
        Assert.Equal(4, list!.BlockedSlots.Count(s => s.Reason == "Christmas break"));
    }

    [Fact]
    public async Task BlockedRange_DeletingOneDate_BehavesLikeAnOrdinaryBlockedSlot()
    {
        var token = await RegisterAndLoginBusiness("schedule-range-2@example.com", "schedule-range-2");
        Authorize(Client, token);
        var createResp = await Client.PostAsJsonAsync("/api/admin/schedule/blocked/range",
            new CreateBlockedRangeRequest("2026-12-24", "2026-12-26", null, null, null));
        var created = await createResp.Content.ReadFromJsonAsync<List<BlockedSlotDto>>();

        var deleteResp = await Client.DeleteAsync($"/api/admin/schedule/blocked/{created![1].Id}");

        Assert.Equal(HttpStatusCode.OK, deleteResp.StatusCode);
        var list = await Client.GetFromJsonAsync<ScheduleResponse>("/api/admin/schedule");
        Assert.Equal(2, list!.BlockedSlots.Count);
        Assert.DoesNotContain(list.BlockedSlots, s => s.Id == created[1].Id);
    }

    [Fact]
    public async Task BlockedRange_EndBeforeStart_IsRejected()
    {
        var token = await RegisterAndLoginBusiness("schedule-range-3@example.com", "schedule-range-3");
        Authorize(Client, token);

        var resp = await Client.PostAsJsonAsync("/api/admin/schedule/blocked/range",
            new CreateBlockedRangeRequest("2026-12-27", "2026-12-24", null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var db = Db();
        Assert.False(await db.BlockedSlots.AnyAsync());
    }

    [Fact]
    public async Task BlockedRange_ExceedingMaxSpan_IsRejected()
    {
        var token = await RegisterAndLoginBusiness("schedule-range-4@example.com", "schedule-range-4");
        Authorize(Client, token);

        var resp = await Client.PostAsJsonAsync("/api/admin/schedule/blocked/range",
            new CreateBlockedRangeRequest("2026-01-01", "2028-01-01", null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var db = Db();
        Assert.False(await db.BlockedSlots.AnyAsync());
    }
}
