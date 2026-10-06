using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Catalog.Entities;

namespace Pickory.Modules.Catalog.Persistence.Configurations;

internal sealed class ProductSlugHistoryConfiguration : IEntityTypeConfiguration<ProductSlugHistory>
{
    public void Configure(EntityTypeBuilder<ProductSlugHistory> entity)
    {
        // Giữ đường dẫn cũ duy nhất để một đường dẫn không chuyển hướng đến hai sản phẩm.
        entity.ToTable("product_slug_history");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.OldSlug).IsRequired();
        entity.Property(value => value.ProductId).IsRequired();
        entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

        entity.HasIndex(value => value.OldSlug)
            .IsUnique()
            .HasDatabaseName("product_slug_history_oldSlug_key");

        entity.HasIndex(value => value.ProductId)
            .HasDatabaseName("product_slug_history_productId_idx");

        // Xóa lịch sử cùng sản phẩm để không còn chuyển hướng đến sản phẩm đã xóa.
        entity.HasOne(value => value.Product)
            .WithMany(value => value.SlugHistory)
            .HasForeignKey(value => value.ProductId)
            .HasConstraintName("product_slug_history_productId_fkey")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
