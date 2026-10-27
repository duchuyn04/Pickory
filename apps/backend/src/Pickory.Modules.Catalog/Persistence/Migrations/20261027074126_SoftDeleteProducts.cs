using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pickory.Modules.Catalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SoftDeleteProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "deletedAt",
                table: "products",
                type: "timestamp(3) without time zone",
                precision: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deletedAt",
                table: "products");
        }
    }
}
