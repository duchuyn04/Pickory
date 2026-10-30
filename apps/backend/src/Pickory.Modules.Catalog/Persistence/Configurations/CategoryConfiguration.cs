using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Catalog.Entities;

namespace Pickory.Modules.Catalog.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> entity)
    {
        // Giữ `ParentId` nullable để danh mục gốc không cần tham chiếu danh mục khác.
        entity.ToTable("categories");
        entity.HasKey(value => value.Id);
        // Sinh UUID (Universally Unique Identifier) khi thêm bản ghi để tránh cấp phát lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.ParentId).IsRequired(false);
        entity.Property(value => value.Name).IsRequired();
        entity.Property(value => value.Slug).IsRequired();
        entity.Property(value => value.Position).HasDefaultValue(0);
        entity.Property(value => value.Attributes).HasColumnType("jsonb");
        entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.Property(value => value.UpdatedAt).IsRequired();
        // Lưu thời điểm xóa mềm để truy vết. Dòng đã xóa vẫn giữ cây và thuộc tính cho dữ liệu cũ.
        entity.Property(value => value.DeletedAt).IsRequired(false);
        // Ẩn danh mục đã xóa khỏi cây công khai và danh sách quản trị.
        entity.HasQueryFilter(value => value.DeletedAt == null);

        entity.HasIndex(value => value.Slug)
            .IsUnique()
            .HasDatabaseName("categories_slug_key")
            // Chỉ chặn trùng slug giữa các danh mục còn sống. Dòng đã xóa nhường slug cho bản mới.
            .HasFilter("\"deletedAt\" IS NULL");

        // Chặn xóa danh mục đang có con để cây danh mục không mất liên kết.
        entity.HasOne(value => value.Parent)
            .WithMany(value => value.Children)
            .HasForeignKey(value => value.ParentId)
            .HasConstraintName("categories_parentId_fkey")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
