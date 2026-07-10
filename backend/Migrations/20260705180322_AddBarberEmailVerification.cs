using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBarberEmailVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailVerified",
                table: "Barbers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Grandfather in barbers who registered before email verification existed —
            // only newly-registered accounts (via AuthController.Register, which explicitly
            // sets EmailVerified = false) should be required to verify.
            migrationBuilder.Sql("UPDATE \"Barbers\" SET \"EmailVerified\" = true;");

            migrationBuilder.CreateTable(
                name: "BarberEmailOtps",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    CodeHash = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Consumed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BarberEmailOtps", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BarberEmailOtps_Email_CreatedAt",
                table: "BarberEmailOtps",
                columns: new[] { "Email", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BarberEmailOtps");

            migrationBuilder.DropColumn(
                name: "EmailVerified",
                table: "Barbers");
        }
    }
}
