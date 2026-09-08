using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessDiscoveryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddressLine",
                table: "Businesses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Businesses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsListed",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "MapUrl",
                table: "Businesses",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Businesses_City",
                table: "Businesses",
                column: "City");

            migrationBuilder.CreateIndex(
                name: "IX_Businesses_IsListed",
                table: "Businesses",
                column: "IsListed");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Businesses_City",
                table: "Businesses");

            migrationBuilder.DropIndex(
                name: "IX_Businesses_IsListed",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "AddressLine",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "IsListed",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "MapUrl",
                table: "Businesses");
        }
    }
}
