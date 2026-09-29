using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <summary>
    /// Test data, not a schema change: fills one whole day of the "jamelma" test business with
    /// fake customers so every slot is booked, for a live test of the waitlist queue (real
    /// customers join the waitlist on one of these appointments, the owner cancels it, and the
    /// waitlist is messaged one person at a time). A no-op on any database without that business.
    /// Fake customers use +999 phone numbers (no such country code) so nothing reaches a real
    /// person, and their reminders are pre-marked as sent. Down() removes everything it added.
    /// </summary>
    public partial class SeedWaitlistTestFullDay : Migration
    {
        public const string BusinessSlugOrName = "jamelma";
        public const string Date = "2026-10-01";
        public const string Marker = "waitlist-test-seed";

        // Kept as single-line statements so the exact same SQL can also be pasted into the
        // Railway Postgres Console (production doesn't auto-migrate, and that console corrupts
        // multi-line pastes).
        public static string SeedCustomersSql(string business, string date) =>
            $"""WITH b AS (SELECT "Id" FROM "Businesses" WHERE lower("Slug") = lower('{business}') OR lower("Name") = lower('{business}') LIMIT 1), it AS (SELECT i."DurationMinutes" AS d FROM "Items" i JOIN b ON i."BusinessId" = b."Id" WHERE i."IsActive" AND i."IsBookable" AND i."DurationMinutes" > 0 ORDER BY i."DurationMinutes" LIMIT 1), wh AS (SELECT w."StartTime"::time AS s, w."EndTime"::time AS e FROM "WorkingHours" w JOIN b ON w."BusinessId" = b."Id" WHERE w."IsActive" AND w."DayOfWeek" = EXTRACT(DOW FROM DATE '{date}')), slots AS (SELECT row_number() OVER (ORDER BY t) AS n FROM wh, it, generate_series(TIMESTAMP '2000-01-01' + wh.s, TIMESTAMP '2000-01-01' + wh.e - make_interval(mins => it.d), make_interval(mins => it.d)) t) INSERT INTO "Customers" ("Id", "Name", "FamilyName", "Phone", "BusinessId") SELECT md5('{Marker}-' || b."Id" || '-' || slots.n), 'Test ' || slots.n, 'Customer', '+999000000' || lpad(slots.n::text, 3, '0'), b."Id" FROM b, slots ON CONFLICT DO NOTHING;""";

        public static string SeedAppointmentsSql(string business, string date) =>
            $"""WITH b AS (SELECT "Id" FROM "Businesses" WHERE lower("Slug") = lower('{business}') OR lower("Name") = lower('{business}') LIMIT 1), it AS (SELECT i."Id" AS item_id, i."DurationMinutes" AS d FROM "Items" i JOIN b ON i."BusinessId" = b."Id" WHERE i."IsActive" AND i."IsBookable" AND i."DurationMinutes" > 0 ORDER BY i."DurationMinutes" LIMIT 1), wh AS (SELECT w."StartTime"::time AS s, w."EndTime"::time AS e FROM "WorkingHours" w JOIN b ON w."BusinessId" = b."Id" WHERE w."IsActive" AND w."DayOfWeek" = EXTRACT(DOW FROM DATE '{date}')), slots AS (SELECT row_number() OVER (ORDER BY t) AS n, to_char(t, 'HH24:MI') AS st, to_char(t + make_interval(mins => it.d), 'HH24:MI') AS et FROM wh, it, generate_series(TIMESTAMP '2000-01-01' + wh.s, TIMESTAMP '2000-01-01' + wh.e - make_interval(mins => it.d), make_interval(mins => it.d)) t) INSERT INTO "Appointments" ("Id", "BusinessId", "CustomerId", "ItemId", "Date", "StartTime", "EndTime", "Notes", "Status", "ReminderSent", "ReminderSentSoon", "CancelToken", "CreatedAt", "PendingCancellationApproval") SELECT md5('{Marker}-appt-' || b."Id" || '-' || slots.n), b."Id", md5('{Marker}-' || b."Id" || '-' || slots.n), it.item_id, DATE '{date}', slots.st, slots.et, '{Marker}', 'CONFIRMED', true, true, md5(random()::text || slots.n), now(), false FROM b, it, slots WHERE NOT EXISTS (SELECT 1 FROM "Appointments" a WHERE a."BusinessId" = b."Id" AND a."Date" = DATE '{date}' AND a."Status" = 'CONFIRMED' AND a."StartTime" < slots.et AND a."EndTime" > slots.st) AND NOT EXISTS (SELECT 1 FROM "Breaks" br WHERE br."BusinessId" = b."Id" AND br."DayOfWeek" = EXTRACT(DOW FROM DATE '{date}') AND br."StartTime" < slots.et AND br."EndTime" > slots.st);""";

        public const string CleanupSql =
            $"""DELETE FROM "Appointments" WHERE "Notes" = '{Marker}'; DELETE FROM "Customers" c WHERE c."Phone" LIKE '+999000000%' AND c."Id" = md5('{Marker}-' || c."BusinessId" || '-' || ltrim(right(c."Phone", 3), '0')) AND NOT EXISTS (SELECT 1 FROM "Appointments" a WHERE a."CustomerId" = c."Id");""";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SeedCustomersSql(BusinessSlugOrName, Date));
            migrationBuilder.Sql(SeedAppointmentsSql(BusinessSlugOrName, Date));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(CleanupSql);
        }
    }
}
