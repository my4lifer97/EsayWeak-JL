using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerAccountsFollowsAndOtp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomerAccountId",
                table: "Customers",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerAccounts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    FamilyName = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerOtps",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: false),
                    CodeHash = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Consumed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerOtps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Follows",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    CustomerAccountId = table.Column<string>(type: "text", nullable: false),
                    BarberId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Follows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Follows_Barbers_BarberId",
                        column: x => x.BarberId,
                        principalTable: "Barbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Follows_CustomerAccounts_CustomerAccountId",
                        column: x => x.CustomerAccountId,
                        principalTable: "CustomerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CustomerAccountId",
                table: "Customers",
                column: "CustomerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccounts_Phone",
                table: "CustomerAccounts",
                column: "Phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerOtps_Phone_CreatedAt",
                table: "CustomerOtps",
                columns: new[] { "Phone", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Follows_BarberId",
                table: "Follows",
                column: "BarberId");

            migrationBuilder.CreateIndex(
                name: "IX_Follows_CustomerAccountId_BarberId",
                table: "Follows",
                columns: new[] { "CustomerAccountId", "BarberId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_CustomerAccounts_CustomerAccountId",
                table: "Customers",
                column: "CustomerAccountId",
                principalTable: "CustomerAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Backfill: seed one global CustomerAccount per distinct existing Customer phone number,
            // then link every matching Customer row to it. Pre-existing data was never a single-account
            // model, so if the same phone has different Name/FamilyName spellings across barbers this
            // arbitrarily picks one (deterministically, by lowest Id) as the account's display name —
            // cosmetic only, corrected the moment the customer actually logs in.
            migrationBuilder.Sql("""
                INSERT INTO "CustomerAccounts" ("Id", "Phone", "Name", "FamilyName", "CreatedAt")
                SELECT DISTINCT ON ("Phone")
                    md5(random()::text || clock_timestamp()::text || "Phone"),
                    "Phone", "Name", "FamilyName", now()
                FROM "Customers"
                WHERE "Phone" IS NOT NULL AND "Phone" <> ''
                ORDER BY "Phone", "Id";

                UPDATE "Customers" c
                SET "CustomerAccountId" = ca."Id"
                FROM "CustomerAccounts" ca
                WHERE c."Phone" = ca."Phone" AND c."Phone" <> '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_CustomerAccounts_CustomerAccountId",
                table: "Customers");

            migrationBuilder.DropTable(
                name: "CustomerOtps");

            migrationBuilder.DropTable(
                name: "Follows");

            migrationBuilder.DropTable(
                name: "CustomerAccounts");

            migrationBuilder.DropIndex(
                name: "IX_Customers_CustomerAccountId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "CustomerAccountId",
                table: "Customers");
        }
    }
}
