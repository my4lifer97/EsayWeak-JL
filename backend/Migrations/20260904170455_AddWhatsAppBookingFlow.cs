using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppBookingFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerOtps");

            migrationBuilder.CreateTable(
                name: "WhatsAppBookingTokens",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BarberId = table.Column<string>(type: "text", nullable: false),
                    ServiceId = table.Column<string>(type: "text", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: false),
                    ProfileName = table.Column<string>(type: "text", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppBookingTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppBookingTokens_Barbers_BarberId",
                        column: x => x.BarberId,
                        principalTable: "Barbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WhatsAppBookingTokens_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppConversationStates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BarberId = table.Column<string>(type: "text", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppConversationStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppConversationStates_Barbers_BarberId",
                        column: x => x.BarberId,
                        principalTable: "Barbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppBookingTokens_BarberId",
                table: "WhatsAppBookingTokens",
                column: "BarberId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppBookingTokens_ExpiresAt",
                table: "WhatsAppBookingTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppBookingTokens_ServiceId",
                table: "WhatsAppBookingTokens",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversationStates_BarberId_Phone",
                table: "WhatsAppConversationStates",
                columns: new[] { "BarberId", "Phone" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppBookingTokens");

            migrationBuilder.DropTable(
                name: "WhatsAppConversationStates");

            migrationBuilder.CreateTable(
                name: "CustomerOtps",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CodeHash = table.Column<string>(type: "text", nullable: false),
                    Consumed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerOtps", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerOtps_Phone_CreatedAt",
                table: "CustomerOtps",
                columns: new[] { "Phone", "CreatedAt" });
        }
    }
}
