using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pickory.Modules.ActivityTracking.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialActivityTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_counters",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    metric = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("daily_counters_pkey", x => new { x.date, x.metric });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_counters");
        }
    }
}
