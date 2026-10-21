using System.Linq.Expressions;
using BarberSaas.Api.Models;

namespace BarberSaas.Api.Services;

// Whether a business takes new bookings (Business.AcceptsBookings). Trial end dates only count
// once billing is configured (Cardcom:TerminalNumber + ApiName, the same check BillingController
// makes): until then there's no way to subscribe, so every trial would end after its 30 days with
// nothing the owner could do (owner's decision, 2026-10-21).
public class BookingRules(IConfiguration config)
{
    public bool TrialsEnforced =>
        !string.IsNullOrEmpty(config["Cardcom:TerminalNumber"]) && !string.IsNullOrEmpty(config["Cardcom:ApiName"]);

    public bool AcceptsBookings(Business business) => business.AcceptsBookings(DateTime.UtcNow, TrialsEnforced);

    public Expression<Func<Business, bool>> AcceptsBookingsQuery() => Business.AcceptsBookingsAt(DateTime.UtcNow, TrialsEnforced);
}
