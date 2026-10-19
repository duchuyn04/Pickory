using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;

namespace Pickory.Modules.Commerce.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> entity)
    {
        entity.ToTable("orders");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.Code).IsRequired();
        entity.Property(value => value.UserId).IsRequired(false);
        entity.Property(value => value.AnonymousId).IsRequired();
        // Liên hệ, địa chỉ và hóa đơn là bản chụp tại thời điểm đặt, nên lưu `jsonb` thay vì nối bảng.
        entity.Property(value => value.Contact).HasColumnType("jsonb");
        entity.Property(value => value.ShippingAddress).HasColumnType("jsonb");
        entity.Property(value => value.InvoiceInfo).HasColumnType("jsonb").IsRequired(false);
        entity.Property(value => value.PaymentMethod).IsRequired();
        entity.Property(value => value.Status).IsRequired();
        entity.Property(value => value.PaymentStatus).IsRequired();
        entity.Property(value => value.Flags).HasColumnType("text[]").HasDefaultValueSql("ARRAY[]::text[]");
        entity.Property(value => value.Subtotal).IsRequired();
        entity.Property(value => value.ShippingFee).IsRequired();
        entity.Property(value => value.ShippingDiscount).IsRequired();
        entity.Property(value => value.Total).IsRequired();
        // Giá trị mặc định `ghn` làm đơn đặt trước khi có cột nhận đúng nguồn GHN, vì trước đó chỉ có phí GHN.
        entity.Property(value => value.ShippingFeeSource).IsRequired().HasDefaultValue(ShippingFeeSource.Ghn);
        entity.Property(value => value.PaymentDeadline).IsRequired(false);
        // Mã giao dịch hoàn tiền dài tối đa 100 ký tự, bằng giới hạn mà route hoàn tiền kiểm.
        entity.Property(value => value.RefundTransactionRef).HasMaxLength(100).IsRequired(false);
        entity.Property(value => value.AccessTokenHash).IsRequired();
        entity.Property(value => value.IdempotencyKey).IsRequired();
        entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

        // Mã đơn là nội dung chuyển khoản và `Ref` của vận đơn, nên hai đơn không được chung mã.
        entity.HasIndex(value => value.Code)
            .IsUnique()
            .HasDatabaseName("orders_code_key");

        // Gửi lại cùng `Idempotency-Key` không được tạo đơn thứ hai. DB là chốt chặn cuối khi hai request chạy đồng thời.
        entity.HasIndex(value => value.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("orders_idempotencyKey_key");

        // Danh sách đơn của nhân viên lọc theo trạng thái và sắp theo ngày tạo.
        entity.HasIndex(value => new { value.Status, value.CreatedAt })
            .HasDatabaseName("orders_status_createdAt_idx");
    }
}
