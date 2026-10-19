using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pickory.Modules.Shipping.Entities;

namespace Pickory.Modules.Shipping.Persistence.Configurations;

internal sealed class ShippingQuoteCacheConfiguration : IEntityTypeConfiguration<ShippingQuoteCache>
{
    public void Configure(EntityTypeBuilder<ShippingQuoteCache> entity)
    {
        entity.ToTable("shipping_quote_cache");
        // Khóa chính là cả sáu thành phần của khóa phí. Lệnh lưu dùng khóa này làm đích của `ON CONFLICT`, nên không cần chỉ mục duy nhất riêng.
        entity.HasKey(value => new
        {
            value.ReceiverWardCode,
            value.PickupWardCode,
            value.ServiceTypeId,
            value.ChargeableWeightGrams,
            value.InsuredValue,
            value.CodAmount,
        });
        entity.Property(value => value.ReceiverWardCode).IsRequired();
        entity.Property(value => value.PickupWardCode).IsRequired();
        entity.Property(value => value.Fee).IsRequired();
        entity.Property(value => value.SavedAt).IsRequired();

        // Lệnh dọn dòng quá hạn lọc theo `savedAt`. Chỉ mục này giúp lệnh xóa không quét cả bảng.
        entity.HasIndex(value => value.SavedAt)
            .HasDatabaseName("shipping_quote_cache_savedAt_idx");
    }
}
