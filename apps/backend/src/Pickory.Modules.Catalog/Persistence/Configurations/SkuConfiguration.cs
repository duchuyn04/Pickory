using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Catalog.Entities;

namespace Pickory.Modules.Catalog.Persistence.Configurations;

internal sealed class SkuConfiguration : IEntityTypeConfiguration<Sku>
{
    public void Configure(EntityTypeBuilder<Sku> entity)
    {
        // Giữ ràng buộc tồn kho không âm theo `reference-schema.sql`. EF Core chỉ tạo ràng buộc này khi khai báo rõ.
        entity.ToTable("skus", table =>
        {
            table.HasCheckConstraint("skus_stock_non_negative", "\"stock\" >= 0");
        });
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.ProductId).IsRequired();
        entity.Property(value => value.Code).IsRequired();
        entity.Property(value => value.OptionValues).HasColumnType("jsonb");
        entity.Property(value => value.Price).IsRequired();
        entity.Property(value => value.CompareAtPrice).IsRequired(false);
        entity.Property(value => value.Stock).HasDefaultValue(0);
        entity.Property(value => value.Enabled).HasDefaultValue(true);
        entity.Property(value => value.WeightGrams).IsRequired(false);
        entity.Property(value => value.LengthCm).IsRequired(false);
        entity.Property(value => value.WidthCm).IsRequired(false);
        entity.Property(value => value.HeightCm).IsRequired(false);
        entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.Property(value => value.UpdatedAt).IsRequired();

        entity.HasIndex(value => value.Code)
            .IsUnique()
            .HasDatabaseName("skus_code_key");

        entity.HasIndex(value => value.ProductId)
            .HasDatabaseName("skus_productId_idx");

        // Xóa biến thể cùng sản phẩm để không còn biến thể tham chiếu sản phẩm đã xóa.
        entity.HasOne(value => value.Product)
            .WithMany(value => value.Skus)
            .HasForeignKey(value => value.ProductId)
            .HasConstraintName("skus_productId_fkey")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
