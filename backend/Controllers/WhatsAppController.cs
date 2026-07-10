using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Twilio.Security;

namespace BarberSaas.Api.Controllers;

[ApiController]
[Route("api/whatsapp")]
public class WhatsAppController(AppDbContext db, IConfiguration config) : ControllerBase
{
    private static readonly string[] BookKeywords = ["book", "שריין", "תור", "موعد", "حجز", "appointment"];
    private static readonly string[] CancelKeywords = ["cancel", "ביטול", "بطل", "إلغاء", "בטל"];
    private static readonly string[] RescheduleKeywords = ["reschedule", "שינוי", "تغيير", "שנה"];

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync();
        var form = System.Web.HttpUtility.ParseQueryString(rawBody);
        var parms = form.AllKeys.Where(k => k is not null).ToDictionary(k => k!, k => form[k] ?? "");

        var toNumber = parms.GetValueOrDefault("To", "").Replace("whatsapp:", "");
        var barber = await db.Barbers
            .Where(b => b.TwilioNumber == toNumber)
            .Select(b => new { b.Id, b.Name, b.Slug, b.Language, b.TwilioToken })
            .FirstOrDefaultAsync();

        if (barber?.TwilioToken is null)
            return NotFound("Not configured");

        var signature = Request.Headers["X-Twilio-Signature"].FirstOrDefault() ?? "";
        var appUrl = config["AppUrl"] ?? "";
        // AppUrl is the frontend's public URL, reused here on the assumption frontend and backend
        // share a domain in production. WebhookPublicUrl overrides just the signature-check base
        // URL for setups where that's not true (e.g. a local ngrok tunnel pointed at the backend
        // only, while AppUrl keeps pointing at the frontend for booking links in reply text).
        var webhookUrl = $"{config["WebhookPublicUrl"] ?? appUrl}/api/whatsapp/webhook";

        var validator = new RequestValidator(barber.TwilioToken);
        if (!validator.Validate(webhookUrl, parms, signature))
            return StatusCode(403, "Invalid signature");

        var incomingMsg = parms.GetValueOrDefault("Body", "").ToLowerInvariant().Trim();
        var fromPhone = parms.GetValueOrDefault("From", "").Replace("whatsapp:", "");
        var bookingUrl = $"{appUrl}/{barber.Slug}/book";
        var lang = barber.Language.ToString();

        string reply;
        if (BookKeywords.Any(k => incomingMsg.Contains(k)))
        {
            reply = I18nService.T(lang, "whatsapp.bookingLink", new() { ["url"] = bookingUrl });
        }
        else if (CancelKeywords.Any(k => incomingMsg.Contains(k)))
        {
            var customer = await db.Customers
                // a.Date is a calendar date (local wall-clock, never UTC-converted), so compare
                // against local "today" as a date — not DateTime.UtcNow, which is both the wrong
                // clock and, being a timestamp rather than a date, would already exclude today's
                // appointments as soon as any time had passed since UTC midnight.
                .Include(c => c.Appointments.Where(a => a.Status == AppointmentStatus.CONFIRMED && a.Date >= DateTime.Now.Date))
                .FirstOrDefaultAsync(c => c.BarberId == barber.Id && c.Phone == fromPhone);

            var upcoming = customer?.Appointments
                .Where(a => AppointmentStatusHelper.EffectiveStatus(a.Status, a.Date, a.EndTime) == "CONFIRMED")
                .OrderBy(a => a.Date)
                .FirstOrDefault();
            if (upcoming is null)
            {
                reply = I18nService.T(lang, "whatsapp.noAppointment", new() { ["url"] = bookingUrl });
            }
            else
            {
                upcoming.Status = AppointmentStatus.CANCELLED;
                await db.SaveChangesAsync();
                reply = I18nService.T(lang, "whatsapp.cancelled", new()
                {
                    ["date"] = upcoming.Date.ToString("yyyy-MM-dd"),
                    ["time"] = upcoming.StartTime,
                });
            }
        }
        else if (RescheduleKeywords.Any(k => incomingMsg.Contains(k)))
        {
            reply = I18nService.T(lang, "whatsapp.rescheduleLink", new() { ["url"] = bookingUrl });
        }
        else
        {
            reply = I18nService.T(lang, "whatsapp.menu", new() { ["barberName"] = barber.Name });
        }

        var twiml = $"""<?xml version="1.0" encoding="UTF-8"?><Response><Message>{System.Net.WebUtility.HtmlEncode(reply)}</Message></Response>""";
        return Content(twiml, "text/xml");
    }
}
