using System.Text;
using BarberSaas.Api;
using BarberSaas.Api.Data;
using BarberSaas.Api.Filters;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(opt => opt.Filters.Add<ActivityLogFilter>());
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
    opt.AddPolicy("BarberOnly", p => p.RequireAuthenticatedUser().RequireAssertion(ctx =>
        ctx.User.FindFirst("type")?.Value != "customer"));
    opt.AddPolicy("PlatformAdminOnly", p => p.RequireAuthenticatedUser().RequireClaim("type", "platform_admin"));
});
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<CustomerJwtService>();
builder.Services.AddScoped<PlatformAdminJwtService>();
builder.Services.AddScoped<AvailabilityService>();
builder.Services.AddScoped<RecurringAppointmentService>();
builder.Services.AddScoped<FollowService>();
builder.Services.AddScoped<IWhatsAppSender, TwilioWhatsAppSender>();
builder.Services.AddScoped<WaitlistService>();
builder.Services.AddScoped<AppointmentCancellationService>();
// Real SMS sending via a platform-level Twilio account only kicks in once Twilio:AccountSid is
// configured (via dotnet user-secrets locally, env vars in production) — falls back to the no-op
// dev sender otherwise, so environments without an account (including the test suite) are
// unaffected. This is separate from each barber's own WhatsApp Twilio credentials.
if (!string.IsNullOrEmpty(builder.Configuration["Twilio:AccountSid"]))
    builder.Services.AddScoped<IOtpSender, TwilioOtpSender>();
else
    builder.Services.AddScoped<IOtpSender, DevOtpSender>();
// Real email sending via Resend only kicks in once Resend:ApiKey is configured (via
// dotnet user-secrets locally, env vars in production) — falls back to the no-op dev sender
// otherwise, so environments without an API key (including the test suite) are unaffected.
if (!string.IsNullOrEmpty(builder.Configuration["Resend:ApiKey"]))
    builder.Services.AddHttpClient<IEmailSender, ResendEmailSender>();
else
    builder.Services.AddScoped<IEmailSender, DevEmailSender>();

builder.Services.AddHttpClient<ICardcomService, CardcomService>();

builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p => p
        .WithOrigins(builder.Configuration["AllowedOrigin"] ?? "http://localhost:5173")
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
