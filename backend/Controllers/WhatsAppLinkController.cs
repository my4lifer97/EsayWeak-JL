using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace BarberSaas.Api.Controllers;

// Public, token-secured companion to PlatformAdminController's WhatsApp-linking endpoints -- lets
// a business owner complete the QR scan themselves (on their own screen, with their own phone)
// from a link the platform admin hands off, instead of the admin relaying a screenshot that goes
// stale within seconds. The admin still initiates every link session (see
// PlatformAdminController.CreateWhatsAppLinkToken); this only removes the admin as a manual relay
// for the scan step itself. No auth beyond the opaque token -- see WhatsAppLinkToken.
[ApiController]
[Route("api/wa-link")]
public class WhatsAppLinkController(WhatsAppLinkingService linking, IWhatsAppBridgeClient bridge) : ControllerBase
{
    [HttpGet("{token}")]
    public async Task<IActionResult> GetLinkStatus(string token)
    {
        var linkToken = await linking.TryResolveTokenAsync(token);
        if (linkToken is null) return NotFound(new { error = "This link has expired or is invalid." });

        var b = linkToken.Business;
        // Ensure a session is actually running -- the admin's own "Link WhatsApp" click isn't a
        // prerequisite for this flow, the owner can be the first (and only) one to open the link.
        var initial = await bridge.GetStatusAsync(b.Id);
        var status = initial.State == "disconnected" ? await bridge.StartLinkAsync(b.Id) : initial;
        status = await linking.RecordIfConnectedAsync(
            b, status, linkToken.CreatedByPlatformAdminId, Request.Method, Request.Path.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new { businessName = b.Name, language = b.Language.ToString(), status.State, status.Qr, status.PhoneNumber });
    }
}
