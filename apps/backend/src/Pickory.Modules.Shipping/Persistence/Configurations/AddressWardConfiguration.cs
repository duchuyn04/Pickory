using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pickory.Modules.Shipping.Entities;

namespace Pickory.Modules.Shipping.Persistence.Configurations;

internal sealed class AddressWardConfiguration : IEntityTypeConfiguration<AddressWard>
{
    public void Configure(EntityTypeBuilder<AddressWard> entity)
    {
        entity.ToTable("address_wards");
        // Khóa chính là `_id` của GHN nên mã do job đồng bộ cấp, không sinh ở DB.
        entity.HasKey(value => value.Code);
        entity.Property(value => value.Code).ValueGeneratedNever();
        entity.Property(value => value.ProvinceCode).IsRequired();
        entity.Property(value => value.Name).IsRequired();
        entity.Property(value => value.IsActive).HasDefaultValue(true);
        entity.Property(value => value.SyncedAt).IsRequired();

        // Danh sách phường xã luôn lọc theo tỉnh. Index này thay cho index khóa ngoại mà quy ước đã tắt.
        entity.HasIndex(value => value.ProvinceCode)
            .HasDatabaseName("address_wards_provinceCode_idx");

        // Chặn xóa tỉnh còn phường xã, để danh mục không mồ côi.
        entity.HasOne(value => value.Province)
            .WithMany(value => value.Wards)
            .HasForeignKey(value => value.ProvinceCode)
            .HasConstraintName("address_wards_provinceCode_fkey")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
