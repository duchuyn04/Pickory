using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pickory.Modules.Shipping.Entities;

namespace Pickory.Modules.Shipping.Persistence.Configurations;

internal sealed class AddressProvinceConfiguration : IEntityTypeConfiguration<AddressProvince>
{
    public void Configure(EntityTypeBuilder<AddressProvince> entity)
    {
        entity.ToTable("address_provinces");
        // Khóa chính là `_id` của GHN nên mã do job đồng bộ cấp, không sinh ở DB.
        entity.HasKey(value => value.Code);
        entity.Property(value => value.Code).ValueGeneratedNever();
        entity.Property(value => value.Name).IsRequired();
        // Mặc định đang dùng. Job đồng bộ tắt dòng mà GHN không còn trả, thay vì xóa, để địa chỉ cũ vẫn tra được.
        entity.Property(value => value.IsActive).HasDefaultValue(true);
        entity.Property(value => value.SyncedAt).IsRequired();
    }
}
