using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Pickory.Modules.AiGateway.Entities;

namespace Pickory.Modules.AiGateway.Persistence.Configurations;

internal sealed class AiEmbeddingCacheConfiguration : IEntityTypeConfiguration<AiEmbeddingCache>
{
    public void Configure(EntityTypeBuilder<AiEmbeddingCache> entity)
    {
        // DB hiện lưu mảng số thực. Extension `vector` không làm thay đổi kiểu của cột này.
        entity.ToTable("ai_embedding_cache");
        entity.HasKey(value => value.Id);
        // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
        entity.Property(value => value.Id).ValueGeneratedOnAdd()
            .HasValueGenerator<StringValueGenerator>();
        entity.Property(value => value.Embedding).HasColumnType("double precision[]").IsRequired(false);
        entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.HasIndex(value => value.Key).IsUnique().HasDatabaseName("ai_embedding_cache_key_key");
        entity.HasIndex(value => value.Model).HasDatabaseName("ai_embedding_cache_model_idx");
    }
}
