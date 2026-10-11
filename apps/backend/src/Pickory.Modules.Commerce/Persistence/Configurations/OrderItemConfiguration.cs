using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Commerce.Entities;

namespace Pickory.Modules.Commerce.Persistence.Configurations;

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> entity)
    {
        entity.ToTable("order_items");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.OrderId).IsRequired();
        entity.Property(value => value.SkuId).IsRequired();
        entity.Property(value => value.ProductId).IsRequired();
        entity.Property(value => value.NameSnapshot).IsRequired();
        entity.Property(value => value.SkuCodeSnapshot).IsRequired();
        entity.Property(value => value.OptionValues).HasColumnType("jsonb");
        entity.Property(value => value.ImagePublicId).IsRequired(false);
        entity.Property(value => value.UnitPrice).IsRequired();
        entity.Property(value => value.Quantity).IsRequired();

        // Đơn không bao giờ bị xóa, nên khóa ngoại chặn xóa đơn còn dòng hàng thay vì xóa dây chuyền.
        entity.HasOne(value => value.Order)
            .WithMany(value => value.Items)
            .HasForeignKey(value => value.OrderId)
            .HasConstraintName("order_items_orderId_fkey")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
