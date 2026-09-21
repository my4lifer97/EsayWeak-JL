using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulePresetBreaks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SchedulePresetBreaks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    SchedulePresetId = table.Column<string>(type: "text", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<string>(type: "text", nullable: false),
                    EndTime = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulePresetBreaks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchedulePresetBreaks_SchedulePresets_SchedulePresetId",
                        column: x => x.SchedulePresetId,
                        principalTable: "SchedulePresets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SchedulePresetBreaks_SchedulePresetId",
                table: "SchedulePresetBreaks",
                column: "SchedulePresetId");

            // Backfill: existing Default presets (created by AddDefaultSchedulePreset) predate this
            // table, so they'd otherwise have zero breaks even if the business has real ones live --
            // copy the business's current Breaks into its Default preset. Guarded per-preset so this
            // is safe to re-run. Non-default presets are untouched (they never had breaks before).
            migrationBuilder.Sql(@"
                INSERT INTO ""SchedulePresetBreaks"" (""Id"", ""SchedulePresetId"", ""DayOfWeek"", ""StartTime"", ""EndTime"")
                SELECT md5(random()::text || clock_timestamp()::text || sp.""Id"" || br.""Id""), sp.""Id"", br.""DayOfWeek"", br.""StartTime"", br.""EndTime""
                FROM ""SchedulePresets"" sp
                JOIN ""Breaks"" br ON br.""BusinessId"" = sp.""BusinessId""
                WHERE sp.""IsDefault"" = true
                AND NOT EXISTS (SELECT 1 FROM ""SchedulePresetBreaks"" spb WHERE spb.""SchedulePresetId"" = sp.""Id"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchedulePresetBreaks");
        }
    }
}
