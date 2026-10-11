using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Commerce.Entities;

namespace Pickory.Modules.Commerce.Persistence.Configurations;

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> entity)
    {
        entity.ToTable("reviews");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.ProductId).IsRequired();
        // Cột nullable để ẩn danh hóa được đánh giá khi người viết xóa tài khoản.
        entity.Property(value => value.UserId).IsRequired(false);
        entity.Property(value => value.OrderId).IsRequired();
        entity.Property(value => value.Rating).IsRequired();
        entity.Property(value => value.Body).IsRequired(false);
        entity.Property(value => value.HiddenAt).IsRequired(false);
        entity.Property(value => value.HiddenReason).IsRequired(false);
        entity.Property(value => value.ReportCount).HasDefaultValue(0);
        entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.Property(value => value.UpdatedAt).IsRequired();

        // Mỗi người chỉ có một đánh giá cho mỗi sản phẩm. PostgreSQL cho phép nhiều dòng `NULL`, nên đánh giá đã ẩn danh không bị chặn.
        entity.HasIndex(value => new { value.ProductId, value.UserId })
            .IsUnique()
            .HasDatabaseName("reviews_productId_userId_key");

        // Đánh giá gắn với đơn đã giao. Đơn không bị xóa, nên khóa ngoại chặn xóa đơn còn đánh giá.
        entity.HasOne(value => value.Order)
            .WithMany()
            .HasForeignKey(value => value.OrderId)
            .HasConstraintName("reviews_orderId_fkey")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
