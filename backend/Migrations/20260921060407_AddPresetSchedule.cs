using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPresetSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PresetSchedules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BusinessId = table.Column<string>(type: "text", nullable: false),
                    PresetId = table.Column<string>(type: "text", nullable: false),
                    RevertToPresetId = table.Column<string>(type: "text", nullable: false),
                    StartDate = table.Column<DateTime>(type: "date", nullable: false),
                    EndDate = table.Column<DateTime>(type: "date", nullable: false),
                    Applied = table.Column<bool>(type: "boolean", nullable: false),
                    Reverted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PresetSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PresetSchedules_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PresetSchedules_SchedulePresets_PresetId",
                        column: x => x.PresetId,
                        principalTable: "SchedulePresets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PresetSchedules_SchedulePresets_RevertToPresetId",
                        column: x => x.RevertToPresetId,
                        principalTable: "SchedulePresets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PresetSchedules_BusinessId",
                table: "PresetSchedules",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_PresetSchedules_PresetId",
                table: "PresetSchedules",
                column: "PresetId");

            migrationBuilder.CreateIndex(
                name: "IX_PresetSchedules_RevertToPresetId",
                table: "PresetSchedules",
                column: "RevertToPresetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PresetSchedules");
        }
    }
}
