using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Xunit;

namespace BarberSaas.Api.Tests.Services;

public class AppointmentStatusHelperTests
{
    // EffectiveStatus is documented and implemented to compare against local server time
    // (DateTime.Now), not UtcNow — these dates must be built the same way, or the test is
    // flaky depending on the runner's timezone and time of day (see AvailabilityService for
    // the same UTC-offset lesson applied to slot generation).
    [Fact]
    public void ConfirmedAppointment_InThePast_ShowsAsCompleted()
    {
        var pastDate = DateTime.Now.Date.AddDays(-1);

        var result = AppointmentStatusHelper.EffectiveStatus(AppointmentStatus.CONFIRMED, pastDate, "23:59");

        Assert.Equal("COMPLETED", result);
    }

    [Fact]
    public void ConfirmedAppointment_InTheFuture_StaysConfirmed()
    {
        var futureDate = DateTime.Now.Date.AddDays(1);

        var result = AppointmentStatusHelper.EffectiveStatus(AppointmentStatus.CONFIRMED, futureDate, "00:01");

        Assert.Equal("CONFIRMED", result);
    }

    [Fact]
    public void CancelledAppointment_StaysCancelled_EvenIfInThePast()
    {
        var pastDate = DateTime.Now.Date.AddDays(-1);

        var result = AppointmentStatusHelper.EffectiveStatus(AppointmentStatus.CANCELLED, pastDate, "10:00");

        Assert.Equal("CANCELLED", result);
    }
}
