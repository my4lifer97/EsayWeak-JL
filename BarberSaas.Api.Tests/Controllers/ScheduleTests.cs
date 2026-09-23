using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.DTOs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// Covers three schedule additions: blocking a range of dates (Blocked Dates -- the existing
// single-date path, AddBlockedSlot, is unchanged and untested here), per-date working-hours
// overrides, and saveable/reusable weekly schedule presets.
public class ScheduleTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);

    // A full 7-day preset payload: one day active with the given hours, the rest inactive at a
    // placeholder time -- matches what the preset editor modal always submits (all 7 days, every
    // save), never a partial list.
    private static List<SchedulePresetDayDto> PresetDays(int activeDay, string start, string end) =>
        Enumerable.Range(0, 7).Select(d => new SchedulePresetDayDto(d, d == activeDay ? start : "09:00", d == activeDay ? end : "18:00", d == activeDay)).ToList();

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


    [Fact]
    public async Task SchedulePreset_SaveThenApply_OverwritesWorkingHours()
    {
        var token = await RegisterAndLoginBusiness("schedule-preset-1@example.com", "schedule-preset-1");
        Authorize(Client, token);
        // Sunday (0) active 09:00-18:00, every other day inactive -- the "normal" schedule.
        await Client.PostAsJsonAsync("/api/admin/schedule",
            Enumerable.Range(0, 7).Select(d => new WorkingHoursDto(null, d, "09:00", "18:00", d == 0)).ToList());

        var saveResp = await Client.PostAsJsonAsync("/api/admin/schedule/presets", new SaveSchedulePresetRequest("Christmas Hours", PresetDays(0, "09:00", "18:00")));
        Assert.Equal(HttpStatusCode.Created, saveResp.StatusCode);
        var preset = await saveResp.Content.ReadFromJsonAsync<SchedulePresetDto>();
        Assert.Equal(7, preset!.Days.Count);
        Assert.True(preset.Days.Single(d => d.DayOfWeek == 0).IsActive);

        // Change the live schedule to something different from what was saved.
        await Client.PostAsJsonAsync("/api/admin/schedule",
            Enumerable.Range(0, 7).Select(d => new WorkingHoursDto(null, d, "10:00", "12:00", d == 3)).ToList());

        var applyResp = await Client.PostAsync($"/api/admin/schedule/presets/{preset.Id}/apply", null);
        Assert.Equal(HttpStatusCode.OK, applyResp.StatusCode);

        var schedule = await Client.GetFromJsonAsync<ScheduleResponse>("/api/admin/schedule");
        var sunday = schedule!.WorkingHours.Single(h => h.DayOfWeek == 0);
        var wednesday = schedule.WorkingHours.Single(h => h.DayOfWeek == 3);
        Assert.True(sunday.IsActive);
        Assert.Equal("09:00", sunday.StartTime);
        Assert.False(wednesday.IsActive); // reverted back to the preset's (inactive) Wednesday
    }

    [Fact]
    public async Task ApplyingAPreset_AlsoBecomesTheDefault_SoItSurvivesARevert()
    {
        var token = await RegisterAndLoginBusiness("schedule-preset-permanent-1@example.com", "schedule-preset-permanent-1");
        Authorize(Client, token);
        await Client.PostAsJsonAsync("/api/admin/schedule/breaks", new CreateBreakRequest(1, "12:00", "13:00"));
        var summerResp = await Client.PostAsJsonAsync("/api/admin/schedule/presets",
            new SaveSchedulePresetRequest("Summer Hours", PresetDays(3, "10:00", "14:00"), [new SchedulePresetBreakDto(3, "11:00", "11:30")]));
        var summer = await summerResp.Content.ReadFromJsonAsync<SchedulePresetDto>();

        var applyResp = await Client.PostAsync($"/api/admin/schedule/presets/{summer!.Id}/apply", null);
        Assert.Equal(HttpStatusCode.OK, applyResp.StatusCode);

        var presets = await Client.GetFromJsonAsync<List<SchedulePresetDto>>("/api/admin/schedule/presets");
        var defaultPreset = presets!.Single(p => p.IsDefault);
        Assert.Equal("Default", defaultPreset.Name);
        var wednesday = defaultPreset.Days.Single(d => d.DayOfWeek == 3);
        Assert.True(wednesday.IsActive);
        Assert.Equal("10:00", wednesday.StartTime);
        Assert.Equal("14:00", wednesday.EndTime);
        Assert.Equal(3, defaultPreset.Breaks.Single().DayOfWeek); // old Monday lunch break replaced

        // A later date range reverts to Default -- which must now be the applied hours, not the old ones.
        var tempResp = await Client.PostAsJsonAsync("/api/admin/schedule/presets", new SaveSchedulePresetRequest("Holiday", PresetDays(5, "08:00", "09:00")));
        var temp = await tempResp.Content.ReadFromJsonAsync<SchedulePresetDto>();
        var today = DateTime.Now.Date.ToString("yyyy-MM-dd");
        var scheduleResp = await Client.PostAsJsonAsync($"/api/admin/schedule/presets/{temp!.Id}/schedule", new SchedulePresetRangeRequest(today, today));
        var scheduled = await scheduleResp.Content.ReadFromJsonAsync<PresetScheduleDto>();
        await Client.DeleteAsync($"/api/admin/schedule/preset-schedule/{scheduled!.Id}");

        var schedule = await Client.GetFromJsonAsync<ScheduleResponse>("/api/admin/schedule");
        Assert.True(schedule!.WorkingHours.Single(h => h.DayOfWeek == 3).IsActive);
        Assert.False(schedule.WorkingHours.Single(h => h.DayOfWeek == 5).IsActive);
    }

    [Fact]
    public async Task ApplyingAPreset_WhileADateRangeIsRunning_EndsThatRange()
    {
        var token = await RegisterAndLoginBusiness("schedule-preset-permanent-2@example.com", "schedule-preset-permanent-2");
        Authorize(Client, token);
        var holidayResp = await Client.PostAsJsonAsync("/api/admin/schedule/presets", new SaveSchedulePresetRequest("Holiday", PresetDays(5, "08:00", "09:00")));
        var holiday = await holidayResp.Content.ReadFromJsonAsync<SchedulePresetDto>();
        var today = DateTime.Now.Date.ToString("yyyy-MM-dd");
        var future = DateTime.Now.Date.AddDays(7).ToString("yyyy-MM-dd");
        await Client.PostAsJsonAsync($"/api/admin/schedule/presets/{holiday!.Id}/schedule", new SchedulePresetRangeRequest(today, future));
        var summerResp = await Client.PostAsJsonAsync("/api/admin/schedule/presets", new SaveSchedulePresetRequest("Summer Hours", PresetDays(3, "10:00", "14:00")));
        var summer = await summerResp.Content.ReadFromJsonAsync<SchedulePresetDto>();

        var applyResp = await Client.PostAsync($"/api/admin/schedule/presets/{summer!.Id}/apply", null);

        Assert.Equal(HttpStatusCode.OK, applyResp.StatusCode);
        var current = await Client.GetFromJsonAsync<PresetScheduleResponse>("/api/admin/schedule/preset-schedule");
        Assert.Null(current!.Schedule);
        var schedule = await Client.GetFromJsonAsync<ScheduleResponse>("/api/admin/schedule");
        Assert.True(schedule!.WorkingHours.Single(h => h.DayOfWeek == 3).IsActive);
        Assert.False(schedule.WorkingHours.Single(h => h.DayOfWeek == 5).IsActive);
    }

    [Fact]
    public async Task SchedulePreset_Delete_RemovesItWithoutTouchingWorkingHours()
    {
        var token = await RegisterAndLoginBusiness("schedule-preset-2@example.com", "schedule-preset-2");
        Authorize(Client, token);
        await Client.PostAsJsonAsync("/api/admin/schedule",
            Enumerable.Range(0, 7).Select(d => new WorkingHoursDto(null, d, "09:00", "18:00", d == 0)).ToList());
        var saveResp = await Client.PostAsJsonAsync("/api/admin/schedule/presets", new SaveSchedulePresetRequest("Temp", PresetDays(0, "09:00", "18:00")));
        var preset = await saveResp.Content.ReadFromJsonAsync<SchedulePresetDto>();

        var deleteResp = await Client.DeleteAsync($"/api/admin/schedule/presets/{preset!.Id}");

        Assert.Equal(HttpStatusCode.OK, deleteResp.StatusCode);
        var presets = await Client.GetFromJsonAsync<List<SchedulePresetDto>>("/api/admin/schedule/presets");
        Assert.All(presets!, p => Assert.True(p.IsDefault)); // only the (undeletable) Default preset remains
        var schedule = await Client.GetFromJsonAsync<ScheduleResponse>("/api/admin/schedule");
        Assert.True(schedule!.WorkingHours.Single(h => h.DayOfWeek == 0).IsActive);
    }

    [Fact]
    public async Task SchedulePreset_Update_ChangesNameAndDays()
    {
        var token = await RegisterAndLoginBusiness("schedule-preset-3@example.com", "schedule-preset-3");
        Authorize(Client, token);
        var saveResp = await Client.PostAsJsonAsync("/api/admin/schedule/presets", new SaveSchedulePresetRequest("Draft", PresetDays(0, "09:00", "18:00")));
        var preset = await saveResp.Content.ReadFromJsonAsync<SchedulePresetDto>();

        var updateResp = await Client.PutAsJsonAsync($"/api/admin/schedule/presets/{preset!.Id}",
            new UpdateSchedulePresetRequest("Christmas Hours", PresetDays(1, "10:00", "14:00")));

        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
        var updated = await updateResp.Content.ReadFromJsonAsync<SchedulePresetDto>();
        Assert.Equal("Christmas Hours", updated!.Name);
        Assert.True(updated.Days.Single(d => d.DayOfWeek == 1).IsActive);
        Assert.False(updated.Days.Single(d => d.DayOfWeek == 0).IsActive);
    }

    [Fact]
    public async Task SchedulePresetRange_StartingTodayAppliesImmediatelyAndRevertsToDefaultLater()
    {
        var token = await RegisterAndLoginBusiness("schedule-preset-range-1@example.com", "schedule-preset-range-1");
        Authorize(Client, token);
        await Client.PostAsJsonAsync("/api/admin/schedule",
            Enumerable.Range(0, 7).Select(d => new WorkingHoursDto(null, d, "09:00", "18:00", d == 0)).ToList());
        var saveResp = await Client.PostAsJsonAsync("/api/admin/schedule/presets", new SaveSchedulePresetRequest("Christmas Hours", PresetDays(3, "10:00", "14:00")));
        var preset = await saveResp.Content.ReadFromJsonAsync<SchedulePresetDto>();
        var today = DateTime.Now.Date.ToString("yyyy-MM-dd");
        var future = DateTime.Now.Date.AddDays(7).ToString("yyyy-MM-dd");

        var scheduleResp = await Client.PostAsJsonAsync($"/api/admin/schedule/presets/{preset!.Id}/schedule",
            new SchedulePresetRangeRequest(today, future));

        Assert.Equal(HttpStatusCode.Created, scheduleResp.StatusCode);
        var scheduled = await scheduleResp.Content.ReadFromJsonAsync<PresetScheduleDto>();
        Assert.True(scheduled!.Applied); // start date is today -- takes effect immediately

        var schedule = await Client.GetFromJsonAsync<ScheduleResponse>("/api/admin/schedule");
        var wednesday = schedule!.WorkingHours.Single(h => h.DayOfWeek == 3);
        Assert.True(wednesday.IsActive);
        Assert.Equal("10:00", wednesday.StartTime);

        // A Default preset was lazily created (capturing the pre-schedule hours) as the revert target.
        var presets = await Client.GetFromJsonAsync<List<SchedulePresetDto>>("/api/admin/schedule/presets");
        var defaultPreset = presets!.Single(p => p.IsDefault);
        Assert.Equal("Default", defaultPreset.Name);
        Assert.True(defaultPreset.Days.Single(d => d.DayOfWeek == 0).IsActive);

        var current = await Client.GetFromJsonAsync<PresetScheduleResponse>("/api/admin/schedule/preset-schedule");
        Assert.NotNull(current!.Schedule);
        Assert.Equal(preset.Id, current.Schedule!.PresetId);
    }

    [Fact]
    public async Task SchedulePresetRange_SchedulingAgain_ReplacesTheExistingRange()
    {
        var token = await RegisterAndLoginBusiness("schedule-preset-range-2@example.com", "schedule-preset-range-2");
        Authorize(Client, token);
        var saveResp = await Client.PostAsJsonAsync("/api/admin/schedule/presets", new SaveSchedulePresetRequest("A", PresetDays(0, "09:00", "18:00")));
        var preset = await saveResp.Content.ReadFromJsonAsync<SchedulePresetDto>();
        var start = DateTime.Now.Date.AddDays(10).ToString("yyyy-MM-dd");
        var end = DateTime.Now.Date.AddDays(17).ToString("yyyy-MM-dd");
        var newEnd = DateTime.Now.Date.AddDays(40).ToString("yyyy-MM-dd");
        await Client.PostAsJsonAsync($"/api/admin/schedule/presets/{preset!.Id}/schedule", new SchedulePresetRangeRequest(start, end));

        var secondResp = await Client.PostAsJsonAsync($"/api/admin/schedule/presets/{preset.Id}/schedule", new SchedulePresetRangeRequest(start, newEnd));

        Assert.Equal(HttpStatusCode.Created, secondResp.StatusCode);
        var current = await Client.GetFromJsonAsync<PresetScheduleResponse>("/api/admin/schedule/preset-schedule");
        Assert.Equal(newEnd, current!.Schedule!.EndDate);
        using var db = Db();
        Assert.Equal(1, await db.PresetSchedules.CountAsync());
    }

    [Fact]
    public async Task SchedulePresetRange_ReplacingARunningRangeWithAFutureOne_RevertsToDefaultMeanwhile()
    {
        var token = await RegisterAndLoginBusiness("schedule-preset-range-5@example.com", "schedule-preset-range-5");
        Authorize(Client, token);
        await Client.PostAsJsonAsync("/api/admin/schedule",
            Enumerable.Range(0, 7).Select(d => new WorkingHoursDto(null, d, "09:00", "18:00", d == 0)).ToList());
        var saveResp = await Client.PostAsJsonAsync("/api/admin/schedule/presets", new SaveSchedulePresetRequest("Holiday", PresetDays(3, "10:00", "14:00")));
        var preset = await saveResp.Content.ReadFromJsonAsync<SchedulePresetDto>();
        var today = DateTime.Now.Date.ToString("yyyy-MM-dd");
        await Client.PostAsJsonAsync($"/api/admin/schedule/presets/{preset!.Id}/schedule", new SchedulePresetRangeRequest(today, today));

        var laterStart = DateTime.Now.Date.AddDays(10).ToString("yyyy-MM-dd");
        var laterEnd = DateTime.Now.Date.AddDays(12).ToString("yyyy-MM-dd");
        var resp = await Client.PostAsJsonAsync($"/api/admin/schedule/presets/{preset.Id}/schedule", new SchedulePresetRangeRequest(laterStart, laterEnd));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var scheduled = await resp.Content.ReadFromJsonAsync<PresetScheduleDto>();
        Assert.False(scheduled!.Applied);
        var schedule = await Client.GetFromJsonAsync<ScheduleResponse>("/api/admin/schedule");
        Assert.True(schedule!.WorkingHours.Single(h => h.DayOfWeek == 0).IsActive); // back on Default until the new range starts
        Assert.False(schedule.WorkingHours.Single(h => h.DayOfWeek == 3).IsActive);
    }

    [Fact]
    public async Task EditingAPresetWhoseRangeIsRunning_UpdatesLiveHours()
    {
        var token = await RegisterAndLoginBusiness("schedule-preset-range-6@example.com", "schedule-preset-range-6");
        Authorize(Client, token);
        var saveResp = await Client.PostAsJsonAsync("/api/admin/schedule/presets", new SaveSchedulePresetRequest("Holiday", PresetDays(3, "10:00", "14:00")));
        var preset = await saveResp.Content.ReadFromJsonAsync<SchedulePresetDto>();
        var today = DateTime.Now.Date.ToString("yyyy-MM-dd");
        var future = DateTime.Now.Date.AddDays(7).ToString("yyyy-MM-dd");
        await Client.PostAsJsonAsync($"/api/admin/schedule/presets/{preset!.Id}/schedule", new SchedulePresetRangeRequest(today, future));

        var updateResp = await Client.PutAsJsonAsync($"/api/admin/schedule/presets/{preset.Id}",
            new UpdateSchedulePresetRequest("Holiday", PresetDays(3, "11:00", "20:00")));

        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
        var schedule = await Client.GetFromJsonAsync<ScheduleResponse>("/api/admin/schedule");
        Assert.Equal("20:00", schedule!.WorkingHours.Single(h => h.DayOfWeek == 3).EndTime);
    }

    [Fact]
    public async Task CancelPresetSchedule_WhileActive_RevertsWorkingHoursImmediately()
    {
        var token = await RegisterAndLoginBusiness("schedule-preset-range-3@example.com", "schedule-preset-range-3");
        Authorize(Client, token);
        await Client.PostAsJsonAsync("/api/admin/schedule",
            Enumerable.Range(0, 7).Select(d => new WorkingHoursDto(null, d, "09:00", "18:00", d == 0)).ToList());
        var saveResp = await Client.PostAsJsonAsync("/api/admin/schedule/presets", new SaveSchedulePresetRequest("Christmas Hours", PresetDays(3, "10:00", "14:00")));
        var preset = await saveResp.Content.ReadFromJsonAsync<SchedulePresetDto>();
        var today = DateTime.Now.Date.ToString("yyyy-MM-dd");
        var future = DateTime.Now.Date.AddDays(7).ToString("yyyy-MM-dd");
        var scheduleResp = await Client.PostAsJsonAsync($"/api/admin/schedule/presets/{preset!.Id}/schedule", new SchedulePresetRangeRequest(today, future));
        var scheduled = await scheduleResp.Content.ReadFromJsonAsync<PresetScheduleDto>();

        var cancelResp = await Client.DeleteAsync($"/api/admin/schedule/preset-schedule/{scheduled!.Id}");

        Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);
        var schedule = await Client.GetFromJsonAsync<ScheduleResponse>("/api/admin/schedule");
        var sunday = schedule!.WorkingHours.Single(h => h.DayOfWeek == 0);
        var wednesday = schedule.WorkingHours.Single(h => h.DayOfWeek == 3);
        Assert.True(sunday.IsActive); // reverted back to the Default preset's (pre-schedule) hours
        Assert.False(wednesday.IsActive);
        var current = await Client.GetFromJsonAsync<PresetScheduleResponse>("/api/admin/schedule/preset-schedule");
        Assert.Null(current!.Schedule);
    }

    [Fact]
    public async Task DeleteSchedulePreset_BlockedWhileReferencedByAnActiveSchedule()
    {
        var token = await RegisterAndLoginBusiness("schedule-preset-range-4@example.com", "schedule-preset-range-4");
        Authorize(Client, token);
        var saveResp = await Client.PostAsJsonAsync("/api/admin/schedule/presets", new SaveSchedulePresetRequest("Christmas Hours", PresetDays(0, "09:00", "18:00")));
        var preset = await saveResp.Content.ReadFromJsonAsync<SchedulePresetDto>();
        var today = DateTime.Now.Date.ToString("yyyy-MM-dd");
        var future = DateTime.Now.Date.AddDays(7).ToString("yyyy-MM-dd");
        await Client.PostAsJsonAsync($"/api/admin/schedule/presets/{preset!.Id}/schedule", new SchedulePresetRangeRequest(today, future));

        var deleteResp = await Client.DeleteAsync($"/api/admin/schedule/presets/{preset.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, deleteResp.StatusCode);
    }

    [Fact]
    public async Task DefaultPreset_IsAutoCreatedAndCannotBeDeletedOrScheduledForARange()
    {
        var token = await RegisterAndLoginBusiness("schedule-default-1@example.com", "schedule-default-1");
        Authorize(Client, token);

        var presets = await Client.GetFromJsonAsync<List<SchedulePresetDto>>("/api/admin/schedule/presets");
        var defaultPreset = Assert.Single(presets!);
        Assert.True(defaultPreset.IsDefault);
        Assert.Equal("Default", defaultPreset.Name);
        Assert.Equal(7, defaultPreset.Days.Count);

        var today = DateTime.Now.Date.ToString("yyyy-MM-dd");
        var future = DateTime.Now.Date.AddDays(7).ToString("yyyy-MM-dd");
        var scheduleResp = await Client.PostAsJsonAsync($"/api/admin/schedule/presets/{defaultPreset.Id}/schedule",
            new SchedulePresetRangeRequest(today, future));
        Assert.Equal(HttpStatusCode.BadRequest, scheduleResp.StatusCode);

        var deleteResp = await Client.DeleteAsync($"/api/admin/schedule/presets/{defaultPreset.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, deleteResp.StatusCode);
    }

    [Fact]
    public async Task DefaultPreset_Update_AppliesImmediatelyAndKeepsNameLocked()
    {
        var token = await RegisterAndLoginBusiness("schedule-default-2@example.com", "schedule-default-2");
        Authorize(Client, token);
        var presets = await Client.GetFromJsonAsync<List<SchedulePresetDto>>("/api/admin/schedule/presets");
        var defaultPreset = presets!.Single(p => p.IsDefault);

        var updateResp = await Client.PutAsJsonAsync($"/api/admin/schedule/presets/{defaultPreset.Id}",
            new UpdateSchedulePresetRequest("Tried To Rename", PresetDays(2, "08:00", "16:00")));

        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
        var updated = await updateResp.Content.ReadFromJsonAsync<SchedulePresetDto>();
        Assert.Equal("Default", updated!.Name); // name stays locked regardless of what's sent
        Assert.True(updated.IsDefault);

        // No scheduled range is active, so editing the Default preset applies straight to WorkingHours.
        var schedule = await Client.GetFromJsonAsync<ScheduleResponse>("/api/admin/schedule");
        var tuesday = schedule!.WorkingHours.Single(h => h.DayOfWeek == 2);
        Assert.True(tuesday.IsActive);
        Assert.Equal("08:00", tuesday.StartTime);
    }

    [Fact]
    public async Task SchedulePreset_SaveWithBreaks_RoundTrips()
    {
        var token = await RegisterAndLoginBusiness("schedule-preset-breaks-1@example.com", "schedule-preset-breaks-1");
        Authorize(Client, token);
        var breaks = new List<SchedulePresetBreakDto> { new(1, "12:00", "13:00"), new(3, "12:00", "13:00") };

        var saveResp = await Client.PostAsJsonAsync("/api/admin/schedule/presets",
            new SaveSchedulePresetRequest("Christmas Hours", PresetDays(0, "09:00", "18:00"), breaks));
        var preset = await saveResp.Content.ReadFromJsonAsync<SchedulePresetDto>();

        Assert.Equal(2, preset!.Breaks.Count);
        Assert.Contains(preset.Breaks, b => b.DayOfWeek == 1 && b.StartTime == "12:00");

        var fetched = await Client.GetFromJsonAsync<List<SchedulePresetDto>>("/api/admin/schedule/presets");
        var refetched = fetched!.Single(p => p.Id == preset.Id);
        Assert.Equal(2, refetched.Breaks.Count);
    }

    [Fact]
    public async Task SchedulePreset_UpdateBreaks_ReplacesEntireList()
    {
        var token = await RegisterAndLoginBusiness("schedule-preset-breaks-2@example.com", "schedule-preset-breaks-2");
        Authorize(Client, token);
        var saveResp = await Client.PostAsJsonAsync("/api/admin/schedule/presets",
            new SaveSchedulePresetRequest("Draft", PresetDays(0, "09:00", "18:00"), [new SchedulePresetBreakDto(1, "12:00", "13:00")]));
        var preset = await saveResp.Content.ReadFromJsonAsync<SchedulePresetDto>();

        var updateResp = await Client.PutAsJsonAsync($"/api/admin/schedule/presets/{preset!.Id}",
            new UpdateSchedulePresetRequest("Draft", PresetDays(0, "09:00", "18:00"),
                [new SchedulePresetBreakDto(2, "14:00", "15:00"), new SchedulePresetBreakDto(4, "14:00", "15:00")]));

        var updated = await updateResp.Content.ReadFromJsonAsync<SchedulePresetDto>();
        Assert.Equal(2, updated!.Breaks.Count);
        Assert.DoesNotContain(updated.Breaks, b => b.DayOfWeek == 1); // old break is gone, not just added-to
        Assert.Contains(updated.Breaks, b => b.DayOfWeek == 2);
        Assert.Contains(updated.Breaks, b => b.DayOfWeek == 4);
    }

    [Fact]
    public async Task ApplyingAPreset_ReplacesLiveBreaksWithThePresetsOwn()
    {
        var token = await RegisterAndLoginBusiness("schedule-preset-breaks-3@example.com", "schedule-preset-breaks-3");
        Authorize(Client, token);
        await Client.PostAsJsonAsync("/api/admin/schedule/breaks", new CreateBreakRequest(1, "12:00", "13:00"));
        var saveResp = await Client.PostAsJsonAsync("/api/admin/schedule/presets",
            new SaveSchedulePresetRequest("Summer Hours", PresetDays(0, "09:00", "18:00"), [new SchedulePresetBreakDto(5, "16:00", "16:30")]));
        var preset = await saveResp.Content.ReadFromJsonAsync<SchedulePresetDto>();

        var applyResp = await Client.PostAsync($"/api/admin/schedule/presets/{preset!.Id}/apply", null);

        Assert.Equal(HttpStatusCode.OK, applyResp.StatusCode);
        var schedule = await Client.GetFromJsonAsync<ScheduleResponse>("/api/admin/schedule");
        Assert.Single(schedule!.Breaks); // the old Monday lunch break is gone
        Assert.Equal(5, schedule.Breaks.Single().DayOfWeek);
        Assert.Equal("16:00", schedule.Breaks.Single().StartTime);
    }

    [Fact]
    public async Task DefaultPreset_LazilyCreated_CapturesExistingLiveBreaks()
    {
        var token = await RegisterAndLoginBusiness("schedule-preset-breaks-4@example.com", "schedule-preset-breaks-4");
        Authorize(Client, token);
        await Client.PostAsJsonAsync("/api/admin/schedule/breaks", new CreateBreakRequest(1, "12:00", "13:00"));

        // First call to the presets list is what lazily creates the Default preset (GetOrCreateDefaultPreset).
        var presets = await Client.GetFromJsonAsync<List<SchedulePresetDto>>("/api/admin/schedule/presets");
        var defaultPreset = presets!.Single(p => p.IsDefault);

        Assert.Single(defaultPreset.Breaks);
        Assert.Equal(1, defaultPreset.Breaks.Single().DayOfWeek);
    }
}
