using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.SupportDesk.Entities;

namespace Pickory.Modules.SupportDesk.Persistence.Configurations;

internal sealed class SupportConversationConfiguration : IEntityTypeConfiguration<SupportConversation>
{
    public void Configure(EntityTypeBuilder<SupportConversation> entity)
    {
        entity.ToTable("support_conversations");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        // Khách vãng lai chưa có tài khoản, nên `customerUserId` được rỗng. `anonymousId` luôn có, vì chủ hội thoại cần một mã.
        entity.Property(value => value.CustomerUserId).IsRequired(false);
        entity.Property(value => value.AnonymousId).IsRequired();
        entity.Property(value => value.Status).IsRequired();
        entity.Property(value => value.CreatedAt).IsRequired();
        entity.Property(value => value.LastMessageAt).IsRequired();
        entity.Property(value => value.ClosedAt).IsRequired(false);

        // Mỗi khách vãng lai có tối đa một hội thoại đang mở. Hai tin đầu đến cùng lúc không tạo được hai hội thoại.
        entity.HasIndex(value => value.AnonymousId)
            .IsUnique()
            // Chỉ tính hội thoại `open` chưa gắn tài khoản, vì cookie `pk_anon` không đổi khi chủ đăng xuất.
            // Ép kiểu enum tường minh để model khớp DB.
            .HasFilter("\"status\" = 'open'::\"SupportConversationStatus\" AND \"customerUserId\" IS NULL")
            .HasDatabaseName("support_conversations_anonymousId_open_key");

        // Mỗi tài khoản có tối đa một hội thoại đang mở, dù khách dùng nhiều trình duyệt với nhiều `anonymousId`.
        entity.HasIndex(value => value.CustomerUserId)
            .IsUnique()
            .HasFilter("\"status\" = 'open'::\"SupportConversationStatus\" AND \"customerUserId\" IS NOT NULL")
            .HasDatabaseName("support_conversations_customerUserId_open_key");
    }
}
