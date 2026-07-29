using System.Net.Http.Json;

namespace BarberSaas.Api.Services;

// Real Cardcom Low-Profile API v11 client (https://secure.cardcom.solutions/api/v11/...).
// Registered via IHttpClientFactory in Program.cs (same pattern as ResendEmailSender) so tests
// substitute a fake ICardcomService instead of hitting the real network.
//
// Several exact JSON field names below are best-effort reconstructions -- Cardcom's docs are a
// JS SPA that couldn't be scraped for this implementation. Each uncertain spot is flagged.
// Verify against https://secure.cardcom.solutions/Api/v11/Docs or the Postman collection, and
// smoke-test against the public sandbox (Terminal 1000 / ApiName "demo" / card
// 4580000000000000, any future expiry, CVV 123) before relying on this in production.
public class CardcomService(HttpClient http, IConfiguration config) : ICardcomService
{
    private const string BaseUrl = "https://secure.cardcom.solutions/api/v11";

    public async Task<CardcomLowProfileCreateResult> CreateLowProfileAsync(CardcomLowProfileCreateParams p)
    {
        var terminalNumber = int.Parse(config["Cardcom:TerminalNumber"]!);
        var apiName = config["Cardcom:ApiName"]!;

        // ApiPassword is deliberately NOT sent here -- per research it's required for
        // RefundByTransactionId/CreateDocument/the recurring charge call, but not for a normal
        // LowProfile/Create.
        var response = await http.PostAsJsonAsync($"{BaseUrl}/LowProfile/Create", new
        {
            TerminalNumber = terminalNumber,
            ApiName = apiName,
            Operation = "ChargeAndCreateToken",
            Amount = p.Amount,
            ISOCoinId = 1, // ILS
            SuccessRedirectUrl = p.SuccessRedirectUrl,
            FailedRedirectUrl = p.FailedRedirectUrl,
            WebHookUrl = p.WebHookUrl,
            ReturnValue = p.ReturnValue,
            ProductName = p.ProductName,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CardcomLowProfileCreateResult>())!;
    }

    public async Task<CardcomLowProfileResult> GetLowProfileResultAsync(string lowProfileId)
    {
        var terminalNumber = int.Parse(config["Cardcom:TerminalNumber"]!);
        var apiName = config["Cardcom:ApiName"]!;

        // Endpoint path/verb is a best guess -- some sources describe this as GetLpResult. Verify
        // the exact path, and whether ApiPassword is required here (unlike Create), against real
        // docs once sandbox credentials exist.
        var response = await http.PostAsJsonAsync($"{BaseUrl}/LowProfile/GetLpResult", new
        {
            TerminalNumber = terminalNumber,
            ApiName = apiName,
            LowProfileId = lowProfileId,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CardcomLowProfileResult>())!;
    }

    public async Task<CardcomChargeResult> ChargeByTokenAsync(string token, decimal amount, string productName)
    {
        var terminalNumber = int.Parse(config["Cardcom:TerminalNumber"]!);
        var apiName = config["Cardcom:ApiName"]!;
        var apiPassword = config["Cardcom:ApiPassword"]!;

        // BEST GUESS endpoint + payload shape -- the least-confirmed part of this integration.
        // Verify against Cardcom's "Transactions/Transaction" (charge-by-token) docs/Postman
        // collection before relying on this for a real recurring charge.
        var response = await http.PostAsJsonAsync($"{BaseUrl}/Transactions/Transaction", new
        {
            TerminalNumber = terminalNumber,
            ApiName = apiName,
            ApiPassword = apiPassword,
            TokenToCharge = new { Token = token },
            Amount = amount,
            ISOCoinId = 1,
            ProductName = productName,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CardcomChargeResult>())!;
    }
}
