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
    opt.AddPolicy("BusinessOnly", p => p.RequireAuthenticatedUser().RequireAssertion(ctx =>
        ctx.User.FindFirst("type")?.Value != "customer"));
    opt.AddPolicy("PlatformAdminOnly", p => p.RequireAuthenticatedUser().RequireClaim("type", "platform_admin"));
});
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<CustomerJwtService>();
builder.Services.AddScoped<PlatformAdminJwtService>();
builder.Services.AddScoped<AvailabilityService>();
builder.Services.AddScoped<RecurringAppointmentService>();
builder.Services.AddScoped<FollowService>();
builder.Services.AddScoped<ReviewService>();
builder.Services.AddScoped<IWhatsAppSender, TwilioWhatsAppSender>();
// Customer login OTP is sent via SMS (not WhatsApp -- this runs before any business is
// identified), using a dedicated platform SMS number separate from any business's WhatsApp
// sender. See TwilioOtpSender.
if (!string.IsNullOrEmpty(builder.Configuration["Twilio:FromNumber"]))
    builder.Services.AddScoped<IOtpSender, TwilioOtpSender>();
else
    builder.Services.AddScoped<IOtpSender, DevOtpSender>();
builder.Services.AddScoped<WaitlistService>();
builder.Services.AddScoped<AppointmentCancellationService>();
builder.Services.AddScoped<WhatsAppBookingTokenService>();
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

builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p => p
        .WithOrigins((builder.Configuration["AllowedOrigin"] ?? "http://localhost:5173")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .AllowAnyHeader()
        .AllowAnyMethod()));

var app = builder.Build();

var uploadsPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "uploads");
Directory.CreateDirectory(uploadsPath);

app.UseExceptionHandler();
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
