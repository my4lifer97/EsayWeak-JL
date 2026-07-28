using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringAppointments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecurringSeriesId",
                table: "Appointments",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecurringSeries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BarberId = table.Column<string>(type: "text", nullable: false),
                    CustomerId = table.Column<string>(type: "text", nullable: false),
                    ServiceId = table.Column<string>(type: "text", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    StartDate = table.Column<DateTime>(type: "date", nullable: false),
                    EndDate = table.Column<DateTime>(type: "date", nullable: true),
                    LastGeneratedThrough = table.Column<DateTime>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringSeries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringSeries_Barbers_BarberId",
                        column: x => x.BarberId,
                        principalTable: "Barbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringSeries_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringSeries_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecurringSkips",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RecurringSeriesId = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateTime>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringSkips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringSkips_RecurringSeries_RecurringSeriesId",
                        column: x => x.RecurringSeriesId,
                        principalTable: "RecurringSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_RecurringSeriesId_Date",
                table: "Appointments",
                columns: new[] { "RecurringSeriesId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSeries_BarberId_IsActive",
                table: "RecurringSeries",
                columns: new[] { "BarberId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSeries_CustomerId",
                table: "RecurringSeries",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSeries_ServiceId",
                table: "RecurringSeries",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSkips_RecurringSeriesId",
                table: "RecurringSkips",
                column: "RecurringSeriesId");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_RecurringSeries_RecurringSeriesId",
                table: "Appointments",
                column: "RecurringSeriesId",
                principalTable: "RecurringSeries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_RecurringSeries_RecurringSeriesId",
                table: "Appointments");

            migrationBuilder.DropTable(
                name: "RecurringSkips");

            migrationBuilder.DropTable(
                name: "RecurringSeries");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_RecurringSeriesId_Date",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "RecurringSeriesId",
                table: "Appointments");
        }
    }
}
