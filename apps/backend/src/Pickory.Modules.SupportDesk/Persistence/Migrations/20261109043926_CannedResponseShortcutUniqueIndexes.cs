using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pickory.Modules.SupportDesk.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CannedResponseShortcutUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "canned_responses_ownerId_shortcut_personal_key",
                table: "canned_responses",
                columns: new[] { "ownerId", "shortcut" },
                unique: true,
                filter: "\"shared\" = false");

            migrationBuilder.CreateIndex(
                name: "canned_responses_shortcut_shared_key",
                table: "canned_responses",
                column: "shortcut",
                unique: true,
                filter: "\"shared\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "canned_responses_ownerId_shortcut_personal_key",
                table: "canned_responses");

            migrationBuilder.DropIndex(
                name: "canned_responses_shortcut_shared_key",
                table: "canned_responses");
        }
    }
}
