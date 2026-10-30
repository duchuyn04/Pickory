using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pickory.Modules.Commerce.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedAddressSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "deletedAt",
                table: "saved_addresses",
                type: "timestamp(3) without time zone",
                precision: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deletedAt",
                table: "saved_addresses");
        }
    }
}
