using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Commerce.Entities;

namespace Pickory.Modules.Commerce.Persistence.Configurations;

internal sealed class SavedAddressConfiguration : IEntityTypeConfiguration<SavedAddress>
{
    public void Configure(EntityTypeBuilder<SavedAddress> entity)
    {
        entity.ToTable("saved_addresses");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.UserId).IsRequired();
        entity.Property(value => value.Label).IsRequired();
        entity.Property(value => value.Name).IsRequired();
        entity.Property(value => value.Phone).IsRequired();
        entity.Property(value => value.Address).HasColumnType("jsonb");
        entity.Property(value => value.IsDefault).HasDefaultValue(false);
        // Lưu thời điểm xóa mềm để truy vết. Địa chỉ đã xóa không còn trong sổ và không tính hạn mức.
        entity.Property(value => value.DeletedAt).IsRequired(false);
        // Ẩn địa chỉ đã xóa khỏi danh sách và các thao tác sửa, đặt mặc định.
        entity.HasQueryFilter(value => value.DeletedAt == null);
    }
}
