using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.SupportDesk.Entities;

namespace Pickory.Modules.SupportDesk.Persistence.Configurations;

internal sealed class CallbackRequestConfiguration : IEntityTypeConfiguration<CallbackRequest>
{
    public void Configure(EntityTypeBuilder<CallbackRequest> entity)
    {
        entity.ToTable("callback_requests");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        // Hội thoại bị xóa thì `conversationId` thành rỗng, nhưng dòng gọi lại còn lại cho báo cáo.
        entity.Property(value => value.ConversationId).IsRequired(false);
        entity.Property(value => value.AnonymousId).IsRequired();
        entity.Property(value => value.CustomerUserId).IsRequired(false);
        // Job xóa số điện thoại sau 30 ngày, nên cột phải cho phép rỗng.
        entity.Property(value => value.Phone).IsRequired(false);
        entity.Property(value => value.Note).IsRequired(false);
        entity.Property(value => value.Status).IsRequired();
        entity.Property(value => value.HandledBy).IsRequired(false);
        entity.Property(value => value.CreatedAt).IsRequired();
        entity.Property(value => value.HandledAt).IsRequired(false);

        // Xóa hội thoại chỉ gỡ liên kết, không xóa dòng gọi lại. Báo cáo vẫn đếm yêu cầu gọi lại đã xảy ra.
        entity.HasOne<SupportConversation>()
            .WithMany()
            .HasForeignKey(value => value.ConversationId)
            .HasConstraintName("callback_requests_conversationId_fkey")
            .OnDelete(DeleteBehavior.SetNull);

        // Tab Gọi lại lọc theo trạng thái và sắp theo ngày tạo.
        entity.HasIndex(value => new { value.Status, value.CreatedAt })
            .HasDatabaseName("callback_requests_status_createdAt_idx");
    }
}
