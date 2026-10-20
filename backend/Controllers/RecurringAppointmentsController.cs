using BarberSaas.Api.Data;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Filters;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace BarberSaas.Api.Controllers;

[ApiController]
[Route("api/admin/recurring")]
[Authorize(Policy = "BusinessOnly")]
public class RecurringAppointmentsController(AppDbContext db, RecurringAppointmentService recurringAppointments, AppointmentCancellationService cancellationService, CustomerNotifier customerNotifier, AvailabilityService availability) : ControllerBase
{
    private string BusinessId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private static RecurringSeriesDto ToDto(RecurringSeries s)
    {
        var today = AppClock.Now.Date;
        var daysUntil = ((s.DayOfWeek - (int)today.DayOfWeek) + 7) % 7;
        var next = today.AddDays(daysUntil);
        if (next < s.StartDate) next = s.StartDate;
        var nextOccurrence = (s.EndDate.HasValue && next > s.EndDate.Value) ? null : next.ToString("yyyy-MM-dd");

        return new RecurringSeriesDto(
            s.Id,
            new CustomerSummary(s.Customer.Id, s.Customer.Name, s.Customer.FamilyName, s.Customer.Phone),
            new ItemSummary(s.Item.Id, s.Item.NameEn, s.Item.NameAr, s.Item.NameHe, s.Item.DurationMinutes, s.Item.Price),
            s.DayOfWeek, s.StartTime, s.Notes, s.IsActive,
            s.StartDate.ToString("yyyy-MM-dd"), s.EndDate?.ToString("yyyy-MM-dd"), nextOccurrence,
            s.Skips.OrderByDescending(sk => sk.Date).Take(5).Select(sk => new RecurringSkipDto(sk.Date.ToString("yyyy-MM-dd"), sk.Reason)).ToList());
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var series = await db.RecurringSeries
            .Include(s => s.Customer).Include(s => s.Item).Include(s => s.Skips)
            .Where(s => s.BusinessId == BusinessId)
            .OrderByDescending(s => s.IsActive).ThenBy(s => s.DayOfWeek).ThenBy(s => s.StartTime)
            .ToListAsync();
        return Ok(series.Select(ToDto));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRecurringSeriesRequest req)
    {
        var item = await db.Items.FirstOrDefaultAsync(s => s.Id == req.ItemId && s.BusinessId == BusinessId && s.IsActive);
        if (item is null) return NotFound(new { error = "Item not found" });
        if (!item.IsBookable || item.DurationMinutes is null) return BadRequest(new { error = "This item is not bookable" });
        // Every series, not only a forced one: "99:00" was saved and then skipped every week.
        if (AvailabilityService.SlotTimeError(req.StartTime, item.DurationMinutes.Value) is { } timeError)
            return BadRequest(new { error = timeError });

        if (req.DayOfWeek < 0 || req.DayOfWeek > 6)
            return BadRequest(new { error = "Invalid day of week" });
        if (!Regex.IsMatch(req.StartTime, @"^\d{2}:\d{2}$"))
            return BadRequest(new { error = "Invalid start time" });

        Customer? customer;
        if (!string.IsNullOrWhiteSpace(req.CustomerId))
        {
            customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == req.CustomerId && c.BusinessId == BusinessId);
            if (customer is null) return NotFound(new { error = "Customer not found" });
        }
        else
        {
            if (string.IsNullOrWhiteSpace(req.CustomerName) || string.IsNullOrWhiteSpace(req.CustomerPhone))
                return BadRequest(new { error = "Customer name and phone are required" });
            if (!PhoneNormalizer.IsValid(req.CustomerPhone))
                return BadRequest(new { error = "Please enter a valid phone number" });
            var customerPhone = PhoneNormalizer.Normalize(req.CustomerPhone);
            customer = await db.Customers.FirstOrDefaultAsync(c => c.BusinessId == BusinessId && c.Phone == customerPhone);
            // Link to an existing CustomerAccount by phone, same as the public booking flow does
            // from its JWT -- otherwise a recurring series the owner sets up for an
            // already-registered customer never shows its occurrences in that customer's own "My
            // Bookings" until they book/reschedule with this business themselves.
            var customerAccountId = (await db.CustomerAccounts.FirstOrDefaultAsync(a => a.Phone == customerPhone))?.Id;
            if (customer is null)
            {
                customer = new Customer
                {
                    Name = req.CustomerName, FamilyName = req.CustomerFamilyName ?? "", Phone = customerPhone, BusinessId = BusinessId,
                    CustomerAccountId = customerAccountId,
                };
                db.Customers.Add(customer);
            }
            else
            {
                if (OwnerTypedCustomer.MatchExisting(customer, req.CustomerName, req.CustomerFamilyName) is { } conflict)
                    return Conflict(new { error = conflict });
                if (customerAccountId is not null) customer.CustomerAccountId = customerAccountId;
            }
        }

