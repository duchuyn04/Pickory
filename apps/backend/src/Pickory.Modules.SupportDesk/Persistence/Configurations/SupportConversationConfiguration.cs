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

        // Mỗi khách có tối đa một hội thoại đang mở. Hai tin đầu đến cùng lúc không tạo được hai hội thoại.
        entity.HasIndex(value => value.AnonymousId)
            .IsUnique()
            // Điều kiện lọc chỉ tính hội thoại `open`, nên hội thoại đã đóng không chặn hội thoại mới.
            // Ép kiểu enum tường minh để model khớp DB.
            .HasFilter("\"status\" = 'open'::\"SupportConversationStatus\"")
            .HasDatabaseName("support_conversations_anonymousId_open_key");
    }
}
