using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pickory.Modules.Commerce.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnableUnaccentExtension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tìm đơn hàng so sánh tên và email không dấu bằng `unaccent`. `IF NOT EXISTS` giúp migration chạy được
            // trên DB đã có extension do module khác tạo.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS unaccent;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Không xóa extension. Module `identity` và `catalog` dùng chung extension này, nên xóa ở đây làm hỏng tìm kiếm của chúng.
        }
    }
}
