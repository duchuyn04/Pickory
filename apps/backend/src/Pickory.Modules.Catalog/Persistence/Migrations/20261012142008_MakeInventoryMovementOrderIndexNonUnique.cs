using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pickory.Modules.Catalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeInventoryMovementOrderIndexNonUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bỏ ràng buộc cũ để cùng đơn có nhiều chu kỳ giữ hàng.
            migrationBuilder.DropIndex(
                name: "inventory_movements_orderId_skuId_type_key",
                table: "inventory_movements");

            // Giữ index tra số dư; khóa theo đơn bảo vệ thao tác lặp.
            migrationBuilder.CreateIndex(
                name: "inventory_movements_orderId_skuId_type_idx",
                table: "inventory_movements",
                columns: new[] { "orderId", "skuId", "type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Bỏ index thường trước khi khôi phục ràng buộc cũ.
            migrationBuilder.DropIndex(
                name: "inventory_movements_orderId_skuId_type_idx",
                table: "inventory_movements");

            // `Down` lỗi nếu đã có dòng trùng vì chu kỳ giữ lại tạo cùng bộ khóa cũ.
            migrationBuilder.CreateIndex(
                name: "inventory_movements_orderId_skuId_type_key",
                table: "inventory_movements",
                columns: new[] { "orderId", "skuId", "type" },
                unique: true);
        }
    }
}
