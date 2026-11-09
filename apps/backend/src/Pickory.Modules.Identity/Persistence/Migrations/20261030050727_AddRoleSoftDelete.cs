using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pickory.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "roles_code_key",
                table: "roles");

            migrationBuilder.AddColumn<DateTime>(
                name: "deletedAt",
                table: "roles",
                type: "timestamp(3) without time zone",
                precision: 3,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "roles_code_key",
                table: "roles",
                column: "code",
                unique: true,
                filter: "\"deletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "roles_code_key",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "deletedAt",
                table: "roles");

            migrationBuilder.CreateIndex(
                name: "roles_code_key",
                table: "roles",
                column: "code",
                unique: true);
        }
    }
}
