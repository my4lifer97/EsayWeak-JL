using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

public class ReviewService(AppDbContext db)
{
    // A "completed" appointment is a still-CONFIRMED one whose end time has passed -- there is no
    // stored COMPLETED status (see AppointmentStatusHelper). PendingCancellationApproval rows still
    // read as CONFIRMED in the DB but the customer already asked to cancel, so they don't count.
    // The end-time math must use local wall-clock time, never UTC (see AvailabilityService).
    public async Task<Appointment?> MostRecentCompletedAppointment(string customerAccountId, string businessId)
    {
        var now = DateTime.Now;
        var candidates = await db.Appointments
            .Where(a => a.Customer.CustomerAccountId == customerAccountId
                     && a.BusinessId == businessId
                     && a.Status == AppointmentStatus.CONFIRMED
                     && !a.PendingCancellationApproval)
            .OrderByDescending(a => a.Date).ThenByDescending(a => a.StartTime)
            .ToListAsync();

        return candidates.FirstOrDefault(a => EndsAt(a.Date, a.EndTime) < now);
    }

    public async Task<bool> HasCompletedAppointment(string customerAccountId, string businessId) =>
        await MostRecentCompletedAppointment(customerAccountId, businessId) is not null;

    // Recomputed from rows (not incremental deltas) after every review mutation -- race-tolerant
    // and cheap at this scale. Hidden reviews are excluded from what the public sees.
    public async Task RecomputeAggregate(string businessId)
    {
        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId);
        if (business is null) return;

        var ratings = await db.Reviews
            .Where(r => r.BusinessId == businessId && !r.IsHidden)
            .Select(r => r.Rating)
            .ToListAsync();

        business.RatingCount = ratings.Count;
        business.RatingAverage = ratings.Count == 0 ? 0d : Math.Round(ratings.Average(), 2);
        await db.SaveChangesAsync();
    }

    private static DateTime EndsAt(DateTime date, string endTime)
    {
        var parts = endTime.Split(':');
        return date.Date.AddHours(int.Parse(parts[0])).AddMinutes(int.Parse(parts[1]));
    }
}
