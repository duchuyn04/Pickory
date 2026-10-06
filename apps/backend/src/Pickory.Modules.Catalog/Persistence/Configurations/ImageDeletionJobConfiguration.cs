using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Catalog.Entities;

namespace Pickory.Modules.Catalog.Persistence.Configurations;

internal sealed class ImageDeletionJobConfiguration : IEntityTypeConfiguration<ImageDeletionJob>
{
    public void Configure(EntityTypeBuilder<ImageDeletionJob> entity)
    {
        // Giữ mặc định retry của DB để EF Core không đổi cấu trúc hàng đợi hiện có.
        entity.ToTable("image_deletion_jobs");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.PublicId).IsRequired();
        entity.Property(value => value.Status).HasDefaultValue("pending");
        entity.Property(value => value.Attempts).HasDefaultValue(0);
        entity.Property(value => value.MaxAttempts).HasDefaultValue(8);
        entity.Property(value => value.NextRetryAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.Property(value => value.LastError).IsRequired(false);
        entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.Property(value => value.UpdatedAt).IsRequired();

        entity.HasIndex(value => new { value.Status, value.NextRetryAt })
            .HasDatabaseName("image_deletion_jobs_status_nextRetryAt_idx");
    }
}
