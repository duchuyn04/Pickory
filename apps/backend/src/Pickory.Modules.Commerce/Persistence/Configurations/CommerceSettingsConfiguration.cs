using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pickory.Modules.Commerce.Entities;

namespace Pickory.Modules.Commerce.Persistence.Configurations;

internal sealed class CommerceSettingsConfiguration : IEntityTypeConfiguration<CommerceSettings>
{
    public void Configure(EntityTypeBuilder<CommerceSettings> entity)
    {
        entity.ToTable("commerce_settings");
        // Bảng chỉ có một dòng nên mã dòng do mã nguồn đặt cố định, không sinh ngẫu nhiên.
        entity.HasKey(value => value.Id);
        entity.Property(value => value.Id).ValueGeneratedNever();
        // Bốn cột số không có mặc định ở DB, vì EF Core thay giá trị 0 bằng mặc định đó.
        // Giá trị mặc định khi chưa có dòng do mã nguồn cung cấp.
        entity.Property(value => value.FreeShippingThreshold).IsRequired();
        entity.Property(value => value.CodMaxAmount).IsRequired();
        entity.Property(value => value.MaxQuantityPerSku).IsRequired();
        entity.Property(value => value.MaxCartLines).IsRequired();
        entity.Property(value => value.SepayAccountNumber).IsRequired(false);
        entity.Property(value => value.SepayBankCode).IsRequired(false);
        entity.Property(value => value.UpdatedAt).IsRequired();
    }
}
