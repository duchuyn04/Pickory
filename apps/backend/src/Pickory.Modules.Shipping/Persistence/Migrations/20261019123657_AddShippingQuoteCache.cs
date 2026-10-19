using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pickory.Modules.Shipping.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingQuoteCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shipping_quote_cache",
                columns: table => new
                {
                    receiverWardCode = table.Column<string>(type: "text", nullable: false),
                    pickupWardCode = table.Column<string>(type: "text", nullable: false),
                    serviceTypeId = table.Column<int>(type: "integer", nullable: false),
                    chargeableWeightGrams = table.Column<long>(type: "bigint", nullable: false),
                    insuredValue = table.Column<int>(type: "integer", nullable: false),
                    codAmount = table.Column<int>(type: "integer", nullable: false),
                    fee = table.Column<int>(type: "integer", nullable: false),
                    savedAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("shipping_quote_cache_pkey", x => new { x.receiverWardCode, x.pickupWardCode, x.serviceTypeId, x.chargeableWeightGrams, x.insuredValue, x.codAmount });
                });

            migrationBuilder.CreateIndex(
                name: "shipping_quote_cache_savedAt_idx",
                table: "shipping_quote_cache",
                column: "savedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shipping_quote_cache");
        }
    }
}
