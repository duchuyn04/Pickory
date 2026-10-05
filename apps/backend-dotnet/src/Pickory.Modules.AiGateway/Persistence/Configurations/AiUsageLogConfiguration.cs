using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.AiGateway.Entities;

namespace Pickory.Modules.AiGateway.Persistence.Configurations;

internal sealed class AiUsageLogConfiguration : IEntityTypeConfiguration<AiUsageLog>
{
    public void Configure(EntityTypeBuilder<AiUsageLog> entity)
    {
        // Giữ kiểu số thực theo `reference-schema.sql` để chi phí không bị đổi sang số nguyên hoặc số thập phân.
        entity.ToTable("ai_usage_logs");
        entity.HasKey(value => value.Id);
        // Sinh UUID (Universally Unique Identifier) khi thêm bản ghi để tránh cấp phát lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.InputTokens).HasDefaultValue(0);
        entity.Property(value => value.OutputTokens).HasDefaultValue(0);
        entity.Property(value => value.CachedTokens).HasDefaultValue(0);
        entity.Property(value => value.CostUsd).HasColumnType("double precision").HasDefaultValue(0d);
        entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.HasIndex(value => value.CreatedAt).HasDatabaseName("ai_usage_logs_createdAt_idx");
        entity.HasIndex(value => value.Feature).HasDatabaseName("ai_usage_logs_feature_idx");
        entity.HasIndex(value => value.Status).HasDatabaseName("ai_usage_logs_status_idx");
        entity.HasIndex(value => value.ActorKeyHash).HasDatabaseName("ai_usage_logs_actorKeyHash_idx");
    }
}
