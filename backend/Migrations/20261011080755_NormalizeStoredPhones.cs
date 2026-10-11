using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <summary>
    /// Data-only: rewrites every stored customer phone to the E.164 format PhoneNormalizer.Normalize
    /// now produces ("050-123-4567" -> "+972501234567"; "+1 555 ..." -> "+1555..."; "00..." -> "+..."),
    /// so the website, owner and WhatsApp paths all match the same customer.
    ///
    /// Tables with a unique phone index (Customers per business, CustomerAccounts globally,
    /// WhatsAppConversationStates per business) are only rewritten when no other row there would end
    /// up with the same canonical phone. Colliding rows are left exactly as they are, never merged or
    /// deleted -- a merge would need judgment about which name/account/history wins, so it can't
    /// fail the migration or silently join two people. Leftover rows simply stop being matched by
    /// new bookings, which use the canonical row.
    /// </summary>
    public partial class NormalizeStoredPhones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Mirrors PhoneNormalizer.Normalize exactly.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION tmp_canon_phone(p text) RETURNS text LANGUAGE sql IMMUTABLE AS $$
                    SELECT CASE
                        WHEN regexp_replace(p, '\D', '', 'g') = '' THEN p
                        WHEN btrim(p) LIKE '+%' THEN '+' || regexp_replace(p, '\D', '', 'g')
                        WHEN regexp_replace(p, '\D', '', 'g') LIKE '00%' THEN '+' || substr(regexp_replace(p, '\D', '', 'g'), 3)
                        ELSE '+972' || ltrim(regexp_replace(p, '\D', '', 'g'), '0')
                    END
                $$;
                """);

            migrationBuilder.Sql("""
                UPDATE "Customers" c SET "Phone" = tmp_canon_phone(c."Phone")
                WHERE c."Phone" <> tmp_canon_phone(c."Phone")
                  AND NOT EXISTS (SELECT 1 FROM "Customers" o
                                  WHERE o."BusinessId" = c."BusinessId" AND o."Id" <> c."Id"
                                    AND tmp_canon_phone(o."Phone") = tmp_canon_phone(c."Phone"));
                """);

            migrationBuilder.Sql("""
                UPDATE "CustomerAccounts" c SET "Phone" = tmp_canon_phone(c."Phone")
                WHERE c."Phone" <> tmp_canon_phone(c."Phone")
                  AND NOT EXISTS (SELECT 1 FROM "CustomerAccounts" o
                                  WHERE o."Id" <> c."Id" AND tmp_canon_phone(o."Phone") = tmp_canon_phone(c."Phone"));
                """);

            migrationBuilder.Sql("""
                UPDATE "WhatsAppConversationStates" c SET "Phone" = tmp_canon_phone(c."Phone")
                WHERE c."Phone" <> tmp_canon_phone(c."Phone")
                  AND NOT EXISTS (SELECT 1 FROM "WhatsAppConversationStates" o
                                  WHERE o."BusinessId" = c."BusinessId" AND o."Id" <> c."Id"
                                    AND tmp_canon_phone(o."Phone") = tmp_canon_phone(c."Phone"));
                """);

            // No unique index on these.
            migrationBuilder.Sql("""UPDATE "CustomerOtps" SET "Phone" = tmp_canon_phone("Phone") WHERE "Phone" <> tmp_canon_phone("Phone");""");
            migrationBuilder.Sql("""UPDATE "WhatsAppBookingTokens" SET "Phone" = tmp_canon_phone("Phone") WHERE "Phone" <> tmp_canon_phone("Phone");""");
            migrationBuilder.Sql("""UPDATE "ChatbotInquiries" SET "CustomerPhone" = tmp_canon_phone("CustomerPhone") WHERE "CustomerPhone" <> tmp_canon_phone("CustomerPhone");""");

            migrationBuilder.Sql("DROP FUNCTION tmp_canon_phone(text);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The original formatting isn't recoverable, and the canonical form is valid under the
            // old code too (it was already what WhatsApp logins stored).
        }
    }
}
