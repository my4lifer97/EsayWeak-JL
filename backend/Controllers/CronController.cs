using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Controllers;

[ApiController]
[Route("api/cron")]
public class CronController(AppDbContext db, IConfiguration config, ILogger<CronController> logger, RecurringAppointmentService recurringAppointments, IWhatsAppSender whatsAppSender) : ControllerBase
{
    [HttpGet("generate-recurring")]
    public async Task<IActionResult> GenerateRecurringAppointments()
    {
        var cronSecret = config["CronSecret"];
        var auth = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(cronSecret) || auth != $"Bearer {cronSecret}")
            return Unauthorized(new { error = "Unauthorized" });

        var (total, created, skipped) = await recurringAppointments.GenerateOccurrences();
        return Ok(new { total, created, skipped });
    }

    [HttpGet("reminders")]
    public async Task<IActionResult> SendReminders()
    {
        var cronSecret = config["CronSecret"];
        var auth = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(cronSecret) || auth != $"Bearer {cronSecret}")
            return Unauthorized(new { error = "Unauthorized" });

        // a.Date is the barber's local wall-clock calendar date, never converted to/from UTC
        // (see AvailabilityService) — compute "tomorrow" from local now or this fires reminders
        // a day early/late near midnight for any barber off UTC.
        var tomorrow = DateTime.Now.AddDays(1).Date;

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

                await whatsAppSender.SendAsync(appt.Barber, appt.Customer.Phone, message);

                appt.ReminderSent = true;
                sent++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send WhatsApp reminder for appointment {AppointmentId} (barber {BarberId})",
                    appt.Id, appt.BarberId);
                failed++;
            }
        }

        await db.SaveChangesAsync();
        return Ok(new { total = appointments.Count, sent, failed });
    }
}
