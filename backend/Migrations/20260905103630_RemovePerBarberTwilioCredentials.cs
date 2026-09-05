using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemovePerBarberTwilioCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TwilioSid",
                table: "Barbers");

            migrationBuilder.DropColumn(
                name: "TwilioToken",
                table: "Barbers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TwilioSid",
                table: "Barbers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwilioToken",
                table: "Barbers",
                type: "text",
                nullable: true);
        }
    }
}
