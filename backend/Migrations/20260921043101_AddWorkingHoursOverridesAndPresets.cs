using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkingHoursOverridesAndPresets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SchedulePresets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BusinessId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulePresets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchedulePresets_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkingHoursOverrides",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BusinessId = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateTime>(type: "date", nullable: false),
                    StartTime = table.Column<string>(type: "text", nullable: false),
                    EndTime = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkingHoursOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkingHoursOverrides_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SchedulePresetDays",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    SchedulePresetId = table.Column<string>(type: "text", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<string>(type: "text", nullable: false),
                    EndTime = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulePresetDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchedulePresetDays_SchedulePresets_SchedulePresetId",
                        column: x => x.SchedulePresetId,
                        principalTable: "SchedulePresets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SchedulePresetDays_SchedulePresetId",
                table: "SchedulePresetDays",
                column: "SchedulePresetId");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulePresets_BusinessId",
                table: "SchedulePresets",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkingHoursOverrides_BusinessId_Date",
                table: "WorkingHoursOverrides",
                columns: new[] { "BusinessId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchedulePresetDays");

            migrationBuilder.DropTable(
                name: "WorkingHoursOverrides");

            migrationBuilder.DropTable(
                name: "SchedulePresets");
        }
    }
}
