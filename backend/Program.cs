using System.Text;
using BarberSaas.Api;
using BarberSaas.Api.Data;
using BarberSaas.Api.Filters;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(opt =>
{
    opt.Filters.Add<ActivityLogFilter>();
    opt.Filters.Add<RequirePasswordChangeFilter>();
    opt.Filters.Add<BlockDisabledBusinessFilter>();
    opt.Filters.Add<ValidateInputFilter>();
    opt.Filters.Add<DispatchNotificationsFilter>();
    // Before ClientErrorResultFilter (-2000): see NotFoundBodyFilter.
    opt.Filters.Add<NotFoundBodyFilter>(NotFoundBodyFilter.FilterOrder);
    opt.Filters.Add<TranslateErrorFilter>();
}).ConfigureApiBehaviorOptions(opt =>
{
    // A missing field, a missing file or a wrong JSON type used to come back as English
    // ProblemDetails with no `error` key, which neither the frontend nor TranslateErrorFilter
    // reads (QA run 3). `errors` names the fields only: the framework's own messages are English
    // and some carry our DTO type names ("could not be converted to BarberSaas.Api.DTOs...", QA run 4).
    opt.InvalidModelStateResponseFactory = ctx => new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new
    {
        error = "Invalid request",
        errors = ctx.ModelState.Where(e => e.Value?.Errors.Count > 0)
            .Select(e => e.Key.StartsWith("$.") ? e.Key[2..] : e.Key)
            .Where(k => k.Length > 0 && k != "req" && k != "$")
            .Distinct()
            .ToDictionary(k => k, _ => "invalid"),
    });
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

var jwtSecret = builder.Configuration["Jwt:Secret"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        };
    });

builder.Services.AddAuthorization(opt =>
{
    opt.AddPolicy("CustomerOnly", p => p.RequireAuthenticatedUser().RequireClaim("type", "customer"));
    // Business JWTs are the only ones without a "type" claim (customer and platform_admin both
    // carry one) -- checking only != "customer" used to let a platform-admin login act as an owner.
    opt.AddPolicy("BusinessOnly", p => p.RequireAuthenticatedUser().RequireAssertion(ctx =>
        ctx.User.FindFirst("type") is null));
    opt.AddPolicy("PlatformAdminOnly", p => p.RequireAuthenticatedUser().RequireClaim("type", "platform_admin"));
});
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<CustomerJwtService>();
builder.Services.AddScoped<PlatformAdminJwtService>();
builder.Services.AddScoped<AvailabilityService>();
builder.Services.AddScoped<RecurringAppointmentService>();
builder.Services.AddScoped<FollowService>();
builder.Services.AddScoped<ReviewService>();
builder.Services.AddScoped<SchedulePresetService>();
// WhatsApp chatbot transport: self-hosted Baileys via whatsapp-bridge (see BridgeWhatsAppSender) --
// TwilioWhatsAppSender is kept in the codebase, unregistered, as the fallback path if a real
// registered business + Trust Hub approval ever happens later.
builder.Services.AddHttpClient<IWhatsAppBridgeClient, WhatsAppBridgeClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["WhatsAppBridge:Url"] ?? "http://localhost:3001");
    client.DefaultRequestHeaders.Add("X-Bridge-Secret", builder.Configuration["WhatsAppBridge:Secret"] ?? "");
    // Sends happen inside customer-facing requests (cancel, book) -- a hung bridge used to keep the
    // customer's button spinning for the default 100s. A healthy send takes about a second.
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddScoped<IWhatsAppSender, BridgeWhatsAppSender>();
// Optional LLM layer on top of the WhatsApp chatbot (see WhatsAppController.ProcessMessageAsync) --
// only ever invoked when OpenAI:ApiKey is configured; falls back to the rule-based flow otherwise
// or if a call throws, so this registration is unconditional (cheap, no I/O at construction time).
builder.Services.AddScoped<IOpenAiChatClient, OpenAiChatClient>();
// Customer login OTP is sent via SMS (not WhatsApp -- this runs before any business is
// identified), using a dedicated platform SMS number separate from any business's WhatsApp
// sender. See TwilioOtpSender.
if (!string.IsNullOrEmpty(builder.Configuration["Twilio:FromNumber"]))
    builder.Services.AddScoped<IOtpSender, TwilioOtpSender>();
else
    builder.Services.AddScoped<IOtpSender, DevOtpSender>();
builder.Services.AddScoped<WaitlistService>();
// Off in the integration tests (TestWebApplicationFactory) so a timer tick can't send on its own
// mid-test; they call WaitlistService.AdvanceQueues through the cron endpoint instead.
if (builder.Configuration.GetValue("Waitlist:WorkerEnabled", true))
    builder.Services.AddHostedService<WaitlistQueueWorker>();
