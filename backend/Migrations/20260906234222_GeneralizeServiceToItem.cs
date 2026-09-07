using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class GeneralizeServiceToItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop FK constraints before renaming their columns/tables -- EF's scaffolder always
            // drops+recreates a FK whose name changes (the name embeds the old table/column
            // names), but this is metadata-only in Postgres (no data rewrite), same discipline as
            // the Barber->Business rename migration.
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Services_ServiceId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_RecurringSeries_Services_ServiceId",
                table: "RecurringSeries");

            migrationBuilder.DropForeignKey(
                name: "FK_WhatsAppBookingTokens_Services_ServiceId",
                table: "WhatsAppBookingTokens");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceGalleryPhotos_Services_ServiceId",
                table: "ServiceGalleryPhotos");

            migrationBuilder.DropForeignKey(
                name: "FK_Services_Businesses_BusinessId",
                table: "Services");

            // Renames only -- Postgres RENAME TABLE/COLUMN are metadata-only operations, never a
            // drop+recreate, so no existing service/item data is lost.
            migrationBuilder.RenameTable(
                name: "Services",
                newName: "Items");

            migrationBuilder.RenameTable(
                name: "ServiceGalleryPhotos",
                newName: "ItemGalleryPhotos");

            migrationBuilder.RenameColumn(
                name: "ServiceId",
                table: "ItemGalleryPhotos",
                newName: "ItemId");

            migrationBuilder.RenameIndex(
                name: "IX_ServiceGalleryPhotos_ServiceId",
                table: "ItemGalleryPhotos",
                newName: "IX_ItemGalleryPhotos_ItemId");

            migrationBuilder.RenameIndex(
                name: "IX_Services_BusinessId",
                table: "Items",
                newName: "IX_Items_BusinessId");

            migrationBuilder.RenameColumn(
                name: "ServiceId",
                table: "WhatsAppBookingTokens",
                newName: "ItemId");

            migrationBuilder.RenameIndex(
                name: "IX_WhatsAppBookingTokens_ServiceId",
                table: "WhatsAppBookingTokens",
                newName: "IX_WhatsAppBookingTokens_ItemId");

            migrationBuilder.RenameColumn(
                name: "ServiceId",
                table: "RecurringSeries",
                newName: "ItemId");

            migrationBuilder.RenameIndex(
                name: "IX_RecurringSeries_ServiceId",
                table: "RecurringSeries",
                newName: "IX_RecurringSeries_ItemId");

            migrationBuilder.RenameColumn(
                name: "ServiceId",
                table: "Appointments",
                newName: "ItemId");

            migrationBuilder.RenameIndex(
                name: "IX_Appointments_ServiceId",
                table: "Appointments",
                newName: "IX_Appointments_ItemId");

            // Widen Price/DurationMinutes to nullable -- a showcase-only item may have no
            // duration (never bookable) or no listed price ("contact for price"). Existing
            // non-null data is unaffected by widening a column's nullability.
            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "Items",
                type: "numeric(10,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)");

            migrationBuilder.AlterColumn<int>(
                name: "DurationMinutes",
                table: "Items",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            // Explicit DB-level default (not just the C# property initializer) -- every
            // pre-existing service is a real bookable offering, so it must backfill as bookable,
            // same reasoning as the ChatbotEnabled/BusinessModel defaults added earlier.
            migrationBuilder.AddColumn<bool>(
                name: "IsBookable",
                table: "Items",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Items_Businesses_BusinessId",
                table: "Items",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ItemGalleryPhotos_Items_ItemId",
                table: "ItemGalleryPhotos",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Items_ItemId",
                table: "Appointments",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringSeries_Items_ItemId",
                table: "RecurringSeries",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WhatsAppBookingTokens_Items_ItemId",
                table: "WhatsAppBookingTokens",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Items_Businesses_BusinessId",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_ItemGalleryPhotos_Items_ItemId",
                table: "ItemGalleryPhotos");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Items_ItemId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_RecurringSeries_Items_ItemId",
                table: "RecurringSeries");

            migrationBuilder.DropForeignKey(
                name: "FK_WhatsAppBookingTokens_Items_ItemId",
                table: "WhatsAppBookingTokens");

            migrationBuilder.DropColumn(
                name: "IsBookable",
                table: "Items");

            migrationBuilder.AlterColumn<int>(
                name: "DurationMinutes",
                table: "Items",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "Items",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)",
                oldNullable: true);

            migrationBuilder.RenameIndex(
                name: "IX_Appointments_ItemId",
                table: "Appointments",
                newName: "IX_Appointments_ServiceId");

            migrationBuilder.RenameColumn(
                name: "ItemId",
                table: "Appointments",
                newName: "ServiceId");

            migrationBuilder.RenameIndex(
                name: "IX_RecurringSeries_ItemId",
                table: "RecurringSeries",
                newName: "IX_RecurringSeries_ServiceId");

            migrationBuilder.RenameColumn(
                name: "ItemId",
                table: "RecurringSeries",
                newName: "ServiceId");

            migrationBuilder.RenameIndex(
                name: "IX_WhatsAppBookingTokens_ItemId",
                table: "WhatsAppBookingTokens",
                newName: "IX_WhatsAppBookingTokens_ServiceId");

            migrationBuilder.RenameColumn(
                name: "ItemId",
                table: "WhatsAppBookingTokens",
                newName: "ServiceId");

            migrationBuilder.RenameIndex(
                name: "IX_Items_BusinessId",
                table: "Items",
                newName: "IX_Services_BusinessId");

            migrationBuilder.RenameIndex(
                name: "IX_ItemGalleryPhotos_ItemId",
                table: "ItemGalleryPhotos",
                newName: "IX_ServiceGalleryPhotos_ServiceId");

            migrationBuilder.RenameColumn(
                name: "ItemId",
                table: "ItemGalleryPhotos",
                newName: "ServiceId");

            migrationBuilder.RenameTable(
                name: "ItemGalleryPhotos",
                newName: "ServiceGalleryPhotos");

            migrationBuilder.RenameTable(
                name: "Items",
                newName: "Services");

            migrationBuilder.AddForeignKey(
                name: "FK_Services_Businesses_BusinessId",
                table: "Services",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceGalleryPhotos_Services_ServiceId",
                table: "ServiceGalleryPhotos",
                column: "ServiceId",
                principalTable: "Services",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Services_ServiceId",
                table: "Appointments",
                column: "ServiceId",
                principalTable: "Services",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringSeries_Services_ServiceId",
                table: "RecurringSeries",
                column: "ServiceId",
                principalTable: "Services",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WhatsAppBookingTokens_Services_ServiceId",
                table: "WhatsAppBookingTokens",
                column: "ServiceId",
                principalTable: "Services",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
