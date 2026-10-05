using Microsoft.EntityFrameworkCore.Migrations;

namespace Pickory.Modules.Catalog.Persistence.Migrations;

public partial class PrismaBaseline : Migration
{
    // Schema Node đã có trong DB. Mốc chỉ ghi lịch sử EF Core, không tạo lại bảng hoặc extension.
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    // Gỡ mốc không được xóa dữ liệu hoặc cấu trúc do Node tạo.
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
