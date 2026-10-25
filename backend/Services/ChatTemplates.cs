using System.Globalization;
using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

// The "From the system" ready messages on the owner's Chats page: text built from the business's
// live data (services, schedule, free times, address...) in the customer's language, which the
// owner can edit before sending. Hours follow AvailabilityService (scheduled presets, full-day
// blocks), so they match what the booking page offers.
public class ChatTemplates(AppDbContext db, AvailabilityService availability, IConfiguration config)
{
    public static readonly string[] Kinds = ["services", "service", "week", "day", "freeTimes", "address", "page", "appointments"];

    // How far ahead "day" and "freeTimes" can look (the booking page offers 60 days too).
    private const int MaxDaysAhead = 60;

    private static readonly Dictionary<string, string[]> DayNames = new()
    {
        ["EN"] = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"],
        ["AR"] = ["الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت"],
        ["HE"] = ["ראשון", "שני", "שלישי", "רביעי", "חמישי", "שישי", "שבת"],
    };

    public sealed record Result(string? Text, string? Error);

    public async Task<Result> BuildAsync(Business business, string customerPhone, string lang, string kind, string? date, string? itemId)
    {
        lang = DayNames.ContainsKey(lang) ? lang : "EN";
        return kind switch
        {
            "services" => await Services(business, lang),
            "service" => await OneService(business, lang, itemId),
            "week" => await Week(business, lang),
            "day" => await Day(business, lang, date),
            "freeTimes" => await FreeTimes(business, lang, date, itemId),
            "address" => Address(business, lang),
            "page" => new Result(I18nService.T(lang, "template.page", new() { ["url"] = $"{config["AppUrl"]}/{business.Slug}" }), null),
            "appointments" => await Appointments(business, customerPhone, lang),
            _ => new Result(null, "Unknown message"),
        };
    }

    private async Task<List<Item>> ActiveItems(string businessId) =>
        (await db.Items.Where(i => i.BusinessId == businessId && i.IsActive).ToListAsync())
            .OrderBy(i => i.NameEn, StringComparer.CurrentCultureIgnoreCase).ToList();

    private static string Name(Item item, string lang) => lang switch { "AR" => item.NameAr, "HE" => item.NameHe, _ => item.NameEn };

