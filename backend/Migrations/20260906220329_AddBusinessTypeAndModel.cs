using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessTypeAndModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BusinessModel",
                table: "Barbers",
                type: "text",
                nullable: false,
                defaultValue: "Appointment");

            migrationBuilder.AddColumn<string>(
                name: "BusinessTypeId",
                table: "Barbers",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BusinessTypeDefinitions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    DisplayNameEn = table.Column<string>(type: "text", nullable: false),
                    DisplayNameAr = table.Column<string>(type: "text", nullable: false),
                    DisplayNameHe = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessTypeDefinitions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Barbers_BusinessModel",
                table: "Barbers",
                column: "BusinessModel");

            migrationBuilder.CreateIndex(
                name: "IX_Barbers_BusinessTypeId",
                table: "Barbers",
                column: "BusinessTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessTypeDefinitions_Key",
                table: "BusinessTypeDefinitions",
                column: "Key",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Barbers_BusinessTypeDefinitions_BusinessTypeId",
                table: "Barbers",
                column: "BusinessTypeId",
                principalTable: "BusinessTypeDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Seed the "barber" vertical and backfill every existing tenant onto it, so no row
            // is left with a null BusinessTypeId after this migration -- every current tenant
            // predates the BusinessType concept but is unambiguously a barber.
            migrationBuilder.Sql(@"
                INSERT INTO ""BusinessTypeDefinitions"" (""Id"", ""Key"", ""DisplayNameEn"", ""DisplayNameAr"", ""DisplayNameHe"", ""IsActive"")
                VALUES ('00000000000000000000000000000001', 'barber', 'Barber', 'حلاق', 'ספר', true);

                UPDATE ""Barbers"" SET ""BusinessTypeId"" = '00000000000000000000000000000001'
                WHERE ""BusinessTypeId"" IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Barbers_BusinessTypeDefinitions_BusinessTypeId",
                table: "Barbers");

            migrationBuilder.DropTable(
                name: "BusinessTypeDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_Barbers_BusinessModel",
                table: "Barbers");

            migrationBuilder.DropIndex(
                name: "IX_Barbers_BusinessTypeId",
                table: "Barbers");

            migrationBuilder.DropColumn(
                name: "BusinessModel",
                table: "Barbers");

            migrationBuilder.DropColumn(
                name: "BusinessTypeId",
                table: "Barbers");
        }
    }
}
