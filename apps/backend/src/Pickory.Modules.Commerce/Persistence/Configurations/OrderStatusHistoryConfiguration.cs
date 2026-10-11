using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Commerce.Entities;

namespace Pickory.Modules.Commerce.Persistence.Configurations;

internal sealed class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> entity)
    {
        entity.ToTable("order_status_history");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.OrderId).IsRequired();
        entity.Property(value => value.FromStatus).IsRequired();
        entity.Property(value => value.ToStatus).IsRequired();
        entity.Property(value => value.ActorId).IsRequired(false);
        entity.Property(value => value.Reason).IsRequired(false);
        entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

        // Lịch sử là bằng chứng kế toán, nên khóa ngoại chặn xóa đơn còn lịch sử.
        entity.HasOne(value => value.Order)
            .WithMany(value => value.StatusHistory)
            .HasForeignKey(value => value.OrderId)
            .HasConstraintName("order_status_history_orderId_fkey")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
