using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.SupportDesk.Entities;

namespace Pickory.Modules.SupportDesk.Persistence.Configurations;

internal sealed class CannedResponseConfiguration : IEntityTypeConfiguration<CannedResponse>
{
    public void Configure(EntityTypeBuilder<CannedResponse> entity)
    {
        entity.ToTable("canned_responses");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        // Mẫu dùng chung của cả đội không có chủ, nên `ownerId` được rỗng.
        entity.Property(value => value.OwnerId).IsRequired(false);
        entity.Property(value => value.Shortcut).IsRequired();
        entity.Property(value => value.Body).IsRequired();
        // Cột không có mặc định ở DB, vì EF Core thay giá trị `false` bằng mặc định đó. Mã nguồn luôn đặt giá trị.
        entity.Property(value => value.Shared).IsRequired();
        entity.Property(value => value.CreatedAt).IsRequired();
        entity.Property(value => value.UpdatedAt).IsRequired();
    }
}
