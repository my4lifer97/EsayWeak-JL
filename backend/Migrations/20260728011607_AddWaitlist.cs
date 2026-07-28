using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWaitlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_BarberId",
                table: "Appointments");

            migrationBuilder.AddColumn<bool>(
                name: "WaitlistEnabled",
                table: "Barbers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "WaitlistEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    AppointmentId = table.Column<string>(type: "text", nullable: false),
                    BarberId = table.Column<string>(type: "text", nullable: false),
                    CustomerAccountId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NotifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaitlistEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WaitlistEntries_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WaitlistEntries_Barbers_BarberId",
                        column: x => x.BarberId,
                        principalTable: "Barbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WaitlistEntries_CustomerAccounts_CustomerAccountId",
                        column: x => x.CustomerAccountId,
                        principalTable: "CustomerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BarberId_Date_StartTime_Confirmed",
                table: "Appointments",
                columns: new[] { "BarberId", "Date", "StartTime" },
                unique: true,
                filter: "\"Status\" = 'CONFIRMED'");

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_AppointmentId_CustomerAccountId",
                table: "WaitlistEntries",
                columns: new[] { "AppointmentId", "CustomerAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_BarberId",
                table: "WaitlistEntries",
                column: "BarberId");

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_CustomerAccountId",
                table: "WaitlistEntries",
                column: "CustomerAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WaitlistEntries");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_BarberId_Date_StartTime_Confirmed",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "WaitlistEnabled",
                table: "Barbers");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BarberId",
                table: "Appointments",
                column: "BarberId");
        }
    }
}
