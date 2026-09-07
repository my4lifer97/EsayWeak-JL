using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessOwnerRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "BusinessOwnerRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BusinessName = table.Column<string>(type: "text", nullable: false),
                    OwnerName = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: false),
                    BusinessTypeId = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RejectionNote = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedByPlatformAdminId = table.Column<string>(type: "text", nullable: true),
                    CreatedBusinessId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessOwnerRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessOwnerRequests_BusinessTypeDefinitions_BusinessTypeId",
                        column: x => x.BusinessTypeId,
                        principalTable: "BusinessTypeDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BusinessOwnerRequests_Businesses_CreatedBusinessId",
                        column: x => x.CreatedBusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BusinessOwnerRequests_PlatformAdmins_ReviewedByPlatformAdmi~",
                        column: x => x.ReviewedByPlatformAdminId,
                        principalTable: "PlatformAdmins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessOwnerRequests_BusinessTypeId",
                table: "BusinessOwnerRequests",
                column: "BusinessTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessOwnerRequests_CreatedBusinessId",
                table: "BusinessOwnerRequests",
                column: "CreatedBusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessOwnerRequests_Email",
                table: "BusinessOwnerRequests",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessOwnerRequests_ReviewedByPlatformAdminId",
                table: "BusinessOwnerRequests",
                column: "ReviewedByPlatformAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessOwnerRequests_Status",
                table: "BusinessOwnerRequests",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessOwnerRequests");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "Businesses");
        }
    }
}
