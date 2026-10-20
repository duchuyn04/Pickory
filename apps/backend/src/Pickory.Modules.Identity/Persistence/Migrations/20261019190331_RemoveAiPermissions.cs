using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pickory.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAiPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Module AI đã gỡ nên hai quyền này không còn trong code. Xóa dòng cũ để API khởi động không cảnh báo quyền lạ.
            // Chỉ xóa đúng hai mã quyền. Migration không đổi cấu trúc bảng và không đụng quyền khác.
            migrationBuilder.Sql("DELETE FROM role_permissions WHERE permission IN ('ai-usage:read', 'ai-settings:manage');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Không khôi phục dòng đã xóa. Hai quyền đã bị xóa khỏi code, nên dòng quyền khôi phục không có ý nghĩa.
        }
    }
}
