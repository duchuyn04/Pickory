using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pickory.Modules.Commerce.Entities;

namespace Pickory.Modules.Commerce.Persistence.Configurations;

internal sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> entity)
    {
        entity.ToTable("cart_items");
        // Mỗi SKU chỉ có một dòng trong một giỏ. Thêm lại cùng SKU thì cộng số lượng, không thêm dòng.
        entity.HasKey(value => new { value.CartId, value.SkuId });
        entity.Property(value => value.CartId).IsRequired();
        entity.Property(value => value.SkuId).IsRequired();
        entity.Property(value => value.Quantity).IsRequired();

        // Xóa giỏ thì xóa luôn dòng, vì dòng không có nghĩa khi thiếu giỏ. `UserDeletedEvent` dựa vào hành vi này.
        entity.HasOne(value => value.Cart)
            .WithMany(value => value.Items)
            .HasForeignKey(value => value.CartId)
            .HasConstraintName("cart_items_cartId_fkey")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
