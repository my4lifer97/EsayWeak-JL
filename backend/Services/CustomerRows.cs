using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

public static class CustomerRows
{
    // A business has one customer per phone (unique index), so two owner bookings for the same new
    // phone at the same moment both tried to insert it and the loser was a 500 (QA run 7). Saved
    // on its own, before the booking: the request that loses the insert takes the row that won.
    // Only for a context with nothing else pending and no open transaction.
    public static async Task<Customer> AddOrGetExistingAsync(AppDbContext db, Customer customer)
    {
        db.Customers.Add(customer);
        try
        {
            await db.SaveChangesAsync();
            return customer;
        }
        catch (DbUpdateException)
        {
            db.Entry(customer).State = EntityState.Detached;
            var winner = await db.Customers.FirstOrDefaultAsync(c => c.BusinessId == customer.BusinessId && c.Phone == customer.Phone);
            if (winner is null) throw;
            return winner;
        }
    }
}
