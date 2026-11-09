using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pickory.Modules.SupportDesk.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupportMessageSessionId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Thêm cột mã phiên hỗ trợ cho phép rỗng để liên kết tin nhắn với từng phiên.
            migrationBuilder.AddColumn<string>(
                name: "sessionId",
                table: "support_messages",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Xóa cột mã phiên hỗ trợ khi hoàn tác migration.
            migrationBuilder.DropColumn(
                name: "sessionId",
                table: "support_messages");
        }
    }
}
