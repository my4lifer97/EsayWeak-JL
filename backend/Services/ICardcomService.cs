namespace BarberSaas.Api.Services;

public record CardcomLowProfileCreateParams(
    decimal Amount,
    string ProductName,
    string SuccessRedirectUrl,
    string FailedRedirectUrl,
    string WebHookUrl,
    string ReturnValue); // barber.Id -- echoed back so the webhook can resolve the barber directly

// ResponseCode/LowProfileId/Url are corroborated by multiple independent sources for Cardcom's
// LowProfile/Create v11 endpoint.
public record CardcomLowProfileCreateResult(int ResponseCode, string? Description, string? LowProfileId, string? Url);

// ResponseCode/LowProfileId/Amount/ReturnValue are solid. TranzactionId/TokenNumber field NAMES
// specifically are a best-effort reconstruction (Cardcom's docs are a JS SPA that couldn't be
// scraped) -- verify against https://secure.cardcom.solutions/Api/v11/Docs or the Postman
// collection once real sandbox credentials exist, before relying on this in production.
public record CardcomLowProfileResult(
    int ResponseCode,
    string? Description,
    string LowProfileId,
    string? TranzactionId,
    string? TokenNumber,
    decimal? Amount,
    string? ReturnValue);

// Best-guess shape for the recurring per-token charge call -- the least-confirmed part of this
// integration. Verify the endpoint path and request/response field names against real docs before
// go-live.
public record CardcomChargeResult(int ResponseCode, string? Description, string? TranzactionId);

public interface ICardcomService
{
    Task<CardcomLowProfileCreateResult> CreateLowProfileAsync(CardcomLowProfileCreateParams p);
    Task<CardcomLowProfileResult> GetLowProfileResultAsync(string lowProfileId);
    Task<CardcomChargeResult> ChargeByTokenAsync(string token, decimal amount, string productName);
}
