using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class RenameBarberToBusiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop FK constraints before renaming their columns/tables -- EF's scaffolder always
            // drops+recreates a FK whose name changes (the name embeds the old table/column names),
            // but this is metadata-only in Postgres (no data rewrite), never a real drop of data.
            migrationBuilder.DropForeignKey(
                name: "FK_ActivityLogs_Barbers_BarberId",
                table: "ActivityLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Barbers_BarberId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_BlockedSlots_Barbers_BarberId",
                table: "BlockedSlots");

            migrationBuilder.DropForeignKey(
                name: "FK_Breaks_Barbers_BarberId",
                table: "Breaks");

            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Barbers_BarberId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_Follows_Barbers_BarberId",
                table: "Follows");

            migrationBuilder.DropForeignKey(
                name: "FK_RecurringSeries_Barbers_BarberId",
                table: "RecurringSeries");

            migrationBuilder.DropForeignKey(
                name: "FK_Services_Barbers_BarberId",
                table: "Services");

            migrationBuilder.DropForeignKey(
                name: "FK_WaitlistEntries_Barbers_BarberId",
                table: "WaitlistEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_WhatsAppBookingTokens_Barbers_BarberId",
                table: "WhatsAppBookingTokens");

            migrationBuilder.DropForeignKey(
                name: "FK_WhatsAppConversationStates_Barbers_BarberId",
                table: "WhatsAppConversationStates");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkingHours_Barbers_BarberId",
                table: "WorkingHours");

            migrationBuilder.DropForeignKey(
                name: "FK_Barbers_BusinessTypeDefinitions_BusinessTypeId",
                table: "Barbers");

            // Renames only -- Postgres RENAME TABLE/COLUMN are metadata-only operations, never a
            // drop+recreate, so no tenant data is lost.
            migrationBuilder.RenameTable(
                name: "Barbers",
                newName: "Businesses");

            migrationBuilder.RenameTable(
                name: "BarberEmailOtps",
                newName: "BusinessEmailOtps");

            migrationBuilder.RenameTable(
                name: "BarberPasswordResetOtps",
                newName: "BusinessPasswordResetOtps");

            migrationBuilder.RenameColumn(
                name: "BarberId",
                table: "WorkingHours",
                newName: "BusinessId");

            migrationBuilder.RenameIndex(
                name: "IX_WorkingHours_BarberId_DayOfWeek",
                table: "WorkingHours",
                newName: "IX_WorkingHours_BusinessId_DayOfWeek");

            migrationBuilder.RenameColumn(
                name: "BarberId",
                table: "WhatsAppConversationStates",
                newName: "BusinessId");

            migrationBuilder.RenameIndex(
                name: "IX_WhatsAppConversationStates_BarberId_Phone",
                table: "WhatsAppConversationStates",
                newName: "IX_WhatsAppConversationStates_BusinessId_Phone");

            migrationBuilder.RenameColumn(
                name: "BarberId",
                table: "WhatsAppBookingTokens",
                newName: "BusinessId");

            migrationBuilder.RenameIndex(
                name: "IX_WhatsAppBookingTokens_BarberId",
                table: "WhatsAppBookingTokens",
                newName: "IX_WhatsAppBookingTokens_BusinessId");

            migrationBuilder.RenameColumn(
                name: "BarberId",
                table: "WaitlistEntries",
                newName: "BusinessId");

            migrationBuilder.RenameIndex(
                name: "IX_WaitlistEntries_BarberId",
                table: "WaitlistEntries",
                newName: "IX_WaitlistEntries_BusinessId");

            migrationBuilder.RenameColumn(
                name: "BarberId",
                table: "Services",
                newName: "BusinessId");

            migrationBuilder.RenameIndex(
                name: "IX_Services_BarberId",
                table: "Services",
                newName: "IX_Services_BusinessId");

            migrationBuilder.RenameColumn(
                name: "BarberId",
                table: "RecurringSeries",
                newName: "BusinessId");

            migrationBuilder.RenameIndex(
                name: "IX_RecurringSeries_BarberId_IsActive",
                table: "RecurringSeries",
                newName: "IX_RecurringSeries_BusinessId_IsActive");

            migrationBuilder.RenameColumn(
                name: "BarberId",
                table: "Follows",
                newName: "BusinessId");

            migrationBuilder.RenameIndex(
                name: "IX_Follows_CustomerAccountId_BarberId",
                table: "Follows",
                newName: "IX_Follows_CustomerAccountId_BusinessId");

            migrationBuilder.RenameIndex(
                name: "IX_Follows_BarberId",
                table: "Follows",
                newName: "IX_Follows_BusinessId");

            migrationBuilder.RenameColumn(
                name: "BarberId",
                table: "Customers",
                newName: "BusinessId");

            migrationBuilder.RenameIndex(
                name: "IX_Customers_BarberId_Phone",
                table: "Customers",
                newName: "IX_Customers_BusinessId_Phone");

            migrationBuilder.RenameColumn(
                name: "BarberId",
                table: "Breaks",
                newName: "BusinessId");

            migrationBuilder.RenameIndex(
                name: "IX_Breaks_BarberId",
                table: "Breaks",
                newName: "IX_Breaks_BusinessId");

            migrationBuilder.RenameColumn(
                name: "BarberId",
                table: "BlockedSlots",
                newName: "BusinessId");

            migrationBuilder.RenameIndex(
                name: "IX_BlockedSlots_BarberId",
                table: "BlockedSlots",
                newName: "IX_BlockedSlots_BusinessId");

            migrationBuilder.RenameColumn(
                name: "BarberId",
                table: "Appointments",
                newName: "BusinessId");

            migrationBuilder.RenameIndex(
                name: "IX_Appointments_BarberId_Date_StartTime_Confirmed",
                table: "Appointments",
                newName: "IX_Appointments_BusinessId_Date_StartTime_Confirmed");

            migrationBuilder.RenameColumn(
                name: "BarberId",
                table: "ActivityLogs",
                newName: "BusinessId");

            migrationBuilder.RenameIndex(
                name: "IX_ActivityLogs_BarberId_CreatedAt",
                table: "ActivityLogs",
                newName: "IX_ActivityLogs_BusinessId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_Barbers_Email",
                table: "Businesses",
                newName: "IX_Businesses_Email");

            migrationBuilder.RenameIndex(
                name: "IX_Barbers_Slug",
                table: "Businesses",
                newName: "IX_Businesses_Slug");

            migrationBuilder.RenameIndex(
                name: "IX_Barbers_BusinessModel",
                table: "Businesses",
                newName: "IX_Businesses_BusinessModel");

            migrationBuilder.RenameIndex(
                name: "IX_Barbers_BusinessTypeId",
                table: "Businesses",
                newName: "IX_Businesses_BusinessTypeId");

            migrationBuilder.RenameIndex(
                name: "IX_BarberEmailOtps_Email_CreatedAt",
                table: "BusinessEmailOtps",
                newName: "IX_BusinessEmailOtps_Email_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_BarberPasswordResetOtps_Email_CreatedAt",
                table: "BusinessPasswordResetOtps",
                newName: "IX_BusinessPasswordResetOtps_Email_CreatedAt");

            migrationBuilder.AddForeignKey(
                name: "FK_ActivityLogs_Businesses_BusinessId",
                table: "ActivityLogs",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Businesses_BusinessId",
                table: "Appointments",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BlockedSlots_Businesses_BusinessId",
                table: "BlockedSlots",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Breaks_Businesses_BusinessId",
                table: "Breaks",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Businesses_BusinessId",
                table: "Customers",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Follows_Businesses_BusinessId",
                table: "Follows",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringSeries_Businesses_BusinessId",
                table: "RecurringSeries",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Services_Businesses_BusinessId",
                table: "Services",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WaitlistEntries_Businesses_BusinessId",
                table: "WaitlistEntries",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WhatsAppBookingTokens_Businesses_BusinessId",
                table: "WhatsAppBookingTokens",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WhatsAppConversationStates_Businesses_BusinessId",
                table: "WhatsAppConversationStates",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkingHours_Businesses_BusinessId",
                table: "WorkingHours",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Businesses_BusinessTypeDefinitions_BusinessTypeId",
                table: "Businesses",
                column: "BusinessTypeId",
                principalTable: "BusinessTypeDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ActivityLogs_Businesses_BusinessId",
                table: "ActivityLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Businesses_BusinessId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_BlockedSlots_Businesses_BusinessId",
                table: "BlockedSlots");

            migrationBuilder.DropForeignKey(
                name: "FK_Breaks_Businesses_BusinessId",
                table: "Breaks");

            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Businesses_BusinessId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_Follows_Businesses_BusinessId",
                table: "Follows");

            migrationBuilder.DropForeignKey(
                name: "FK_RecurringSeries_Businesses_BusinessId",
                table: "RecurringSeries");

            migrationBuilder.DropForeignKey(
                name: "FK_Services_Businesses_BusinessId",
                table: "Services");

            migrationBuilder.DropForeignKey(
                name: "FK_WaitlistEntries_Businesses_BusinessId",
                table: "WaitlistEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_WhatsAppBookingTokens_Businesses_BusinessId",
                table: "WhatsAppBookingTokens");

            migrationBuilder.DropForeignKey(
                name: "FK_WhatsAppConversationStates_Businesses_BusinessId",
                table: "WhatsAppConversationStates");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkingHours_Businesses_BusinessId",
                table: "WorkingHours");

            migrationBuilder.DropForeignKey(
                name: "FK_Businesses_BusinessTypeDefinitions_BusinessTypeId",
                table: "Businesses");

            migrationBuilder.RenameIndex(
                name: "IX_Businesses_Email",
                table: "Businesses",
                newName: "IX_Barbers_Email");

            migrationBuilder.RenameIndex(
                name: "IX_Businesses_Slug",
                table: "Businesses",
                newName: "IX_Barbers_Slug");

            migrationBuilder.RenameIndex(
                name: "IX_Businesses_BusinessModel",
                table: "Businesses",
                newName: "IX_Barbers_BusinessModel");

            migrationBuilder.RenameIndex(
                name: "IX_Businesses_BusinessTypeId",
                table: "Businesses",
                newName: "IX_Barbers_BusinessTypeId");

            migrationBuilder.RenameIndex(
                name: "IX_BusinessEmailOtps_Email_CreatedAt",
                table: "BusinessEmailOtps",
                newName: "IX_BarberEmailOtps_Email_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_BusinessPasswordResetOtps_Email_CreatedAt",
                table: "BusinessPasswordResetOtps",
                newName: "IX_BarberPasswordResetOtps_Email_CreatedAt");

            migrationBuilder.RenameColumn(
                name: "BusinessId",
                table: "WorkingHours",
                newName: "BarberId");

            migrationBuilder.RenameIndex(
                name: "IX_WorkingHours_BusinessId_DayOfWeek",
                table: "WorkingHours",
                newName: "IX_WorkingHours_BarberId_DayOfWeek");

            migrationBuilder.RenameColumn(
                name: "BusinessId",
                table: "WhatsAppConversationStates",
                newName: "BarberId");

            migrationBuilder.RenameIndex(
                name: "IX_WhatsAppConversationStates_BusinessId_Phone",
                table: "WhatsAppConversationStates",
                newName: "IX_WhatsAppConversationStates_BarberId_Phone");

            migrationBuilder.RenameColumn(
                name: "BusinessId",
                table: "WhatsAppBookingTokens",
                newName: "BarberId");

            migrationBuilder.RenameIndex(
                name: "IX_WhatsAppBookingTokens_BusinessId",
                table: "WhatsAppBookingTokens",
                newName: "IX_WhatsAppBookingTokens_BarberId");

            migrationBuilder.RenameColumn(
                name: "BusinessId",
                table: "WaitlistEntries",
                newName: "BarberId");

            migrationBuilder.RenameIndex(
                name: "IX_WaitlistEntries_BusinessId",
                table: "WaitlistEntries",
                newName: "IX_WaitlistEntries_BarberId");

            migrationBuilder.RenameColumn(
                name: "BusinessId",
                table: "Services",
                newName: "BarberId");

            migrationBuilder.RenameIndex(
                name: "IX_Services_BusinessId",
                table: "Services",
                newName: "IX_Services_BarberId");

            migrationBuilder.RenameColumn(
                name: "BusinessId",
                table: "RecurringSeries",
                newName: "BarberId");

            migrationBuilder.RenameIndex(
                name: "IX_RecurringSeries_BusinessId_IsActive",
                table: "RecurringSeries",
                newName: "IX_RecurringSeries_BarberId_IsActive");

            migrationBuilder.RenameColumn(
                name: "BusinessId",
                table: "Follows",
                newName: "BarberId");

            migrationBuilder.RenameIndex(
                name: "IX_Follows_CustomerAccountId_BusinessId",
                table: "Follows",
                newName: "IX_Follows_CustomerAccountId_BarberId");

            migrationBuilder.RenameIndex(
                name: "IX_Follows_BusinessId",
                table: "Follows",
                newName: "IX_Follows_BarberId");

            migrationBuilder.RenameColumn(
                name: "BusinessId",
                table: "Customers",
                newName: "BarberId");

            migrationBuilder.RenameIndex(
                name: "IX_Customers_BusinessId_Phone",
                table: "Customers",
                newName: "IX_Customers_BarberId_Phone");

            migrationBuilder.RenameColumn(
                name: "BusinessId",
                table: "Breaks",
                newName: "BarberId");

            migrationBuilder.RenameIndex(
                name: "IX_Breaks_BusinessId",
                table: "Breaks",
                newName: "IX_Breaks_BarberId");

            migrationBuilder.RenameColumn(
                name: "BusinessId",
                table: "BlockedSlots",
                newName: "BarberId");

            migrationBuilder.RenameIndex(
                name: "IX_BlockedSlots_BusinessId",
                table: "BlockedSlots",
                newName: "IX_BlockedSlots_BarberId");

            migrationBuilder.RenameColumn(
                name: "BusinessId",
                table: "Appointments",
                newName: "BarberId");

            migrationBuilder.RenameIndex(
                name: "IX_Appointments_BusinessId_Date_StartTime_Confirmed",
                table: "Appointments",
                newName: "IX_Appointments_BarberId_Date_StartTime_Confirmed");

            migrationBuilder.RenameColumn(
                name: "BusinessId",
                table: "ActivityLogs",
                newName: "BarberId");

            migrationBuilder.RenameIndex(
                name: "IX_ActivityLogs_BusinessId_CreatedAt",
                table: "ActivityLogs",
                newName: "IX_ActivityLogs_BarberId_CreatedAt");

            migrationBuilder.RenameTable(
                name: "Businesses",
                newName: "Barbers");

            migrationBuilder.RenameTable(
                name: "BusinessEmailOtps",
                newName: "BarberEmailOtps");

            migrationBuilder.RenameTable(
                name: "BusinessPasswordResetOtps",
                newName: "BarberPasswordResetOtps");

            migrationBuilder.AddForeignKey(
                name: "FK_ActivityLogs_Barbers_BarberId",
                table: "ActivityLogs",
                column: "BarberId",
                principalTable: "Barbers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Barbers_BarberId",
                table: "Appointments",
                column: "BarberId",
                principalTable: "Barbers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BlockedSlots_Barbers_BarberId",
                table: "BlockedSlots",
                column: "BarberId",
                principalTable: "Barbers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Breaks_Barbers_BarberId",
                table: "Breaks",
                column: "BarberId",
                principalTable: "Barbers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Barbers_BarberId",
                table: "Customers",
                column: "BarberId",
                principalTable: "Barbers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Follows_Barbers_BarberId",
                table: "Follows",
                column: "BarberId",
                principalTable: "Barbers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringSeries_Barbers_BarberId",
                table: "RecurringSeries",
                column: "BarberId",
                principalTable: "Barbers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Services_Barbers_BarberId",
                table: "Services",
                column: "BarberId",
                principalTable: "Barbers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WaitlistEntries_Barbers_BarberId",
                table: "WaitlistEntries",
                column: "BarberId",
                principalTable: "Barbers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WhatsAppBookingTokens_Barbers_BarberId",
                table: "WhatsAppBookingTokens",
                column: "BarberId",
                principalTable: "Barbers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WhatsAppConversationStates_Barbers_BarberId",
                table: "WhatsAppConversationStates",
                column: "BarberId",
                principalTable: "Barbers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkingHours_Barbers_BarberId",
                table: "WorkingHours",
                column: "BarberId",
                principalTable: "Barbers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Barbers_BusinessTypeDefinitions_BusinessTypeId",
                table: "Barbers",
                column: "BusinessTypeId",
                principalTable: "BusinessTypeDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
