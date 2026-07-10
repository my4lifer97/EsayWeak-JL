using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddServicePhotoGallery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhotoMode",
                table: "Services",
                type: "text",
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "PhotoUrl",
                table: "Appointments",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServiceGalleryPhotos",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ServiceId = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceGalleryPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceGalleryPhotos_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceGalleryPhotos_ServiceId",
                table: "ServiceGalleryPhotos",
                column: "ServiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceGalleryPhotos");

            migrationBuilder.DropColumn(
                name: "PhotoMode",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "PhotoUrl",
                table: "Appointments");
        }
    }
}
