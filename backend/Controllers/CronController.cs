using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Controllers;

[ApiController]
[Route("api/cron")]
public class CronController(AppDbContext db, IConfiguration config, ILogger<CronController> logger, RecurringAppointmentService recurringAppointments, IWhatsAppSender whatsAppSender, ICardcomService cardcom, WaitlistService waitlist) : ControllerBase
{
    [HttpGet("retry-waitlist-notifications")]
    public async Task<IActionResult> RetryWaitlistNotifications()
    {
        var cronSecret = config["CronSecret"];
        var auth = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(cronSecret) || auth != $"Bearer {cronSecret}")
            return Unauthorized(new { error = "Unauthorized" });

        var (total, sent, failed) = await waitlist.RetryFailedNotifications();
        await db.SaveChangesAsync();

        return Ok(new { total, sent, failed });
    }


    [HttpGet("charge-subscriptions")]
    public async Task<IActionResult> ChargeSubscriptions()
    {
        var cronSecret = config["CronSecret"];
        var auth = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(cronSecret) || auth != $"Bearer {cronSecret}")
            return Unauthorized(new { error = "Unauthorized" });

        var amount = decimal.Parse(config["Cardcom:MonthlyAmount"] ?? "120");
        var now = DateTime.UtcNow;

        var due = await db.Businesses
            .Where(b => b.SubscriptionStatus == SubStatus.ACTIVE
                && b.CardcomToken != null
                && b.CardcomNextChargeAt != null
                && b.CardcomNextChargeAt <= now)
            .ToListAsync();

        int charged = 0, failed = 0;
        foreach (var business in due)
        {
            try
            {
                var result = await cardcom.ChargeByTokenAsync(business.CardcomToken!, amount, "Business SaaS Monthly Subscription");
                if (result.ResponseCode == 0)
                {
                    business.CardcomNextChargeAt = business.CardcomNextChargeAt!.Value.AddMonths(1);
                    charged++;
                }
                else
                {
                    logger.LogWarning("Cardcom recurring charge failed for business {BusinessId}: {Code} {Description}",
                        business.Id, result.ResponseCode, result.Description);
                    business.SubscriptionStatus = SubStatus.EXPIRED;
                    failed++;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Cardcom recurring charge threw for business {BusinessId}", business.Id);
                business.SubscriptionStatus = SubStatus.EXPIRED;
                failed++;
            }
        }

        await db.SaveChangesAsync();
        return Ok(new { total = due.Count, charged, failed });
    }

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

        var appUrl = config["AppUrl"] ?? "";

        // a.Date is the business's local wall-clock calendar date, never converted to/from UTC
        // (see AvailabilityService) — compute "tomorrow"/"now" from local time or this fires
        // reminders a day early/late (or at the wrong hour) for any business off UTC.
        var tomorrow = DateTime.Now.AddDays(1).Date;
        var dayBeforeAppointments = await db.Appointments
            .Include(a => a.Business)
            .Include(a => a.Customer)
            .Include(a => a.Item)
            .Where(a => a.Date == tomorrow && a.Status == AppointmentStatus.CONFIRMED && !a.ReminderSent)
            .ToListAsync();

        var (dayBeforeSent, dayBeforeFailed) = await SendBatchAsync(dayBeforeAppointments, "reminder.message", appUrl,
            (appt) => appt.ReminderSent = true);

        // "Soon" window: local start time (Date + StartTime) has just come within 3 hours of now,
        // and hasn't fired yet -- checked every run (this endpoint is called every 15 min by the
        // GitHub Actions schedule), so an appointment is caught the first run after it enters the
        // window and never sent twice thanks to ReminderSentSoon.
        var now = DateTime.Now;
        var in3Hours = now.AddHours(3);
        var soonCandidates = await db.Appointments
            .Include(a => a.Business)
            .Include(a => a.Customer)
            .Include(a => a.Item)
            .Where(a => a.Status == AppointmentStatus.CONFIRMED && !a.ReminderSentSoon
                && a.Date >= now.Date && a.Date <= in3Hours.Date)
            .ToListAsync();
        var soonAppointments = soonCandidates
            .Where(a => a.Date.Date.Add(TimeSpan.Parse(a.StartTime)) is var start && start >= now && start <= in3Hours)
            .ToList();

        var (soonSent, soonFailed) = await SendBatchAsync(soonAppointments, "reminder.message.soon", appUrl,
            (appt) => appt.ReminderSentSoon = true);

        await db.SaveChangesAsync();
        return Ok(new
        {
            dayBefore = new { total = dayBeforeAppointments.Count, sent = dayBeforeSent, failed = dayBeforeFailed },
            soon = new { total = soonAppointments.Count, sent = soonSent, failed = soonFailed },
        });
    }

    private async Task<(int sent, int failed)> SendBatchAsync(List<Appointment> appointments, string messageKey, string appUrl, Action<Appointment> markSent)
    {
        int sent = 0, failed = 0;

        foreach (var appt in appointments)
        {
            if (appt.Business.WhatsAppNumber is null)
                continue;

            try
            {
                var lang = appt.Business.Language.ToString();
                var itemName = lang switch
                {
                    "AR" => appt.Item.NameAr,
                    "HE" => appt.Item.NameHe,
                    _ => appt.Item.NameEn,
                };

                var cancelUrl = $"{appUrl}/{appt.Business.Slug}/appointments/{appt.Id}?token={appt.CancelToken}";
                var message = I18nService.T(lang, messageKey, new()
                {
                    ["customerName"] = appt.Customer.Name,
                    ["businessName"] = appt.Business.Name,
                    ["time"] = appt.StartTime,
                    ["service"] = itemName,
                    ["cancelUrl"] = cancelUrl,
                });

                await whatsAppSender.SendAsync(appt.Business, appt.Customer.Phone, message);

                markSent(appt);
                sent++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send WhatsApp reminder ({MessageKey}) for appointment {AppointmentId} (business {BusinessId})",
                    messageKey, appt.Id, appt.BusinessId);
                failed++;
            }
        }

        return (sent, failed);
    }
}
