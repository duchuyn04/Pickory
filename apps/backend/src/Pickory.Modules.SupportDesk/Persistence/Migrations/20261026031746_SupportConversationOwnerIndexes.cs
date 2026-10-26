using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pickory.Modules.SupportDesk.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupportConversationOwnerIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "support_conversations_anonymousId_open_key",
                table: "support_conversations");

            migrationBuilder.CreateIndex(
                name: "support_conversations_anonymousId_open_key",
                table: "support_conversations",
                column: "anonymousId",
                unique: true,
                filter: "\"status\" = 'open'::\"SupportConversationStatus\" AND \"customerUserId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "support_conversations_customerUserId_open_key",
                table: "support_conversations",
                column: "customerUserId",
                unique: true,
                filter: "\"status\" = 'open'::\"SupportConversationStatus\" AND \"customerUserId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nếu DB có hội thoại `open` của tài khoản và hội thoại `open` vãng lai cùng `anonymousId`, bước tạo lại chỉ mục cũ lỗi `23505`.
            // Đóng hoặc xóa một trong hai hội thoại trước khi chạy `Down`.
            migrationBuilder.DropIndex(
                name: "support_conversations_anonymousId_open_key",
                table: "support_conversations");

            migrationBuilder.DropIndex(
                name: "support_conversations_customerUserId_open_key",
                table: "support_conversations");

            migrationBuilder.CreateIndex(
                name: "support_conversations_anonymousId_open_key",
                table: "support_conversations",
                column: "anonymousId",
                unique: true,
                filter: "\"status\" = 'open'::\"SupportConversationStatus\"");
        }
    }
}
