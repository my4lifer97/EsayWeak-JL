using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultSchedulePreset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "SchedulePresets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill: every existing business gets one Default preset, seeded from whatever its
            // WorkingHours currently holds (defaulting missing days to 09:00-18:00/inactive, same
            // fallback the app code already uses). Ids are md5 hex (32 chars, no dashes) to match
            // the app's Guid.NewGuid().ToString("N") format. New businesses registered after this
            // migration get their Default preset lazily via SchedulePresetService.GetOrCreateDefaultPreset.
            migrationBuilder.Sql(@"
                WITH new_presets AS (
                    INSERT INTO ""SchedulePresets"" (""Id"", ""BusinessId"", ""Name"", ""CreatedAt"", ""IsDefault"")
                    SELECT md5(random()::text || clock_timestamp()::text || b.""Id""), b.""Id"", 'Default', now(), true
                    FROM ""Businesses"" b
                    RETURNING ""Id"" AS ""PresetId"", ""BusinessId""
                ),
                days AS (
                    SELECT generate_series(0, 6) AS ""DayOfWeek""
                )
                INSERT INTO ""SchedulePresetDays"" (""Id"", ""SchedulePresetId"", ""DayOfWeek"", ""StartTime"", ""EndTime"", ""IsActive"")
                SELECT md5(random()::text || clock_timestamp()::text || np.""PresetId"" || d.""DayOfWeek""),
                       np.""PresetId"", d.""DayOfWeek"",
                       COALESCE(wh.""StartTime"", '09:00'),
                       COALESCE(wh.""EndTime"", '18:00'),
                       COALESCE(wh.""IsActive"", false)
                FROM new_presets np
                CROSS JOIN days d
                LEFT JOIN ""WorkingHours"" wh ON wh.""BusinessId"" = np.""BusinessId"" AND wh.""DayOfWeek"" = d.""DayOfWeek"";
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "SchedulePresets");
        }
    }
}
