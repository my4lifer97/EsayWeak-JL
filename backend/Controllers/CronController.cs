using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Twilio;
using Twilio.Rest.Api.V2010.Account;

namespace BarberSaas.Api.Controllers;

[ApiController]
[Route("api/cron")]
public class CronController(AppDbContext db, IConfiguration config) : ControllerBase
{
    [HttpGet("reminders")]
    public async Task<IActionResult> SendReminders()
    {
        var cronSecret = config["CronSecret"];
        var auth = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(cronSecret) || auth != $"Bearer {cronSecret}")
            return Unauthorized(new { error = "Unauthorized" });

        var tomorrow = DateTime.UtcNow.AddDays(1).Date;

        var appointments = await db.Appointments
            .Include(a => a.Barber)
            .Include(a => a.Customer)
            .Include(a => a.Service)
            .Where(a => a.Date == tomorrow && a.Status == AppointmentStatus.CONFIRMED && !a.ReminderSent)
            .ToListAsync();

        var appUrl = config["AppUrl"] ?? "";
        int sent = 0, failed = 0;

        foreach (var appt in appointments)
        {
            if (appt.Barber.TwilioSid is null || appt.Barber.TwilioToken is null || appt.Barber.TwilioNumber is null)
                continue;

            try
            {
                var lang = appt.Barber.Language.ToString();
                var serviceName = lang switch
                {
                    "AR" => appt.Service.NameAr,
                    "HE" => appt.Service.NameHe,
                    _ => appt.Service.NameEn,
                };

                var cancelUrl = $"{appUrl}/{appt.Barber.Slug}/appointments/{appt.Id}?token={appt.CancelToken}";
                var message = I18nService.T(lang, "reminder.message", new()
                {
                    ["customerName"] = appt.Customer.Name,
                    ["barberName"] = appt.Barber.Name,
                    ["time"] = appt.StartTime,
                    ["service"] = serviceName,
                    ["cancelUrl"] = cancelUrl,
                });

                TwilioClient.Init(appt.Barber.TwilioSid, appt.Barber.TwilioToken);
                await MessageResource.CreateAsync(
                    from: new Twilio.Types.PhoneNumber($"whatsapp:{appt.Barber.TwilioNumber}"),
                    to: new Twilio.Types.PhoneNumber($"whatsapp:{appt.Customer.Phone}"),
                    body: message);

                appt.ReminderSent = true;
                sent++;
            }
            catch
            {
                failed++;
            }
        }

        await db.SaveChangesAsync();
        return Ok(new { total = appointments.Count, sent, failed });
    }
}
