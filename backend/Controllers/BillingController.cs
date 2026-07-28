using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;
using System.Security.Claims;

namespace BarberSaas.Api.Controllers;

[ApiController]
[Route("api/billing")]
public class BillingController(AppDbContext db, IConfiguration config, ILogger<BillingController> logger) : ControllerBase
{
    private string BarberId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpPost("checkout-session")]
    [Authorize(Policy = "BarberOnly")]
    public async Task<IActionResult> CreateCheckoutSession()
    {
        var secretKey = config["Stripe:SecretKey"];
        var priceId = config["Stripe:PriceId"];
        if (string.IsNullOrEmpty(secretKey) || string.IsNullOrEmpty(priceId))
            return StatusCode(503, new { error = "Payments are not yet configured. Please contact support." });

        var barber = await db.Barbers.FindAsync(BarberId);
        if (barber is null) return NotFound();

        var appUrl = config["AppUrl"] ?? "";
        var options = new SessionCreateOptions
        {
            Mode = "subscription",
            LineItems = [new SessionLineItemOptions { Price = priceId, Quantity = 1 }],
            SuccessUrl = $"{appUrl}/admin/settings?billing=success",
            CancelUrl = $"{appUrl}/admin/settings?billing=cancelled",
            ClientReferenceId = barber.Id,
        };
        // Reuse the existing Stripe customer on repeat visits instead of letting Stripe create a
        // new one every time the barber clicks "Subscribe Now" (e.g. after cancelling checkout).
        if (barber.StripeCustomerId is not null)
            options.Customer = barber.StripeCustomerId;
        else
            options.CustomerEmail = barber.Email;

        var session = await new SessionService().CreateAsync(options);
        return Ok(new { url = session.Url });
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        var webhookSecret = config["Stripe:WebhookSecret"];
        if (string.IsNullOrEmpty(webhookSecret))
            return StatusCode(503, new { error = "Webhook is not yet configured." });

        var json = await new StreamReader(Request.Body).ReadToEndAsync();
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(json, Request.Headers["Stripe-Signature"], webhookSecret);
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Stripe webhook signature verification failed");
            return BadRequest();
        }

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
            {
                if (stripeEvent.Data.Object is Session session)
                {
                    var barber = await ResolveBarber(session.ClientReferenceId, session.CustomerId);
                    if (barber is not null)
                    {
                        barber.StripeCustomerId = session.CustomerId;
                        barber.StripeSubscriptionId = session.SubscriptionId;
                        barber.SubscriptionStatus = SubStatus.ACTIVE;
                        await db.SaveChangesAsync();
                    }
                }
                break;
            }
            case "customer.subscription.deleted":
            case "invoice.payment_failed":
            {
                var customerId = stripeEvent.Data.Object switch
                {
                    Subscription sub => sub.CustomerId,
                    Invoice inv => inv.CustomerId,
                    _ => null,
                };
                if (customerId is not null)
                {
                    var barber = await db.Barbers.FirstOrDefaultAsync(b => b.StripeCustomerId == customerId);
                    if (barber is not null)
                    {
                        barber.SubscriptionStatus = SubStatus.EXPIRED;
                        await db.SaveChangesAsync();
                    }
                }
                break;
            }
            // Any other event type: acknowledge with 2xx so Stripe doesn't retry -- we simply
            // don't act on it.
        }

        return Ok();
    }

    private async Task<Barber?> ResolveBarber(string? clientReferenceId, string? customerId)
    {
        if (!string.IsNullOrEmpty(clientReferenceId))
        {
            var barber = await db.Barbers.FindAsync(clientReferenceId);
            if (barber is not null) return barber;
        }
        if (!string.IsNullOrEmpty(customerId))
            return await db.Barbers.FirstOrDefaultAsync(b => b.StripeCustomerId == customerId);
        return null;
    }
}