        var today = AppClock.Now.Date;
        DateTime startDate;
        if (string.IsNullOrWhiteSpace(req.StartDate))
        {
            var daysUntil = ((req.DayOfWeek - (int)today.DayOfWeek) + 7) % 7;
            startDate = today.AddDays(daysUntil);
        }
        else
        {
            startDate = DateTime.Parse(req.StartDate + "T00:00:00Z").ToUniversalTime();
            if (startDate.Date < today) return BadRequest(new { error = "Start date cannot be in the past" });
        }

        // Without "custom time" the series has to fit the opening hours of its first date. A closed
        // day used to be accepted and then logged a slot_unavailable skip every week (QA run 3).
        // That's the first DayOfWeek on or after StartDate, which can be a different weekday.
        if (!req.Force)
        {
            var firstDate = startDate.Date.AddDays(((req.DayOfWeek - (int)startDate.DayOfWeek) + 7) % 7);
            var day = await availability.GetEffectiveDay(BusinessId, firstDate);
            var end = AvailabilityService.AddMinutes(req.StartTime, item.DurationMinutes.Value);
            if (!day.IsActive || string.CompareOrdinal(req.StartTime, day.StartTime) < 0 || string.CompareOrdinal(end, day.EndTime) > 0)
                return BadRequest(new { error = "This time is outside your working hours" });
        }

        DateTime? endDate = null;
        if (!string.IsNullOrWhiteSpace(req.EndDate))
        {
            endDate = DateTime.Parse(req.EndDate + "T00:00:00Z").ToUniversalTime();
            if (endDate.Value.Date < startDate.Date) return BadRequest(new { error = "End date cannot be before start date" });
        }

        var series = new RecurringSeries
        {
            BusinessId = BusinessId,
            CustomerId = customer.Id,
            ItemId = item.Id,
            DayOfWeek = req.DayOfWeek,
            StartTime = req.StartTime,
            Notes = req.Notes,
            StartDate = startDate,
            EndDate = endDate,
            Force = req.Force,
        };
        db.RecurringSeries.Add(series);
        await db.SaveChangesAsync();

        // Materialize the first occurrence immediately -- otherwise the slot wouldn't appear
        // on the dashboard or block other bookings until the next daily cron run.
        await recurringAppointments.GenerateForSeriesNow(series.Id);

        var created = await db.RecurringSeries
            .Include(s => s.Customer).Include(s => s.Item).Include(s => s.Skips)
            .FirstAsync(s => s.Id == series.Id);

        this.SetActivityDetail(
            $"Created recurring series: {item.NameEn} with {ActivityDetailExtensions.FullName(customer.Name, customer.FamilyName)} — every {(DayOfWeek)req.DayOfWeek}s at {req.StartTime}");

        return StatusCode(201, ToDto(created));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, [FromQuery] bool notifyCustomer = true)
    {
        var series = await db.RecurringSeries
            .Include(s => s.Customer).Include(s => s.Item)
            .FirstOrDefaultAsync(s => s.Id == id && s.BusinessId == BusinessId);
        if (series is null) return NotFound();

        this.SetActivityDetail(
            $"Deleted recurring series: {series.Item.NameEn} with {ActivityDetailExtensions.FullName(series.Customer.Name, series.Customer.FamilyName)} — every {(DayOfWeek)series.DayOfWeek}s at {series.StartTime}");

        // Deleting a series means the customer no longer has these slots reserved --
        // cancel every occurrence that hasn't happened yet, freeing the slot for others.
        // Already-completed appointments are left untouched (historical record, and their
        // effective "COMPLETED" status is computed from the passed end time, never stored).
        var linkedAppointments = await db.Appointments
            .Where(a => a.RecurringSeriesId == id && a.Status == AppointmentStatus.CONFIRMED)
            .OrderBy(a => a.Date).ThenBy(a => a.StartTime)
            .ToListAsync();
        var cancelled = new List<Appointment>();
        foreach (var appt in linkedAppointments.Where(a => AppointmentStatusHelper.EffectiveStatus(a.Status, a.Date, a.EndTime) == "CONFIRMED"))
            if (await cancellationService.CancelAsync(appt, notifyWaitlist: true)) cancelled.Add(appt);

        // QA run 2: deleting a series cancelled the customer's upcoming appointments silently.
        if (notifyCustomer && cancelled.Count > 0)
            await customerNotifier.QueueSeriesCancelledByOwner(cancelled);

        db.RecurringSeries.Remove(series);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
