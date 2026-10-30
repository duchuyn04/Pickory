using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pickory.Modules.Catalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "products_slug_key",
                table: "products");

            migrationBuilder.DropIndex(
                name: "categories_slug_key",
                table: "categories");

            migrationBuilder.DropIndex(
                name: "brands_slug_key",
                table: "brands");

            migrationBuilder.AddColumn<DateTime>(
                name: "deletedAt",
                table: "products",
                type: "timestamp(3) without time zone",
                precision: 3,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deletedAt",
                table: "categories",
                type: "timestamp(3) without time zone",
                precision: 3,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deletedAt",
                table: "brands",
                type: "timestamp(3) without time zone",
                precision: 3,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "products_slug_key",
                table: "products",
                column: "slug",
                unique: true,
                filter: "\"deletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "categories_slug_key",
                table: "categories",
                column: "slug",
                unique: true,
                filter: "\"deletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "brands_slug_key",
                table: "brands",
                column: "slug",
                unique: true,
                filter: "\"deletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "products_slug_key",
                table: "products");

            migrationBuilder.DropIndex(
                name: "categories_slug_key",
                table: "categories");

            migrationBuilder.DropIndex(
                name: "brands_slug_key",
                table: "brands");

            migrationBuilder.DropColumn(
                name: "deletedAt",
                table: "products");

            migrationBuilder.DropColumn(
                name: "deletedAt",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "deletedAt",
                table: "brands");

            migrationBuilder.CreateIndex(
                name: "products_slug_key",
                table: "products",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "categories_slug_key",
                table: "categories",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "brands_slug_key",
                table: "brands",
                column: "slug",
                unique: true);
        }
    }
}
