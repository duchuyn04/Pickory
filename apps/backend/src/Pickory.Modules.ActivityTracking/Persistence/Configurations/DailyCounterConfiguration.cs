using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pickory.Modules.ActivityTracking.Entities;

namespace Pickory.Modules.ActivityTracking.Persistence.Configurations;

internal sealed class DailyCounterConfiguration : IEntityTypeConfiguration<DailyCounter>
{
    public void Configure(EntityTypeBuilder<DailyCounter> entity)
    {
        entity.ToTable("daily_counters");
        // Khóa chính ghép theo ngày rồi chỉ số. Mỗi chỉ số có đúng một dòng mỗi ngày, và đọc theo khoảng ngày dùng được khóa này.
        entity.HasKey(counter => new { counter.Date, counter.Metric });
        // Kiểu `date` không có giờ, nên ngày không lệch theo múi giờ của máy chủ.
        entity.Property(counter => counter.Date).HasColumnType("date").IsRequired();
        entity.Property(counter => counter.Metric).IsRequired();
        // Kiểu `bigint` để bộ đếm chỉ tăng không tràn số nguyên 32 bit.
        entity.Property(counter => counter.Value).HasColumnType("bigint").IsRequired();
    }
}
