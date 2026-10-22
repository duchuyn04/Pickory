using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.SupportDesk.Entities;

namespace Pickory.Modules.SupportDesk.Persistence.Configurations;

internal sealed class SupportMessageConfiguration : IEntityTypeConfiguration<SupportMessage>
{
    public void Configure(EntityTypeBuilder<SupportMessage> entity)
    {
        entity.ToTable("support_messages");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.ConversationId).IsRequired();
        entity.Property(value => value.Role).IsRequired();
        // Tin của khách và tin hệ thống không có nhân viên gửi, nên `staffId` được rỗng.
        entity.Property(value => value.StaffId).IsRequired(false);
        // Độ dài tối đa 2.000 ký tự do API kiểm. Cột không giới hạn độ dài, nên đổi giới hạn không cần migration.
        entity.Property(value => value.Body).IsRequired();
        entity.Property(value => value.Action).IsRequired(false);
        entity.Property(value => value.CreatedAt).IsRequired();

        // Xóa hội thoại thì xóa luôn tin, vì tin không có nghĩa khi thiếu hội thoại. Việc xóa sau 90 ngày dựa vào hành vi này.
        entity.HasOne<SupportConversation>()
            .WithMany()
            .HasForeignKey(value => value.ConversationId)
            .HasConstraintName("support_messages_conversationId_fkey")
            .OnDelete(DeleteBehavior.Cascade);

        // Lịch sử chat đọc theo hội thoại và phân trang theo thời gian.
        entity.HasIndex(value => new { value.ConversationId, value.CreatedAt })
            .HasDatabaseName("support_messages_conversationId_createdAt_idx");
    }
}
