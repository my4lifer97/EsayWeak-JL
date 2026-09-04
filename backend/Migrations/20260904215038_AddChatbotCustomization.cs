using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddChatbotCustomization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "WhatsAppConversationStates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "WhatsAppBookingTokens",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ChatbotConfirmationMessage",
                table: "Barbers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ChatbotEnabled",
                table: "Barbers",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "ChatbotWelcomeMessage",
                table: "Barbers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Language",
                table: "WhatsAppConversationStates");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "WhatsAppBookingTokens");

            migrationBuilder.DropColumn(
                name: "ChatbotConfirmationMessage",
                table: "Barbers");

            migrationBuilder.DropColumn(
                name: "ChatbotEnabled",
                table: "Barbers");

            migrationBuilder.DropColumn(
                name: "ChatbotWelcomeMessage",
                table: "Barbers");
        }
    }
}
