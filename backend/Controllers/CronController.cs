using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Controllers;

[ApiController]
[Route("api/cron")]
public class CronController(AppDbContext db, IConfiguration config, ILogger<CronController> logger, RecurringAppointmentService recurringAppointments, IWhatsAppSender whatsAppSender, ICardcomService cardcom) : ControllerBase
{
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

        // a.Date is the business's local wall-clock calendar date, never converted to/from UTC
        // (see AvailabilityService) — compute "tomorrow" from local now or this fires reminders
        // a day early/late near midnight for any business off UTC.
        var tomorrow = DateTime.Now.AddDays(1).Date;

        var appointments = await db.Appointments
            .Include(a => a.Business)
            .Include(a => a.Customer)
            .Include(a => a.Item)
            .Where(a => a.Date == tomorrow && a.Status == AppointmentStatus.CONFIRMED && !a.ReminderSent)
            .ToListAsync();

        var appUrl = config["AppUrl"] ?? "";
        int sent = 0, failed = 0;

        foreach (var appt in appointments)
        {
            if (appt.Business.TwilioNumber is null)
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
                var message = I18nService.T(lang, "reminder.message", new()
                {
                    ["customerName"] = appt.Customer.Name,
                    ["businessName"] = appt.Business.Name,
                    ["time"] = appt.StartTime,
                    ["service"] = itemName,
                    ["cancelUrl"] = cancelUrl,
                });

                await whatsAppSender.SendAsync(appt.Business, appt.Customer.Phone, message);

                appt.ReminderSent = true;
                sent++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send WhatsApp reminder for appointment {AppointmentId} (business {BusinessId})",
                    appt.Id, appt.BusinessId);
                failed++;
            }
        }

        await db.SaveChangesAsync();
        return Ok(new { total = appointments.Count, sent, failed });
    }
}
