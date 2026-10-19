using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pickory.Modules.Shipping.Entities;

namespace Pickory.Modules.Shipping.Persistence.Configurations;

internal sealed class ShippingFallbackFeeConfiguration : IEntityTypeConfiguration<ShippingFallbackFee>
{
    public void Configure(EntityTypeBuilder<ShippingFallbackFee> entity)
    {
        entity.ToTable("shipping_fallback_fee");
        // Bảng chỉ có một dòng nên mã dòng do mã nguồn đặt cố định, giống `shipping_settings`.
        entity.HasKey(value => value.Id);
        entity.Property(value => value.Id).ValueGeneratedNever();
        entity.Property(value => value.Enabled).IsRequired();
        // Chín cột giá có thể để trống, vì admin được lưu bảng chưa đủ khi đang tắt. API kiểm luật "bật thì đủ 9 giá trị".
        entity.Property(value => value.SameProvinceBaseFee).IsRequired(false);
        entity.Property(value => value.SameProvinceBaseWeightGrams).IsRequired(false);
        entity.Property(value => value.SameProvinceStepFee).IsRequired(false);
        entity.Property(value => value.SameRegionBaseFee).IsRequired(false);
        entity.Property(value => value.SameRegionBaseWeightGrams).IsRequired(false);
        entity.Property(value => value.SameRegionStepFee).IsRequired(false);
        entity.Property(value => value.CrossRegionBaseFee).IsRequired(false);
        entity.Property(value => value.CrossRegionBaseWeightGrams).IsRequired(false);
        entity.Property(value => value.CrossRegionStepFee).IsRequired(false);
        entity.Property(value => value.UpdatedAt).IsRequired();
    }
}
