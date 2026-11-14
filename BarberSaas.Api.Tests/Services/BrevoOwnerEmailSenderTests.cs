using System.Net;
using System.Text.Json;
using BarberSaas.Api.Services;
using Microsoft.Extensions.Configuration;

namespace BarberSaas.Api.Tests.Services;

public class BrevoOwnerEmailSenderTests
{
    private class StubHandler(HttpStatusCode status, string responseBody = "{}") : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody) };
        }
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static IConfiguration FullConfig() => Config(
        ("OwnerEmail:Provider", "brevo"), ("Brevo:ApiKey", "key"), ("Brevo:FromName", Brand.Name),
        ("OwnerEmail:FromEmail", "hello@esayweek.co.il"), ("OwnerEmail:ReplyTo", "owner@example.com"));

    [Fact]
    public async Task Send_uses_the_domain_sender_and_reply_to()
    {
        var handler = new StubHandler(HttpStatusCode.Created);
        var health = new OwnerEmailHealth();
        var sender = new BrevoOwnerEmailSender(new HttpClient(handler), FullConfig(), health);

        await sender.SendAsync("to@example.com", "Subject", "Body");

        Assert.Equal("https://api.brevo.com/v3/smtp/email", handler.Request!.RequestUri!.ToString());
        Assert.Equal("key", handler.Request.Headers.GetValues("api-key").Single());
        var json = JsonDocument.Parse(handler.RequestBody!).RootElement;
        Assert.Equal("hello@esayweek.co.il", json.GetProperty("sender").GetProperty("email").GetString());
        Assert.Equal(Brand.Name, json.GetProperty("sender").GetProperty("name").GetString());
        Assert.Equal("owner@example.com", json.GetProperty("replyTo").GetProperty("email").GetString());
        Assert.Equal("to@example.com", json.GetProperty("to")[0].GetProperty("email").GetString());
        Assert.Equal("ok", health.GetSnapshot().Status);
    }

    [Fact]
    public async Task Send_without_a_reply_to_leaves_the_field_out()
    {
        var handler = new StubHandler(HttpStatusCode.Created);
        var config = Config(("Brevo:ApiKey", "key"), ("OwnerEmail:FromEmail", "hello@esayweek.co.il"));
        var sender = new BrevoOwnerEmailSender(new HttpClient(handler), config, new OwnerEmailHealth());

        await sender.SendAsync("to@example.com", "Subject", "Body");

        Assert.False(JsonDocument.Parse(handler.RequestBody!).RootElement.TryGetProperty("replyTo", out _));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "{\"message\":\"email is not valid in to\"}", false)]
    [InlineData(HttpStatusCode.BadRequest, "{\"message\":\"Sender not valid\"}", true)]
    [InlineData(HttpStatusCode.Unauthorized, "{\"message\":\"unrecognised IP address\"}", true)]
    [InlineData(HttpStatusCode.InternalServerError, "{}", true)]
    public async Task A_failed_send_throws_and_marks_the_channel_only_when_every_email_would_fail(
        HttpStatusCode status, string body, bool channelBroken)
    {
        var health = new OwnerEmailHealth();
        var sender = new BrevoOwnerEmailSender(new HttpClient(new StubHandler(status, body)), FullConfig(), health);

        await Assert.ThrowsAsync<HttpRequestException>(() => sender.SendAsync("to@example.com", "Subject", "Body"));

        Assert.Equal(channelBroken, health.GetSnapshot().Status == "failing");
    }

    [Fact]
    public async Task CheckAuth_throws_when_Brevo_refuses_the_key()
    {
        var sender = new BrevoOwnerEmailSender(
            new HttpClient(new StubHandler(HttpStatusCode.Unauthorized)), FullConfig(), new OwnerEmailHealth());

        await Assert.ThrowsAsync<HttpRequestException>(sender.CheckAuthAsync);
    }

    [Fact]
    public void Is_selected_only_when_turned_on_and_fully_configured()
    {
        Assert.True(BrevoOwnerEmailSender.IsSelected(FullConfig()));
        Assert.False(BrevoOwnerEmailSender.IsSelected(Config(("Brevo:ApiKey", "key"), ("OwnerEmail:FromEmail", "a@b.co"))));
        Assert.False(BrevoOwnerEmailSender.IsSelected(Config(("OwnerEmail:Provider", "brevo"), ("Brevo:ApiKey", "key"))));
        Assert.False(BrevoOwnerEmailSender.IsSelected(Config(("OwnerEmail:Provider", "brevo"), ("OwnerEmail:FromEmail", "a@b.co"))));
    }
}
