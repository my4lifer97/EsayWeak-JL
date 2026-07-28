using BarberSaas.Api.Models;

namespace BarberSaas.Api.Services;

// Shared funnel for the 5 places in this app that cancel an appointment, so the
// waitlist-notify hook is written once instead of duplicated at every call site. Caller still
// owns SaveChangesAsync -- keeps this composable with bulk-cancel loops (e.g. deleting a
// recurring series cancels every future occurrence in one save after the loop).
public class AppointmentCancellationService(WaitlistService waitlist)
{
    public async Task CancelAsync(Appointment appointment, bool notifyWaitlist)
    {
        appointment.Status = AppointmentStatus.CANCELLED;
        if (notifyWaitlist) await waitlist.NotifyForCancellation(appointment);
    }
}
