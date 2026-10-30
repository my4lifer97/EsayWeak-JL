using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Controllers;

[ApiController]
[Route("api/cron")]
public class CronController(AppDbContext db, IConfiguration config, ILogger<CronController> logger, RecurringAppointmentService recurringAppointments, IWhatsAppSender whatsAppSender, ICardcomService cardcom, WaitlistService waitlist, SchedulePresetService schedulePresets) : ControllerBase
{
    // Daily sweep for PresetSchedule rows: applies any whose StartDate has arrived, reverts any
    // applied one whose EndDate has passed -- see SchedulePresetService.ApplyDueScheduledPresets
    // for the actual mechanics (shared with the owner-triggered endpoints in AdminController).
    [HttpGet("apply-scheduled-presets")]
    public async Task<IActionResult> ApplyScheduledPresets()
    {
        var cronSecret = config["CronSecret"];
        var auth = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(cronSecret) || auth != $"Bearer {cronSecret}")
            return Unauthorized(new { error = "Unauthorized" });

        var (applied, reverted) = await schedulePresets.ApplyDueScheduledPresets();
        return Ok(new { applied, reverted });
    }

    [HttpGet("retry-waitlist-notifications")]
    public async Task<IActionResult> RetryWaitlistNotifications()
    {
        var cronSecret = config["CronSecret"];
        var auth = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(cronSecret) || auth != $"Bearer {cronSecret}")
            return Unauthorized(new { error = "Unauthorized" });

        var (total, sent, failed) = await waitlist.AdvanceQueues();
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
        var tomorrow = AppClock.Now.AddDays(1).Date;
        var dayBeforeAppointments = await db.Appointments
            .Include(a => a.Business)
            .Include(a => a.Customer)
            .Include(a => a.Item)
            // A customer cancellation waiting on the owner's approval stays CONFIRMED internally, but the
            // customer already sees it as cancelled -- no reminder for it.
            // Nor for a business the platform disabled (QA run 6).
            .Where(a => a.Date == tomorrow && a.Status == AppointmentStatus.CONFIRMED && !a.PendingCancellationApproval && !a.ReminderSent
                && !a.Business.IsDisabled)
            // In time order: with two appointments the same day, the question goes with the first
            // one's reminder, not an arbitrary one (QA run 5). Each reminder names its own time.
            .OrderBy(a => a.StartTime)
            .ToListAsync();

        var (dayBeforeSent, dayBeforeFailed) = await SendBatchAsync(dayBeforeAppointments, "reminder.message", appUrl,
            (appt) => appt.ReminderSent = true,
            (id, value) => db.Appointments.Where(a => a.Id == id && a.ReminderSent != value)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.ReminderSent, value)),
            askAttendance: true);
        var sentDayBefore = dayBeforeAppointments.Where(a => a.ReminderSent).Select(a => a.Id).ToHashSet();

        // "Soon" window: local start time (Date + StartTime) has just come within 3 hours of now,
        // and hasn't fired yet -- checked every run (this endpoint is called every 15 min by the
        // GitHub Actions schedule), so an appointment is caught the first run after it enters the
        // window and never sent twice thanks to ReminderSentSoon.
        var now = AppClock.Now;
        var in3Hours = now.AddHours(3);
        var soonCandidates = await db.Appointments
            .Include(a => a.Business)
            .Include(a => a.Customer)
            .Include(a => a.Item)
            .Where(a => a.Status == AppointmentStatus.CONFIRMED && !a.PendingCancellationApproval && !a.ReminderSentSoon
                && !a.Business.IsDisabled && a.Date >= now.Date && a.Date <= in3Hours.Date)
            .ToListAsync();
        // Not one that just got its day-before reminder in this same run: an appointment right
        // after midnight got both at once (QA run 6). Its "soon" one goes on a later run.
        var soonAppointments = soonCandidates
            .Where(a => a.Date.Date.Add(TimeSpan.Parse(a.StartTime)) is var start && start >= now && start <= in3Hours)
            .Where(a => !sentDayBefore.Contains(a.Id))
            .ToList();

        var (soonSent, soonFailed) = await SendBatchAsync(soonAppointments, "reminder.message.soon", appUrl,
            (appt) => appt.ReminderSentSoon = true,
            (id, value) => db.Appointments.Where(a => a.Id == id && a.ReminderSentSoon != value)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.ReminderSentSoon, value)));

        await db.SaveChangesAsync();
        return Ok(new
        {
            dayBefore = new { total = dayBeforeAppointments.Count, sent = dayBeforeSent, failed = dayBeforeFailed },
            soon = new { total = soonAppointments.Count, sent = soonSent, failed = soonFailed },
        });
    }

    // askAttendance: the day-before one ends with "will you come? 1/2" when the customer has no
    // live conversation (AttendanceQuestion), at most one question per phone per business.
    // setSentFlag(id, value): sets the appointment's sent flag in the database only if it isn't
    // already `value`, returning the rows changed. Each reminder is claimed with it before its send,
    // so two overlapping runs (a slow run and the next one, or a retry) can't both send it: they
    // both sent every reminder (QA run 6). A failed send gives the claim back.
    private async Task<(int sent, int failed)> SendBatchAsync(List<Appointment> appointments, string messageKey, string appUrl,
        Action<Appointment> markSent, Func<string, bool, Task<int>> setSentFlag, bool askAttendance = false)
    {
        int sent = 0, failed = 0;
        var asked = new HashSet<(string, string)>();

        // In the language the chatbot last spoke with the customer in (like owner notices and
        // waitlist messages, QA run 4), else the business's chatbot language. A customer the owner
        // typed in may not have their account linked yet, so match by phone too.
        var accountIds = appointments.Select(a => a.Customer.CustomerAccountId).OfType<string>().Distinct().ToList();
        var phones = appointments.Select(a => a.Customer.Phone).Distinct().ToList();
        var accounts = await db.CustomerAccounts
            .Where(a => a.LastMessageLanguage != null && (accountIds.Contains(a.Id) || phones.Contains(a.Phone)))
            .Select(a => new { a.Id, a.Phone, a.LastMessageLanguage })
            .ToListAsync();

        foreach (var appt in appointments)
        {
            if (appt.Business.WhatsAppNumber is null)
                continue;
            if (await setSentFlag(appt.Id, true) == 0)
                continue; // another run has it

            var delivered = false;
            try
            {
                var lang = accounts.FirstOrDefault(a => a.Id == appt.Customer.CustomerAccountId)?.LastMessageLanguage
                    ?? accounts.FirstOrDefault(a => a.Phone == appt.Customer.Phone)?.LastMessageLanguage
                    ?? (appt.Business.ChatbotDefaultLanguage ?? appt.Business.Language).ToString();
                var itemName = lang switch
                {
                    "AR" => appt.Item.NameAr,
                    "HE" => appt.Item.NameHe,
                    _ => appt.Item.NameEn,
                };
                if (string.IsNullOrWhiteSpace(itemName)) itemName = appt.Item.NameEn;

                var cancelUrl = $"{appUrl}/{appt.Business.Slug}/appointments/{appt.Id}?token={appt.CancelToken}";
                var message = I18nService.T(lang, messageKey, new()
                {
                    ["customerName"] = appt.Customer.Name,
                    ["businessName"] = appt.Business.Name,
                    ["time"] = appt.StartTime,
                    ["service"] = itemName,
                    ["cancelUrl"] = cancelUrl,
                });

                var ask = false;
                WhatsAppConversationState? existing = null;
                // Only the chatbot answers it: with the chatbot off (or the business disabled) the
                // customer's "1"/"2" got no reply and changed nothing (QA run 5).
                if (askAttendance && appt.Business.ChatbotEnabled && !appt.Business.IsDisabled
                    && !asked.Contains((appt.BusinessId, appt.Customer.Phone)))
                    (ask, existing) = await AttendanceQuestion.CanAskAsync(db, appt.BusinessId, appt.Customer.Phone);
                if (ask) message += $"\n\n{I18nService.T(lang, "reminder.confirmQuestion")}";

                await whatsAppSender.SendAsync(appt.Business, appt.Customer.Phone, message);
                delivered = true;

                // Saved per reminder, right after its send: one save for the whole run meant any
                // failure left every ReminderSent unsaved, and the next run sent them all again
                // (QA run 5). Parked only once the question really went out.
                markSent(appt);
                var parked = ask ? AttendanceQuestion.Park(db, existing, appt, lang) : null;
                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException) when (parked is not null && existing is null)
                {
                    // The customer wrote in during the run and their conversation row now exists
                    // (unique per business + phone): that conversation wins, the reminder stays sent.
                    db.Entry(parked).State = EntityState.Detached;
                    appt.AttendanceStatus = null;
                    await db.SaveChangesAsync();
                    parked = null;
                }
                if (parked is not null) asked.Add((appt.BusinessId, appt.Customer.Phone));
                sent++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send WhatsApp reminder ({MessageKey}) for appointment {AppointmentId} (business {BusinessId})",
                    messageKey, appt.Id, appt.BusinessId);
                // Not sent: a later run tries again. Sent but not saved: it stays claimed.
                if (!delivered) await setSentFlag(appt.Id, false);
                failed++;
            }
        }

        return (sent, failed);
    }
}
