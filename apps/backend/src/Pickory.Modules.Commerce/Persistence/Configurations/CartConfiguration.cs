using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Commerce.Entities;

namespace Pickory.Modules.Commerce.Persistence.Configurations;

internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> entity)
    {
        entity.ToTable("carts");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.AnonymousId).IsRequired(false);
        entity.Property(value => value.UserId).IsRequired(false);
        entity.Property(value => value.UpdatedAt).IsRequired();

        // Mỗi tài khoản có tối đa một giỏ. PostgreSQL cho phép nhiều dòng `NULL`, nên giỏ vãng lai không bị chặn.
        entity.HasIndex(value => value.UserId)
            .IsUnique()
            .HasDatabaseName("carts_userId_key");

        // Mỗi mã ẩn danh có tối đa một giỏ. Hai request đầu tiên chạy đồng thời không tạo được hai giỏ.
        entity.HasIndex(value => value.AnonymousId)
            .IsUnique()
            .HasDatabaseName("carts_anonymousId_key");
    }
}
