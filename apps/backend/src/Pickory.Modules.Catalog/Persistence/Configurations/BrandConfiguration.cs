using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Catalog.Entities;

namespace Pickory.Modules.Catalog.Persistence.Configurations;

internal sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> entity)
    {
        // Giữ logo nullable để thương hiệu chưa có ảnh vẫn đọc được.
        entity.ToTable("brands");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.Name).IsRequired();
        entity.Property(value => value.Slug).IsRequired();
        entity.Property(value => value.LogoPublicId).IsRequired(false);
        entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.Property(value => value.UpdatedAt).IsRequired();
        // Lưu thời điểm xóa mềm để truy vết. Dòng đã xóa vẫn giữ logo và tên cho nhật ký cũ.
        entity.Property(value => value.DeletedAt).IsRequired(false);
        // Ẩn thương hiệu đã xóa khỏi mọi truy vấn thường. Handler xóa đặt `DeletedAt` thay vì xóa dòng.
        entity.HasQueryFilter(value => value.DeletedAt == null);

        entity.HasIndex(value => value.Slug)
            .IsUnique()
            .HasDatabaseName("brands_slug_key")
            // Chỉ chặn trùng slug giữa các thương hiệu còn sống. Dòng đã xóa nhường slug cho bản mới.
            .HasFilter("\"deletedAt\" IS NULL");
    }
}
