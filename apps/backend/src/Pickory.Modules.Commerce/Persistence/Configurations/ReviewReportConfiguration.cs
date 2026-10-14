using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Commerce.Entities;

namespace Pickory.Modules.Commerce.Persistence.Configurations;

internal sealed class ReviewReportConfiguration : IEntityTypeConfiguration<ReviewReport>
{
    public void Configure(EntityTypeBuilder<ReviewReport> entity)
    {
        entity.ToTable("review_reports");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.ReviewId).IsRequired();
        // `UserId` trỏ tới identity bằng giá trị, không có khóa ngoại giữa hai module.
        entity.Property(value => value.UserId).IsRequired();
        entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

        // Mỗi khách báo cáo một đánh giá tối đa một lần. Khóa duy nhất chặn cả hai request đồng thời.
        entity.HasIndex(value => new { value.ReviewId, value.UserId })
            .IsUnique()
            .HasDatabaseName("review_reports_reviewId_userId_key");

        // Báo cáo vô nghĩa khi đánh giá không còn, nên xóa đánh giá thì xóa theo báo cáo.
        entity.HasOne(value => value.Review)
            .WithMany()
            .HasForeignKey(value => value.ReviewId)
            .HasConstraintName("review_reports_reviewId_fkey")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
