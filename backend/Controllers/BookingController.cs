using System.Security.Claims;
using BarberSaas.Api.Data;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Controllers;

[ApiController]
[Route("api/{slug}")]
public class BookingController(AppDbContext db, AvailabilityService availability) : ControllerBase
{
    [HttpGet("info")]
    public async Task<IActionResult> GetBarberInfo(string slug)
    {
        var barber = await db.Barbers
            .Include(b => b.Services.Where(s => s.IsActive))
            .Include(b => b.WorkingHours.Where(w => w.IsActive))
            .FirstOrDefaultAsync(b => b.Slug == slug);

        if (barber is null) return NotFound(new { error = "Not found" });

        var isRTL = barber.Language is Language.AR or Language.HE;
        var activeDays = barber.WorkingHours.Select(w => w.DayOfWeek).ToArray();

        var services = barber.Services.Select(s => new ServiceDto(
            s.Id, s.BarberId, s.NameEn, s.NameAr, s.NameHe, s.DurationMinutes, s.Price, s.IsActive)).ToList();

        var isFollowed = false;
        if (User.FindFirst("type")?.Value == "customer")
        {
            var accountId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            isFollowed = await db.Follows.AnyAsync(f => f.CustomerAccountId == accountId && f.BarberId == barber.Id);
        }

        return Ok(new PublicBarberDto(
            barber.Slug, barber.Name, barber.Description, barber.Logo,
            barber.Language.ToString(), isRTL, activeDays, services, isFollowed));
    }

    [HttpGet("availability")]
    public async Task<IActionResult> GetAvailability(string slug, [FromQuery] string date, [FromQuery] string serviceId)
    {
        var barber = await db.Barbers.FirstOrDefaultAsync(b => b.Slug == slug);
        if (barber is null) return NotFound(new { error = "Not found" });

        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == serviceId && s.BarberId == barber.Id && s.IsActive);
        if (service is null) return NotFound(new { error = "Service not found" });

        var slots = await availability.GetAvailableSlots(barber.Id, date, service.DurationMinutes);
        return Ok(new { slots });
    }

    [HttpPost("appointments")]
    public async Task<IActionResult> BookAppointment(string slug, [FromBody] BookAppointmentRequest req)
    {
        var barber = await db.Barbers.FirstOrDefaultAsync(b => b.Slug == slug);
        if (barber is null) return NotFound(new { error = "Not found" });

        if (barber.SubscriptionStatus == SubStatus.EXPIRED ||
            (barber.SubscriptionStatus == SubStatus.TRIAL && barber.TrialEndsAt < DateTime.UtcNow))
            return StatusCode(403, new { error = "Booking unavailable" });

        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == req.ServiceId && s.BarberId == barber.Id && s.IsActive);
        if (service is null) return NotFound(new { error = "Service not found" });

        var slots = await availability.GetAvailableSlots(barber.Id, req.Date, service.DurationMinutes);
        if (!slots.Any(s => s.Start == req.StartTime))
            return Conflict(new { error = "Slot no longer available" });

        var endTime = AvailabilityService.AddMinutes(req.StartTime, service.DurationMinutes);

        // If a logged-in customer token is attached, link the booking to their account and trust
        // their verified phone over whatever was typed in the form (never let a client override
        // another account's Customer row via a spoofed phone number).
        string? customerAccountId = null;
        var phone = req.CustomerPhone;
        if (User.FindFirst("type")?.Value == "customer")
        {
            customerAccountId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            phone = User.FindFirst("phone")?.Value ?? phone;
        }

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.BarberId == barber.Id && c.Phone == phone);
        if (customer is null)
        {
            customer = new Customer { Name = req.CustomerName, Phone = phone, BarberId = barber.Id, CustomerAccountId = customerAccountId };
            db.Customers.Add(customer);
        }
        else
        {
            customer.Name = req.CustomerName;
            if (customerAccountId is not null) customer.CustomerAccountId = customerAccountId;
        }

        var appointment = new Appointment
        {
            BarberId = barber.Id,
            CustomerId = customer.Id,
            ServiceId = service.Id,
            Date = DateTime.Parse(req.Date + "T00:00:00Z").ToUniversalTime(),
            StartTime = req.StartTime,
            EndTime = endTime,
            Notes = req.Notes,
            Status = AppointmentStatus.CONFIRMED,
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        return StatusCode(201, new BookAppointmentResponse(appointment.Id, appointment.CancelToken));
    }

    [HttpGet("appointments/{id}")]
    public async Task<IActionResult> GetAppointment(string slug, string id)
    {
        var appointment = await db.Appointments
            .Include(a => a.Customer)
            .Include(a => a.Service)
            .Include(a => a.Barber)
            .FirstOrDefaultAsync(a => a.Id == id && a.Barber.Slug == slug);

        if (appointment is null) return NotFound(new { error = "Not found" });

        return Ok(new AppointmentDetailDto(
            appointment.Id, appointment.BarberId, appointment.CustomerId, appointment.ServiceId,
            appointment.Date.ToString("yyyy-MM-dd"), appointment.StartTime, appointment.EndTime,
            appointment.Notes, appointment.Status.ToString(), appointment.ReminderSent, appointment.CancelToken,
            appointment.CreatedAt,
            new CustomerSummary(appointment.Customer.Id, appointment.Customer.Name, appointment.Customer.FamilyName, appointment.Customer.Phone),
            new ServiceSummary(appointment.Service.Id, appointment.Service.NameEn, appointment.Service.NameAr, appointment.Service.NameHe, appointment.Service.DurationMinutes, appointment.Service.Price),
            new BarberSummary(appointment.Barber.Name, appointment.Barber.Slug, appointment.Barber.Language.ToString())));
    }

    [HttpDelete("appointments/{id}")]
    public async Task<IActionResult> CancelAppointment(string slug, string id, [FromQuery] string token)
    {
        var appointment = await db.Appointments
            .Include(a => a.Barber)
            .FirstOrDefaultAsync(a => a.Id == id && a.Barber.Slug == slug);

        if (appointment is null) return NotFound(new { error = "Not found" });
        if (appointment.CancelToken != token) return StatusCode(403, new { error = "Invalid token" });

        appointment.Status = AppointmentStatus.CANCELLED;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPatch("appointments/{id}")]
    public async Task<IActionResult> RescheduleAppointment(string slug, string id, [FromQuery] string token, [FromBody] RescheduleRequest req)
    {
        var appointment = await db.Appointments
            .Include(a => a.Service)
            .Include(a => a.Barber)
            .FirstOrDefaultAsync(a => a.Id == id && a.Barber.Slug == slug);

        if (appointment is null) return NotFound(new { error = "Not found" });
        if (appointment.CancelToken != token) return StatusCode(403, new { error = "Invalid token" });

        var slots = await availability.GetAvailableSlots(appointment.BarberId, req.Date, appointment.Service.DurationMinutes);
        if (!slots.Any(s => s.Start == req.StartTime))
            return Conflict(new { error = "Slot not available" });

        appointment.Date = DateTime.Parse(req.Date + "T00:00:00Z").ToUniversalTime();
        appointment.StartTime = req.StartTime;
        appointment.EndTime = AvailabilityService.AddMinutes(req.StartTime, appointment.Service.DurationMinutes);
        appointment.ReminderSent = false;
        await db.SaveChangesAsync();

        return Ok(new { appointment.Id, Status = appointment.Status.ToString() });
    }
}

public record RescheduleRequest(string Date, string StartTime);
