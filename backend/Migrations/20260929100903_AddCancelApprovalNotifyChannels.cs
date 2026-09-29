using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCancelApprovalNotifyChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelApprovalEmail",
                table: "Businesses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CancelApprovalNotifyViaEmail",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CancelApprovalNotifyViaWhatsApp",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelApprovalWhatsAppNumber",
                table: "Businesses",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancelApprovalEmail",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "CancelApprovalNotifyViaEmail",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "CancelApprovalNotifyViaWhatsApp",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "CancelApprovalWhatsAppNumber",
                table: "Businesses");
        }
    }
}
