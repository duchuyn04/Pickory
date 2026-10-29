using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.SupportDesk.Entities;

namespace Pickory.Modules.SupportDesk.Persistence.Configurations;

internal sealed class SupportSessionConfiguration : IEntityTypeConfiguration<SupportSession>
{
    public void Configure(EntityTypeBuilder<SupportSession> entity)
    {
        entity.ToTable("support_sessions");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.ConversationId).IsRequired();
        entity.Property(value => value.HandoffAt).IsRequired();
        // Cột không có mặc định ở DB, vì EF Core thay giá trị `false` bằng mặc định đó. Mã nguồn luôn đặt giá trị.
        entity.Property(value => value.HandoffInHours).IsRequired();
        entity.Property(value => value.CustomerUserId).IsRequired(false);
        entity.Property(value => value.AnonymousId).IsRequired();
        entity.Property(value => value.StaffId).IsRequired(false);
        entity.Property(value => value.ClaimedAt).IsRequired(false);
        entity.Property(value => value.FirstResponseAt).IsRequired(false);
        entity.Property(value => value.ClosedAt).IsRequired(false);
        entity.Property(value => value.ClosedBy).IsRequired(false);
        entity.Property(value => value.Csat).IsRequired(false);
        entity.Property(value => value.TransferNote).IsRequired(false);
        entity.Property(value => value.NudgedAt).IsRequired(false);

        // Xóa hội thoại thì xóa luôn phiên, vì phiên không có nghĩa khi thiếu hội thoại. Việc xóa sau 90 ngày dựa vào hành vi này.
        entity.HasOne<SupportConversation>()
            .WithMany()
            .HasForeignKey(value => value.ConversationId)
            .HasConstraintName("support_sessions_conversationId_fkey")
            .OnDelete(DeleteBehavior.Cascade);

        // Mỗi hội thoại có tối đa một phiên mở. Hai tin của khách đến cùng lúc không tạo được phiên thứ hai.
        entity.HasIndex(value => value.ConversationId)
            .IsUnique()
            // Điều kiện lọc chỉ tính phiên chưa đóng, nên phiên đã đóng không chặn phiên mới.
            .HasFilter("\"closedAt\" IS NULL")
            .HasDatabaseName("support_sessions_conversationId_open_key");

        // Báo cáo và hàng chờ lọc theo trạng thái đóng rồi sắp theo lúc chuyển phiên.
        entity.HasIndex(value => new { value.ClosedAt, value.HandoffAt })
            .HasDatabaseName("support_sessions_closedAt_handoffAt_idx");
    }
}
