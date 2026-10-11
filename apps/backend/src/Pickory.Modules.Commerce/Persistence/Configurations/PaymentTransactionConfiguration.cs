using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Commerce.Entities;

namespace Pickory.Modules.Commerce.Persistence.Configurations;

internal sealed class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> entity)
    {
        entity.ToTable("payment_transactions");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.Provider).IsRequired();
        entity.Property(value => value.ProviderTxId).IsRequired();
        entity.Property(value => value.Amount).IsRequired();
        entity.Property(value => value.Content).IsRequired();
        // Payload gốc của nhà cung cấp, giữ nguyên để đối soát và tra lỗi sau này.
        entity.Property(value => value.Raw).HasColumnType("jsonb");
        entity.Property(value => value.OrderId).IsRequired(false);
        entity.Property(value => value.MatchStatus).IsRequired();
        entity.Property(value => value.ReceivedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

        // Nhà cung cấp gửi lại cùng giao dịch thì DB từ chối dòng thứ hai. Đây là chốt chặn cuối chống ghi nhận tiền hai lần.
        entity.HasIndex(value => value.ProviderTxId)
            .IsUnique()
            .HasDatabaseName("payment_transactions_providerTxId_key");

        // Giao dịch là chứng từ tiền, nên khóa ngoại chặn xóa đơn còn giao dịch gắn vào.
        entity.HasOne(value => value.Order)
            .WithMany()
            .HasForeignKey(value => value.OrderId)
            .HasConstraintName("payment_transactions_orderId_fkey")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
