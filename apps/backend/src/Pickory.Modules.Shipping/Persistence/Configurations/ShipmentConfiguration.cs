using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Shipping.Contracts.Parcels;
using Pickory.Modules.Shipping.Entities;

namespace Pickory.Modules.Shipping.Persistence.Configurations;

internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    private static readonly JsonSerializerOptions ParcelJsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<Shipment> entity)
    {
        entity.ToTable("shipments");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.Ref).IsRequired();
        // Mặc định `ghn` vì hiện chỉ có một đơn vị vận chuyển. Thêm đơn vị thứ hai cần hỏi người dùng trước.
        entity.Property(value => value.Carrier).HasDefaultValue("ghn");
        entity.Property(value => value.TrackingCode).IsRequired();
        entity.Property(value => value.Fee).IsRequired();
        entity.Property(value => value.CodAmount).IsRequired();
        // Kiện hàng lưu dạng JSON để đổi kích thước thật khi tạo vận đơn mà không cần thêm cột.
        entity.Property(value => value.Parcel)
            .HasColumnType("jsonb")
            .HasConversion(new ValueConverter<Parcel, string>(
                parcel => JsonSerializer.Serialize(parcel, ParcelJsonOptions),
                json => JsonSerializer.Deserialize<Parcel>(json, ParcelJsonOptions)!))
            .IsRequired();
        entity.Property(value => value.Status).IsRequired();
        entity.Property(value => value.RawStatus).IsRequired();
        entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.Property(value => value.UpdatedAt).IsRequired();

        // Mỗi `ref` chỉ có một vận đơn. Vận đơn đã hủy đổi `ref` để giữ ràng buộc này.
        entity.HasIndex(value => value.Ref)
            .IsUnique()
            .HasDatabaseName("shipments_ref_key");

        // Webhook tra vận đơn theo mã vận đơn của GHN, và hai vận đơn không được chung mã.
        entity.HasIndex(value => value.TrackingCode)
            .IsUnique()
            .HasDatabaseName("shipments_trackingCode_key");
    }
}
