using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pickory.Modules.Commerce.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommerceSettingsShopInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "shopAddress",
                table: "commerce_settings",
                type: "text",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shopName",
                table: "commerce_settings",
                type: "text",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shopPhone",
                table: "commerce_settings",
                type: "text",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shopTaxCode",
                table: "commerce_settings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "shopAddress",
                table: "commerce_settings");

            migrationBuilder.DropColumn(
                name: "shopName",
                table: "commerce_settings");

            migrationBuilder.DropColumn(
                name: "shopPhone",
                table: "commerce_settings");

            migrationBuilder.DropColumn(
                name: "shopTaxCode",
                table: "commerce_settings");
        }
    }
}