// Off in tests too; ConversationStateCleanupTests call DeleteExpiredAsync directly.
if (builder.Configuration.GetValue("ConversationCleanup:WorkerEnabled", true))
    builder.Services.AddHostedService<ConversationStateCleanupWorker>();
builder.Services.AddScoped<AppointmentCancellationService>();
builder.Services.AddScoped<CustomerNotifier>();
// Best-effort WhatsApp/email sends run after the request instead of inside it (see NotificationQueue).
builder.Services.AddSingleton<NotificationQueue>();
builder.Services.AddScoped<NotificationOutbox>();
if (!builder.Configuration.GetValue("Notifications:Inline", false))
    builder.Services.AddHostedService<NotificationWorker>();
builder.Services.AddScoped<WhatsAppBookingTokenService>();
builder.Services.AddScoped<ServiceListPrompt>();
builder.Services.AddScoped<ChatTemplates>();
builder.Services.AddScoped<WhatsAppLinkingService>();
// Email delivery, in order of precedence: Brevo (Brevo:ApiKey set -- the only option that can
// reach arbitrary recipients without a verified domain, see BrevoEmailSender), then SMTP (e.g.
// Gmail with an app password -- note this needs Railway's Pro plan or a non-Railway host, since
// Railway blocks outbound SMTP ports 25/465/587/2525 on lower plans), then Resend (limited to the
// account owner's own address without a verified domain), otherwise the no-op dev sender that
// just logs. Config comes from dotnet user-secrets locally / env vars in production. Only one
// sender is ever active; environments with none configured (including the test suite) fall
// through to DevEmailSender.
if (!string.IsNullOrEmpty(builder.Configuration["Brevo:ApiKey"]))
    builder.Services.AddHttpClient<IEmailSender, BrevoEmailSender>();
else if (!string.IsNullOrEmpty(builder.Configuration["Smtp:Username"]) &&
    !string.IsNullOrEmpty(builder.Configuration["Smtp:Password"]))
    builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
else if (!string.IsNullOrEmpty(builder.Configuration["Resend:ApiKey"]))
    builder.Services.AddHttpClient<IEmailSender, ResendEmailSender>();
else
    builder.Services.AddScoped<IEmailSender, DevEmailSender>();

builder.Services.AddHttpClient<ICardcomService, CardcomService>();

// Powers PlatformAdminController's owner-email composer (see IOwnerEmailSender) -- separate from
// the IEmailSender chain above, which handles system emails only. Always registered (its own
// constructor needs no config at construction time); the controller checks Gmail:ClientId/
// ClientSecret/RefreshToken are all set before calling it, same pattern as CardcomService/
// BillingController for a not-yet-configured integration.
builder.Services.AddHttpClient<IOwnerEmailSender, GmailApiEmailSender>();
// Watches that Gmail channel and alerts the platform admin (via the IEmailSender chain above) when
// it breaks -- see OwnerEmailHealth. Worker off in the tests, same as the waitlist worker.
builder.Services.AddSingleton<OwnerEmailHealth>();
builder.Services.AddScoped<OwnerEmailMonitor>();
builder.Services.AddScoped<PlatformAdminAlerts>();
if (builder.Configuration.GetValue("OwnerEmailHealth:WorkerEnabled", true))
    builder.Services.AddHostedService<OwnerEmailHealthWorker>();

builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p => p
        .WithOrigins((builder.Configuration["AllowedOrigin"] ?? "http://localhost:5173")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .AllowAnyHeader()
        .AllowAnyMethod()));

builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddSingleton<RequestQuota>();
builder.Services.AddSingleton<AiUsageLimiter>();
builder.Services.AddSingleton<BookingRules>();

var app = builder.Build();

var uploadsPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "uploads");
Directory.CreateDirectory(uploadsPath);

// Railway's proxy sits in front of the app, so RemoteIpAddress is the proxy's -- see ClientIp.
app.Use((context, next) =>
{
    var ip = ClientIp.Resolve(context.Request.Headers);
    if (ip is not null) context.Connection.RemoteIpAddress = ip;
    // Raw proxy headers on sign-in requests, to check what Railway sends when the IPs look wrong.
    if (context.Request.Path.Value?.EndsWith("/login", StringComparison.OrdinalIgnoreCase) == true)
        app.Logger.LogInformation("Login request IP headers: X-Forwarded-For={Xff} X-Real-IP={RealIp} resolved={Ip}",
            context.Request.Headers["X-Forwarded-For"].ToString(), context.Request.Headers["X-Real-IP"].ToString(), ip);
    return next(context);
});
app.UseExceptionHandler();
// Every response is JSON or an uploaded file: never sniffed as something else, never framed
// (QA run 7: uploads were served without nosniff).
app.Use((context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    return next(context);
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsPath),
    RequestPath = "/api/uploads",
});
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsNpgsql())
        db.Database.Migrate();
}

app.Run();

public partial class Program { }