    // "Haircut — 30 min — ₪50"; parts the item doesn't have are left out.
    private static string ServiceLine(Item item, string lang)
    {
        var parts = new List<string> { Name(item, lang) };
        if (item.DurationMinutes is { } minutes) parts.Add(I18nService.T(lang, "template.minutes", new() { ["minutes"] = minutes.ToString() }));
        parts.Add(item.Price is { } price ? $"₪{price.ToString("0.##", CultureInfo.InvariantCulture)}" : I18nService.T(lang, "template.priceOnRequest"));
        return string.Join(" — ", parts);
    }

    private async Task<Result> Services(Business business, string lang)
    {
        var items = await ActiveItems(business.Id);
        if (items.Count == 0) return new Result(null, "Add your services first");
        var lines = items.Select(i => $"• {ServiceLine(i, lang)}");
        return new Result($"{I18nService.T(lang, "template.servicesHeader", new() { ["businessName"] = business.Name })}\n{string.Join("\n", lines)}", null);
    }

    private async Task<Result> OneService(Business business, string lang, string? itemId)
    {
        var item = (await ActiveItems(business.Id)).FirstOrDefault(i => i.Id == itemId);
        return item is null ? new Result(null, "Choose a service") : new Result(ServiceLine(item, lang), null);
    }

    private async Task<Result> Week(Business business, string lang)
    {
        var days = await availability.GetUpcomingHours(business.Id, 7);
        var lines = days.Select(d => $"• {DayNames[lang][d.DayOfWeek]} {ShortDate(d.Date)}: " +
            (d.StartTime is null ? I18nService.T(lang, "template.closed") : $"‎{d.StartTime}–{d.EndTime}‎"));
        return new Result($"{I18nService.T(lang, "template.weekHeader")}\n{string.Join("\n", lines)}", null);
    }

    private async Task<Result> Day(Business business, string lang, string? date)
    {
        var (day, error) = await OpeningDay(business.Id, date);
        if (error is not null) return new Result(null, error);
        var args = new Dictionary<string, string> { ["day"] = DayNames[lang][day!.DayOfWeek], ["date"] = ShortDate(day.Date) };
        if (day.StartTime is null) return new Result(I18nService.T(lang, "template.dayClosed", args), null);
        args["start"] = day.StartTime;
        args["end"] = day.EndTime!;
        return new Result(I18nService.T(lang, "template.dayHours", args), null);
    }

    // The free start times on a date for one service -- the same slots the booking page offers.
    private async Task<Result> FreeTimes(Business business, string lang, string? date, string? itemId)
    {
        var item = (await ActiveItems(business.Id)).FirstOrDefault(i => i.Id == itemId && i.IsBookable && i.DurationMinutes is not null);
        if (item is null) return new Result(null, "Choose a service");
        var (day, error) = await OpeningDay(business.Id, date);
        if (error is not null) return new Result(null, error);

        var args = new Dictionary<string, string>
        {
            ["day"] = DayNames[lang][day!.DayOfWeek], ["date"] = ShortDate(day.Date), ["service"] = Name(item, lang),
        };
        var slots = day.StartTime is null ? [] : await availability.GetAvailableSlots(business.Id, day.Date, item.DurationMinutes!.Value);
        if (slots.Count == 0) return new Result(I18nService.T(lang, "template.noFreeTimes", args), null);
        args["times"] = string.Join(", ", slots.Select(s => s.Start));
        return new Result(I18nService.T(lang, "template.freeTimes", args), null);
    }

    private async Task<(DTOs.OpeningDayDto? Day, string? Error)> OpeningDay(string businessId, string? date)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return (null, "Choose a date");
        var offset = (parsed.Date - AppClock.Now.Date).Days;
        if (offset < 0 || offset >= MaxDaysAhead) return (null, "Choose a date in the next 60 days");
        var days = await availability.GetUpcomingHours(businessId, offset + 1);
        return (days[offset], null);
    }

    private static Result Address(Business business, string lang)
    {
        var address = string.Join(", ", new[] { business.AddressLine, business.City }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (address.Length == 0 && string.IsNullOrWhiteSpace(business.MapUrl))
            return new Result(null, "Add your address in Settings first");
        var lines = new List<string>();
        if (address.Length > 0) lines.Add(I18nService.T(lang, "template.address", new() { ["address"] = address }));
        if (!string.IsNullOrWhiteSpace(business.MapUrl)) lines.Add(I18nService.T(lang, "template.map", new() { ["url"] = business.MapUrl }));
        return new Result(string.Join("\n", lines), null);
    }

    private async Task<Result> Appointments(Business business, string phone, string lang)
    {
        var appointments = (await db.Appointments.Include(a => a.Item)
                .Where(a => a.BusinessId == business.Id && a.Customer.Phone == phone
                    && a.Status == AppointmentStatus.CONFIRMED && a.Date >= AppClock.Now.Date)
                .ToListAsync())
            .Where(a => AppointmentStatusHelper.EffectiveStatus(a.Status, a.Date, a.EndTime) == "CONFIRMED")
            .OrderBy(a => a.Date).ThenBy(a => a.StartTime)
            .ToList();
        if (appointments.Count == 0) return new Result(I18nService.T(lang, "template.noUpcoming"), null);
        var lines = appointments.Select(a =>
            $"• {DayNames[lang][(int)a.Date.DayOfWeek]} {a.Date:dd/MM} ‎{a.StartTime}‎ — {Name(a.Item, lang)}");
        return new Result(I18nService.T(lang, "template.upcoming", new() { ["list"] = string.Join("\n", lines) }), null);
    }

    private static string ShortDate(string isoDate) =>
        DateTime.ParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("dd/MM", CultureInfo.InvariantCulture);
}
