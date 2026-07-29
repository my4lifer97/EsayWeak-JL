using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceStripeWithCardcomBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "StripeSubscriptionId",
                table: "Barbers",
                newName: "CardcomToken");

            migrationBuilder.RenameColumn(
                name: "StripeCustomerId",
                table: "Barbers",
                newName: "CardcomLastLowProfileId");

            migrationBuilder.AddColumn<DateTime>(
                name: "CardcomNextChargeAt",
                table: "Barbers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CardcomNextChargeAt",
                table: "Barbers");

            migrationBuilder.RenameColumn(
                name: "CardcomToken",
                table: "Barbers",
                newName: "StripeSubscriptionId");

            migrationBuilder.RenameColumn(
                name: "CardcomLastLowProfileId",
                table: "Barbers",
                newName: "StripeCustomerId");
        }
    }
}
