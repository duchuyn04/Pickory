using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Identity.Entities;

namespace Pickory.Modules.Identity.Persistence.Configurations;

internal sealed class ConsentRecordConfiguration : IEntityTypeConfiguration<ConsentRecord>
{
    public void Configure(EntityTypeBuilder<ConsentRecord> entity)
    {
        // Cho phép khách chưa có tài khoản ghi đồng thuận bằng `AnonymousId`.
        entity.ToTable("consent_records");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.AnonymousId).IsRequired();
        entity.Property(value => value.UserId).IsRequired(false);
        entity.Property(value => value.Personalization).HasDefaultValue(false);
        entity.Property(value => value.Marketing).HasDefaultValue(false);
        entity.Property(value => value.PolicyVersion).IsRequired();
        entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.HasIndex(value => value.AnonymousId).HasDatabaseName("consent_records_anonymousId_idx");
        entity.HasIndex(value => value.UserId).HasDatabaseName("consent_records_userId_idx");
        // Giữ đồng thuận khi tài khoản bị xóa. DB chỉ bỏ liên kết tài khoản.
        entity.HasOne(value => value.User)
            .WithMany(user => user.ConsentRecords)
            .HasForeignKey(value => value.UserId)
            .HasConstraintName("consent_records_userId_fkey")
            .OnDelete(DeleteBehavior.SetNull);
    }
}
