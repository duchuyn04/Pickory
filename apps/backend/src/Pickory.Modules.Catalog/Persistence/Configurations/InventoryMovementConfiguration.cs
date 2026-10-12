using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Catalog.Entities;

namespace Pickory.Modules.Catalog.Persistence.Configurations;

internal sealed class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> entity)
    {
        // Cho phép `OrderId` nullable để biến động thủ công không cần đơn hàng.
        entity.ToTable("inventory_movements");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.SkuId).IsRequired();
        entity.Property(value => value.Delta).IsRequired();
        entity.Property(value => value.Type).IsRequired();
        entity.Property(value => value.OrderId).IsRequired(false);
        entity.Property(value => value.ActorId).IsRequired(false);
        entity.Property(value => value.Reason).IsRequired();
        entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

        entity.HasIndex(value => new { value.SkuId, value.CreatedAt })
            .HasDatabaseName("inventory_movements_skuId_createdAt_idx");

        // Index hỗ trợ đọc số dư; khóa theo đơn chặn thao tác lặp mà vẫn cho phép giữ hàng lại.
        entity.HasIndex(value => new { value.OrderId, value.SkuId, value.Type })
            .HasDatabaseName("inventory_movements_orderId_skuId_type_idx");

        // Chặn xóa biến thể đã có biến động để giữ lịch sử tồn kho.
        entity.HasOne(value => value.Sku)
            .WithMany(value => value.Movements)
            .HasForeignKey(value => value.SkuId)
            .HasConstraintName("inventory_movements_skuId_fkey")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
