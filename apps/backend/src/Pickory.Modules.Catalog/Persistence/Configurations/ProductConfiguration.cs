using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Catalog.Entities;

namespace Pickory.Modules.Catalog.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> entity)
    {
        // Giữ `jsonb` cho nội dung có cấu trúc để EF Core đọc đúng dữ liệu đã có.
        entity.ToTable("products");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.CategoryId).IsRequired();
        entity.Property(value => value.BrandId).IsRequired();
        entity.Property(value => value.Name).IsRequired();
        entity.Property(value => value.Slug).IsRequired();
        entity.Property(value => value.ShortDesc).IsRequired();
        entity.Property(value => value.Description).HasColumnType("jsonb");
        entity.Property(value => value.Specs).HasColumnType("jsonb");
        entity.Property(value => value.Options).HasColumnType("jsonb");
        entity.Property(value => value.WarrantyMonths).HasDefaultValue(12);
        entity.Property(value => value.Status).HasDefaultValue(ProductStatus.Draft);
        entity.Property(value => value.MinPrice).IsRequired(false);
        entity.Property(value => value.InStock).HasDefaultValue(false);
        entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.Property(value => value.UpdatedAt).IsRequired();
        entity.Property(value => value.DeletedAt).IsRequired(false);

        entity.HasIndex(value => value.Slug)
            .IsUnique()
            .HasDatabaseName("products_slug_key");

        entity.HasIndex(value => new { value.CategoryId, value.Status })
            .HasDatabaseName("products_categoryId_status_idx");

        // Giữ phương thức GIN (Generalized Inverted Index) để tìm thuộc tính JSON theo index hiện có.
        entity.HasIndex(value => value.Specs)
            .HasMethod("gin")
            .HasDatabaseName("products_specs_gin_idx");

        entity.HasIndex(value => value.MinPrice)
            .HasDatabaseName("products_minPrice_idx");

        entity.HasIndex(value => value.CreatedAt)
            .HasDatabaseName("products_createdAt_idx");

        // Chặn xóa danh mục đang có sản phẩm để sản phẩm không mất danh mục.
        entity.HasOne(value => value.Category)
            .WithMany(value => value.Products)
            .HasForeignKey(value => value.CategoryId)
            .HasConstraintName("products_categoryId_fkey")
            .OnDelete(DeleteBehavior.Restrict);

        // Chặn xóa thương hiệu đang có sản phẩm để sản phẩm không mất thương hiệu.
        entity.HasOne(value => value.Brand)
            .WithMany(value => value.Products)
            .HasForeignKey(value => value.BrandId)
            .HasConstraintName("products_brandId_fkey")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
