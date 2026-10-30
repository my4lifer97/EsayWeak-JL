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

    private static int Minutes(string hhmm) => int.Parse(hhmm[..2]) * 60 + int.Parse(hhmm[3..]);

    // "Next" is the series' next real upcoming appointment. It used to be the next matching
    // weekday from the calendar alone, shown even when every week had been skipped (QA run 4).
    private async Task<Dictionary<string, DateTime>> NextOccurrences(IEnumerable<string> seriesIds)
    {
        var ids = seriesIds.ToList();
        var now = AppClock.Now;
        var today = now.Date;
        var nowTime = now.ToString("HH:mm");
        var upcoming = await db.Appointments
            .Where(a => a.RecurringSeriesId != null && ids.Contains(a.RecurringSeriesId) && a.Status == AppointmentStatus.CONFIRMED
                && (a.Date > today || (a.Date == today && string.Compare(a.StartTime, nowTime) > 0)))
            .Select(a => new { SeriesId = a.RecurringSeriesId!, a.Date })
            .ToListAsync();
        return upcoming.GroupBy(a => a.SeriesId).ToDictionary(g => g.Key, g => g.Min(a => a.Date));
    }

    private static RecurringSeriesDto ToDto(RecurringSeries s, IReadOnlyDictionary<string, DateTime> next)
    {
        var nextOccurrence = next.TryGetValue(s.Id, out var d) ? d.ToString("yyyy-MM-dd") : null;

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
        var next = await NextOccurrences(series.Select(s => s.Id));
        return Ok(series.Select(s => ToDto(s, next)));
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
        if (!Regex.IsMatch(req.StartTime, @"^[0-9]{2}:[0-9]{2}$"))
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
            // Trimmed like the one-off booking's (QA run 7).
            var customerName = req.CustomerName.Trim();
            var customerFamilyName = req.CustomerFamilyName?.Trim();
            if (customer is null)
            {
                customer = new Customer
                {
                    Name = customerName, FamilyName = customerFamilyName ?? "", Phone = customerPhone, BusinessId = BusinessId,
                    CustomerAccountId = customerAccountId,
                };
                db.Customers.Add(customer);
            }
            else
            {
                if (OwnerTypedCustomer.MatchExisting(customer, customerName, customerFamilyName) is { } conflict)
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
            // And be one of the times the generator can book: on the 30-minute grid from opening,
            // clear of breaks. 10:07 or a time inside a break was saved and then skipped every week (QA run 4).
            if ((Minutes(req.StartTime) - Minutes(day.StartTime)) % 30 != 0
                || day.Breaks.Any(b => AvailabilityService.Overlaps(req.StartTime, end, b.Start, b.End)))
                return BadRequest(new { error = "This time isn't one of your bookable times" });
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
        // Materialize the occurrences immediately -- otherwise the slot wouldn't appear on the
        // dashboard or block other bookings until the next daily cron run. One transaction with
        // the series itself: a series none of whose weeks could be booked (the slot is taken
        // every week) is refused instead of saved as an empty one that only logs skips, and a
        // failure doesn't leave an active series behind (QA run 4).
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            db.RecurringSeries.Add(series);
            await db.SaveChangesAsync();
            var (inRange, generated) = await recurringAppointments.GenerateForSeriesNow(series.Id);
            if (inRange > 0 && generated == 0)
            {
                await tx.RollbackAsync();
                db.ChangeTracker.Clear();
                return Conflict(new { error = "This time is already taken on every upcoming week" });
            }
            await tx.CommitAsync();
        }

        var created = await db.RecurringSeries
            .Include(s => s.Customer).Include(s => s.Item).Include(s => s.Skips)
            .FirstAsync(s => s.Id == series.Id);

        this.SetActivityDetail(
            $"Created recurring series: {item.NameEn} with {ActivityDetailExtensions.FullName(customer.Name, customer.FamilyName)} — every {(DayOfWeek)req.DayOfWeek}s at {req.StartTime}");

        return StatusCode(201, ToDto(created, await NextOccurrences([created.Id])));
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
        {
            // A claim that fails because the customer moved this occurrence meanwhile is retried on
            // the moved row. Skipped, it stayed CONFIRMED with no series and wasn't in the notice
            // (QA run 4). A row someone else cancelled stops the retries.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (await cancellationService.CancelAsync(appt, notifyWaitlist: true)) { cancelled.Add(appt); break; }
                await db.Entry(appt).ReloadAsync();
                if (AppointmentStatusHelper.EffectiveStatus(appt.Status, appt.Date, appt.EndTime) != "CONFIRMED") break;
            }
        }

        // QA run 2: deleting a series cancelled the customer's upcoming appointments silently.
        if (notifyCustomer && cancelled.Count > 0)
            await customerNotifier.QueueSeriesCancelledByOwner(cancelled);

        db.RecurringSeries.Remove(series);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
