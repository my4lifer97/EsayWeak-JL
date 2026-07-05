using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

public class FollowService(AppDbContext db)
{
    // Idempotently ensures a Follow row exists. Follows has a unique index on
    // (CustomerAccountId, BarberId), so a race between this exists-check and the insert (e.g.
    // a double-submitted booking, or a double-clicked Follow button) can still throw a unique-
    // constraint violation here — caught and treated as success rather than bubbling up as an
    // unhandled 500, since the end state (a Follow row exists) is exactly what we wanted anyway.
    public async Task EnsureFollowed(string customerAccountId, string barberId)
    {
        var exists = await db.Follows.AnyAsync(f => f.CustomerAccountId == customerAccountId && f.BarberId == barberId);
        if (exists) return;

        db.Follows.Add(new Follow { CustomerAccountId = customerAccountId, BarberId = barberId });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            // Only swallow if a concurrent request really did win the race; otherwise this was
            // some other failure and should still surface.
            if (!await db.Follows.AnyAsync(f => f.CustomerAccountId == customerAccountId && f.BarberId == barberId))
                throw;
        }
    }
}
