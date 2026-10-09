using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.Identity.Entities;

namespace Pickory.Modules.Identity.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> entity)
    {
        // Giữ `jsonb` nullable để nhật ký có thể thiếu trạng thái trước hoặc sau.
        entity.ToTable("audit_logs");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.ActorId).IsRequired();
        entity.Property(value => value.Action).IsRequired();
        entity.Property(value => value.TargetType).IsRequired();
        entity.Property(value => value.TargetId).IsRequired();
        entity.Property(value => value.Before).HasColumnType("jsonb").IsRequired(false);
        entity.Property(value => value.After).HasColumnType("jsonb").IsRequired(false);
        entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.HasIndex(value => value.ActorId).HasDatabaseName("audit_logs_actorId_idx");
        entity.HasIndex(value => value.Action).HasDatabaseName("audit_logs_action_idx");
        entity.HasIndex(value => value.CreatedAt).HasDatabaseName("audit_logs_createdAt_idx");
    }
}
