using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pickory.Modules.Shipping.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingFallbackFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shipping_fallback_fee",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    sameProvinceBaseFee = table.Column<int>(type: "integer", nullable: true),
                    sameProvinceBaseWeightGrams = table.Column<int>(type: "integer", nullable: true),
                    sameProvinceStepFee = table.Column<int>(type: "integer", nullable: true),
                    sameRegionBaseFee = table.Column<int>(type: "integer", nullable: true),
                    sameRegionBaseWeightGrams = table.Column<int>(type: "integer", nullable: true),
                    sameRegionStepFee = table.Column<int>(type: "integer", nullable: true),
                    crossRegionBaseFee = table.Column<int>(type: "integer", nullable: true),
                    crossRegionBaseWeightGrams = table.Column<int>(type: "integer", nullable: true),
                    crossRegionStepFee = table.Column<int>(type: "integer", nullable: true),
                    updatedAt = table.Column<DateTime>(type: "timestamp(3) without time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("shipping_fallback_fee_pkey", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shipping_fallback_fee");
        }
    }
}
