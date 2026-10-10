using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Shipping.Entities;

namespace Pickory.Modules.Shipping.Persistence.Configurations;

internal sealed class ShipmentEventConfiguration : IEntityTypeConfiguration<ShipmentEvent>
{
    public void Configure(EntityTypeBuilder<ShipmentEvent> entity)
    {
        entity.ToTable("shipment_events");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.ShipmentId).IsRequired();
        entity.Property(value => value.Status).IsRequired();
        entity.Property(value => value.RawStatus).IsRequired();
        entity.Property(value => value.OccurredAt).IsRequired();
        entity.Property(value => value.Source).IsRequired();

        // Webhook và job đồng bộ đọc lịch sử theo vận đơn. Index này thay cho index khóa ngoại mà quy ước đã tắt.
        entity.HasIndex(value => value.ShipmentId)
            .HasDatabaseName("shipment_events_shipmentId_idx");

        // Chặn xóa vận đơn đã có lịch sử. Lịch sử trạng thái là bằng chứng đối soát với đơn vị vận chuyển.
        entity.HasOne(value => value.Shipment)
            .WithMany(value => value.Events)
            .HasForeignKey(value => value.ShipmentId)
            .HasConstraintName("shipment_events_shipmentId_fkey")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
