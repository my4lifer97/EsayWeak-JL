using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberSaas.Api.Migrations
{
    /// <inheritdoc />
    public partial class SplitChatbotMessagesByLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "ChatbotWelcomeMessageEn", table: "Businesses", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "ChatbotWelcomeMessageAr", table: "Businesses", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "ChatbotWelcomeMessageHe", table: "Businesses", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "ChatbotConfirmationMessageEn", table: "Businesses", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "ChatbotConfirmationMessageAr", table: "Businesses", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "ChatbotConfirmationMessageHe", table: "Businesses", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "ChatbotFinalMessageEn", table: "Businesses", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "ChatbotFinalMessageAr", table: "Businesses", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "ChatbotFinalMessageHe", table: "Businesses", type: "text", nullable: true);

            // Preserve each business's existing single-language text by copying it into the new
            // column matching their current Language setting (stored as text 'EN'/'AR'/'HE', not
            // an int -- see AppDbContext's HasConversion<string>() on Business.Language). The other
            // two language slots for that message stay null, which read-side code already treats
            // as "use the built-in default text for that language" -- never borrows another
            // language's custom text.
            migrationBuilder.Sql(@"
                UPDATE ""Businesses"" SET ""ChatbotWelcomeMessageEn"" = ""ChatbotWelcomeMessage"" WHERE ""Language"" = 'EN' AND ""ChatbotWelcomeMessage"" IS NOT NULL;
                UPDATE ""Businesses"" SET ""ChatbotWelcomeMessageAr"" = ""ChatbotWelcomeMessage"" WHERE ""Language"" = 'AR' AND ""ChatbotWelcomeMessage"" IS NOT NULL;
                UPDATE ""Businesses"" SET ""ChatbotWelcomeMessageHe"" = ""ChatbotWelcomeMessage"" WHERE ""Language"" = 'HE' AND ""ChatbotWelcomeMessage"" IS NOT NULL;
                UPDATE ""Businesses"" SET ""ChatbotConfirmationMessageEn"" = ""ChatbotConfirmationMessage"" WHERE ""Language"" = 'EN' AND ""ChatbotConfirmationMessage"" IS NOT NULL;
                UPDATE ""Businesses"" SET ""ChatbotConfirmationMessageAr"" = ""ChatbotConfirmationMessage"" WHERE ""Language"" = 'AR' AND ""ChatbotConfirmationMessage"" IS NOT NULL;
                UPDATE ""Businesses"" SET ""ChatbotConfirmationMessageHe"" = ""ChatbotConfirmationMessage"" WHERE ""Language"" = 'HE' AND ""ChatbotConfirmationMessage"" IS NOT NULL;
                UPDATE ""Businesses"" SET ""ChatbotFinalMessageEn"" = ""ChatbotFinalMessage"" WHERE ""Language"" = 'EN' AND ""ChatbotFinalMessage"" IS NOT NULL;
                UPDATE ""Businesses"" SET ""ChatbotFinalMessageAr"" = ""ChatbotFinalMessage"" WHERE ""Language"" = 'AR' AND ""ChatbotFinalMessage"" IS NOT NULL;
                UPDATE ""Businesses"" SET ""ChatbotFinalMessageHe"" = ""ChatbotFinalMessage"" WHERE ""Language"" = 'HE' AND ""ChatbotFinalMessage"" IS NOT NULL;
            ");

            migrationBuilder.DropColumn(name: "ChatbotWelcomeMessage", table: "Businesses");
            migrationBuilder.DropColumn(name: "ChatbotConfirmationMessage", table: "Businesses");
            migrationBuilder.DropColumn(name: "ChatbotFinalMessage", table: "Businesses");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "ChatbotWelcomeMessage", table: "Businesses", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "ChatbotConfirmationMessage", table: "Businesses", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "ChatbotFinalMessage", table: "Businesses", type: "text", nullable: true);

            // Best-effort/lossy on the way back down -- picks whichever language slot has content,
            // preferring English. Reverse migrations aren't exercised in practice in this project.
            migrationBuilder.Sql(@"
                UPDATE ""Businesses"" SET ""ChatbotWelcomeMessage"" = COALESCE(""ChatbotWelcomeMessageEn"", ""ChatbotWelcomeMessageAr"", ""ChatbotWelcomeMessageHe"");
                UPDATE ""Businesses"" SET ""ChatbotConfirmationMessage"" = COALESCE(""ChatbotConfirmationMessageEn"", ""ChatbotConfirmationMessageAr"", ""ChatbotConfirmationMessageHe"");
                UPDATE ""Businesses"" SET ""ChatbotFinalMessage"" = COALESCE(""ChatbotFinalMessageEn"", ""ChatbotFinalMessageAr"", ""ChatbotFinalMessageHe"");
            ");

            migrationBuilder.DropColumn(name: "ChatbotWelcomeMessageEn", table: "Businesses");
            migrationBuilder.DropColumn(name: "ChatbotWelcomeMessageAr", table: "Businesses");
            migrationBuilder.DropColumn(name: "ChatbotWelcomeMessageHe", table: "Businesses");
            migrationBuilder.DropColumn(name: "ChatbotConfirmationMessageEn", table: "Businesses");
            migrationBuilder.DropColumn(name: "ChatbotConfirmationMessageAr", table: "Businesses");
            migrationBuilder.DropColumn(name: "ChatbotConfirmationMessageHe", table: "Businesses");
            migrationBuilder.DropColumn(name: "ChatbotFinalMessageEn", table: "Businesses");
            migrationBuilder.DropColumn(name: "ChatbotFinalMessageAr", table: "Businesses");
            migrationBuilder.DropColumn(name: "ChatbotFinalMessageHe", table: "Businesses");
        }
    }
}
