using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Catalog.Entities;

namespace Pickory.Modules.Catalog.Persistence.Configurations;

internal sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> entity)
    {
        // Giữ `SkuId` nullable để ảnh có thể thuộc sản phẩm mà không thuộc biến thể.
        entity.ToTable("product_images");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.ProductId).IsRequired();
        entity.Property(value => value.SkuId).IsRequired(false);
        entity.Property(value => value.PublicId).IsRequired();
        entity.Property(value => value.Alt).IsRequired();
        entity.Property(value => value.Position).HasDefaultValue(0);
        entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

        entity.HasIndex(value => new { value.ProductId, value.Position })
            .HasDatabaseName("product_images_productId_position_idx");

        // Xóa ảnh cùng sản phẩm để không còn ảnh tham chiếu sản phẩm đã xóa.
        entity.HasOne(value => value.Product)
            .WithMany(value => value.Images)
            .HasForeignKey(value => value.ProductId)
            .HasConstraintName("product_images_productId_fkey")
            .OnDelete(DeleteBehavior.Cascade);

        // Giữ ảnh khi biến thể bị xóa. DB chỉ bỏ liên kết biến thể.
        entity.HasOne(value => value.Sku)
            .WithMany(value => value.Images)
            .HasForeignKey(value => value.SkuId)
            .HasConstraintName("product_images_skuId_fkey")
            .OnDelete(DeleteBehavior.SetNull);
    }
}
