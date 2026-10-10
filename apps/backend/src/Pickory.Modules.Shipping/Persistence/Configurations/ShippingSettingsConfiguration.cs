using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pickory.Modules.Shipping.Entities;

namespace Pickory.Modules.Shipping.Persistence.Configurations;

internal sealed class ShippingSettingsConfiguration : IEntityTypeConfiguration<ShippingSettings>
{
    public void Configure(EntityTypeBuilder<ShippingSettings> entity)
    {
        entity.ToTable("shipping_settings");
        // Bảng chỉ có một dòng nên mã dòng do mã nguồn đặt cố định, không sinh ngẫu nhiên.
        entity.HasKey(value => value.Id);
        entity.Property(value => value.Id).ValueGeneratedNever();
        entity.Property(value => value.SenderName).IsRequired();
        entity.Property(value => value.SenderPhone).IsRequired();
        entity.Property(value => value.ProvinceCode).IsRequired();
        entity.Property(value => value.ProvinceName).IsRequired();
        entity.Property(value => value.WardCode).IsRequired();
        entity.Property(value => value.WardName).IsRequired();
        entity.Property(value => value.Line).IsRequired();
        // Cột giữ `service_type_id` của GHN (2 hoặc 5). Để trống nghĩa là chưa chọn, và khi báo phí module dùng loại 2.
        entity.Property(value => value.DefaultServiceTypeId).IsRequired(false);
        entity.Property(value => value.UpdatedAt).IsRequired();
    }
}
