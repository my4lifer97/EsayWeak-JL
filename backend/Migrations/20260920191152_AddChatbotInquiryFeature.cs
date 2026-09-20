using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddChatbotInquiryFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChatbotMode",
                table: "WhatsAppConversationStates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "InquiryNotified",
                table: "WhatsAppConversationStates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ChatbotFinalMessage",
                table: "Businesses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ChatbotInquiryEnabled",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "InquiryEmail",
                table: "Businesses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InquiryNotifyViaEmail",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "InquiryNotifyViaWhatsApp",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "InquiryWhatsAppNumber",
                table: "Businesses",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChatbotInquiries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BusinessId = table.Column<string>(type: "text", nullable: false),
                    CustomerPhone = table.Column<string>(type: "text", nullable: false),
                    CustomerName = table.Column<string>(type: "text", nullable: true),
                    Message = table.Column<string>(type: "text", nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatbotInquiries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatbotInquiries_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatbotInquiries_BusinessId_CreatedAt",
                table: "ChatbotInquiries",
                columns: new[] { "BusinessId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatbotInquiries");

            migrationBuilder.DropColumn(
                name: "ChatbotMode",
                table: "WhatsAppConversationStates");

            migrationBuilder.DropColumn(
                name: "InquiryNotified",
                table: "WhatsAppConversationStates");

            migrationBuilder.DropColumn(
                name: "ChatbotFinalMessage",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "ChatbotInquiryEnabled",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "InquiryEmail",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "InquiryNotifyViaEmail",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "InquiryNotifyViaWhatsApp",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "InquiryWhatsAppNumber",
                table: "Businesses");
        }
    }
}
