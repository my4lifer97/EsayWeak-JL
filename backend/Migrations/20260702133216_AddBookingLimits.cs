using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxBookingsPerDay",
                table: "Barbers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxBookingsPerWeek",
                table: "Barbers",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxBookingsPerDay",
                table: "Barbers");

            migrationBuilder.DropColumn(
                name: "MaxBookingsPerWeek",
                table: "Barbers");
        }
    }
}
