using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddChatAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttachmentId",
                table: "ChatMessages",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChatAttachments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BusinessId = table.Column<string>(type: "text", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatAttachments_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_AttachmentId",
                table: "ChatMessages",
                column: "AttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatAttachments_BusinessId",
                table: "ChatAttachments",
                column: "BusinessId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_ChatAttachments_AttachmentId",
                table: "ChatMessages",
                column: "AttachmentId",
                principalTable: "ChatAttachments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_ChatAttachments_AttachmentId",
                table: "ChatMessages");

            migrationBuilder.DropTable(
                name: "ChatAttachments");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_AttachmentId",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "AttachmentId",
                table: "ChatMessages");
        }
    }
}
