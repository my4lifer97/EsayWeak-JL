using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUsernameAndExpandBusinessOwnerRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "OwnerName",
                table: "BusinessOwnerRequests",
                newName: "OwnerFirstName");

            migrationBuilder.AddColumn<string>(
                name: "BusinessDescription",
                table: "BusinessOwnerRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerFamilyName",
                table: "BusinessOwnerRequests",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SystemNeeds",
                table: "BusinessOwnerRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "Businesses",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Businesses_Username",
                table: "Businesses",
                column: "Username",
                unique: true);

            // Expand the seeded business-type list beyond the single "barber" vertical the
            // multi-vertical groundwork migration added. The request form now requires a choice
            // from this list; more can be added later with a plain INSERT, no deploy.
            migrationBuilder.Sql(@"
                UPDATE ""BusinessTypeDefinitions"" SET ""DisplayNameEn"" = 'Barber Shop'
                WHERE ""Id"" = '00000000000000000000000000000001';

                INSERT INTO ""BusinessTypeDefinitions"" (""Id"", ""Key"", ""DisplayNameEn"", ""DisplayNameAr"", ""DisplayNameHe"", ""IsActive"")
                VALUES
                    ('00000000000000000000000000000002', 'beauty-salon', 'Beauty Salon', 'صالون تجميل', 'מכון יופי', true),
                    ('00000000000000000000000000000003', 'hair-salon',   'Hair Salon',   'صالون شعر',   'מספרה',      true),
                    ('00000000000000000000000000000004', 'other',        'Other',        'أخرى',        'אחר',        true)
                ON CONFLICT (""Id"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Businesses_Username",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "BusinessDescription",
                table: "BusinessOwnerRequests");

            migrationBuilder.DropColumn(
                name: "OwnerFamilyName",
                table: "BusinessOwnerRequests");

            migrationBuilder.DropColumn(
                name: "SystemNeeds",
                table: "BusinessOwnerRequests");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "Businesses");

            migrationBuilder.RenameColumn(
                name: "OwnerFirstName",
                table: "BusinessOwnerRequests",
                newName: "OwnerName");
        }
    }
}
