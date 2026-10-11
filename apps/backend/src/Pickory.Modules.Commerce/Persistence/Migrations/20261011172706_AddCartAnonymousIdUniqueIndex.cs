using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pickory.Modules.Commerce.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCartAnonymousIdUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "carts_anonymousId_key",
                table: "carts",
                column: "anonymousId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "carts_anonymousId_key",
                table: "carts");
        }
    }
}
