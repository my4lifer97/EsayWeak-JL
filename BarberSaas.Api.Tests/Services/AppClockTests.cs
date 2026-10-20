using BarberSaas.Api.Services;
using Xunit;

namespace BarberSaas.Api.Tests.Services;

// Production runs in UTC; appointment times are Israeli wall-clock values.
public class AppClockTests
{
    [Fact]
    public void Now_IsIsraeliTime_WhateverTheServerZone()
    {
        var expected = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem"));
        var offset = AppClock.Now - DateTime.UtcNow;
        Assert.InRange(offset.TotalHours, 1.9, 3.1); // UTC+2 in winter, UTC+3 in summer
        Assert.InRange((AppClock.Now - expected).TotalSeconds, -5, 5);
    }
}
