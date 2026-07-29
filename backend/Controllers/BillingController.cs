using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BarberSaas.Api.Controllers;

[ApiController]
[Route("api/billing")]
public class BillingController(AppDbContext db, IConfiguration config, ICardcomService cardcom, ILogger<BillingController> logger) : ControllerBase
{
    private string BarberId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpPost("checkout-session")]
    [Authorize(Policy = "BarberOnly")]
    public async Task<IActionResult> CreateCheckoutSession()
    {
        var terminalNumber = config["Cardcom:TerminalNumber"];
        var apiName = config["Cardcom:ApiName"];
        if (string.IsNullOrEmpty(terminalNumber) || string.IsNullOrEmpty(apiName))
            return StatusCode(503, new { error = "Payments are not yet configured. Please contact support." });

        var barber = await db.Barbers.FindAsync(BarberId);
        if (barber is null) return NotFound();

        var appUrl = config["AppUrl"] ?? "";
        var backendUrl = config["BackendUrl"] ?? "";
        var amount = decimal.Parse(config["Cardcom:MonthlyAmount"] ?? "120");

        var result = await cardcom.CreateLowProfileAsync(new CardcomLowProfileCreateParams(
            Amount: amount,
            ProductName: "Barber SaaS Monthly Subscription",
            SuccessRedirectUrl: $"{appUrl}/admin/settings?billing=success",
            FailedRedirectUrl: $"{appUrl}/admin/settings?billing=cancelled",
            WebHookUrl: $"{backendUrl}/api/billing/webhook",
            ReturnValue: barber.Id));

        if (result.ResponseCode != 0 || string.IsNullOrEmpty(result.Url))
        {
            logger.LogError("Cardcom LowProfile/Create failed: {Code} {Description}", result.ResponseCode, result.Description);
            return StatusCode(502, new { error = "Could not start checkout. Please try again." });
        }

        return Ok(new { url = result.Url });
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        var terminalNumber = config["Cardcom:TerminalNumber"];
        if (string.IsNullOrEmpty(terminalNumber))
            return StatusCode(503, new { error = "Webhook is not yet configured." });

        // Cardcom's webhook isn't HMAC-signed like Stripe's, so the inbound POST is treated purely
        // as a trigger ("something happened for this LowProfileId") -- exact delivery shape
        // (query string vs. form field) is a best guess, checked both ways below. The handler
        // never acts on fields taken directly from this request; it re-fetches the verified
        // result from Cardcom server-to-server before mutating any billing state.
        var lowProfileId = Request.Query["LowProfileId"].FirstOrDefault()
            ?? Request.Form["LowProfileId"].FirstOrDefault();
        if (string.IsNullOrEmpty(lowProfileId))
        {
            logger.LogWarning("Cardcom webhook received without a LowProfileId");
            return Ok(); // ack anyway -- nothing we can look up
        }

        var verified = await cardcom.GetLowProfileResultAsync(lowProfileId);
        if (verified.ResponseCode != 0 || string.IsNullOrEmpty(verified.ReturnValue))
        {
            logger.LogWarning("Cardcom LowProfile {Id} verification failed or had no ReturnValue: {Code} {Description}",
                lowProfileId, verified.ResponseCode, verified.Description);
            return Ok();
        }

        var barber = await db.Barbers.FindAsync(verified.ReturnValue);
        if (barber is null) return Ok();

        // Idempotency: ignore a duplicate webhook delivery for a LowProfileId already processed.
        if (barber.CardcomLastLowProfileId == lowProfileId) return Ok();

        if (!string.IsNullOrEmpty(verified.TokenNumber))
        {
            barber.CardcomToken = verified.TokenNumber;
            barber.SubscriptionStatus = SubStatus.ACTIVE;
            barber.CardcomNextChargeAt = DateTime.UtcNow.AddMonths(1);
        }
        barber.CardcomLastLowProfileId = lowProfileId;
        await db.SaveChangesAsync();

        return Ok();
    }
}
