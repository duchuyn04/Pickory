using Microsoft.EntityFrameworkCore;
using Pickory.Shared.Persistence;

namespace Pickory.Modules.AiGateway.Persistence;

public sealed class AiGatewayDbContext(DbContextOptions<AiGatewayDbContext> options) : DbContext(options)
{
    public DbSet<AiUsageLog> UsageLogs => Set<AiUsageLog>();
    public DbSet<AiSetting> Settings => Set<AiSetting>();
    public DbSet<AiEmbeddingCache> EmbeddingCache => Set<AiEmbeddingCache>();

    public static void ConfigureOptions(DbContextOptionsBuilder options, string connectionString)
    {
        // Tách lịch sử của module khỏi schema ứng dụng để Prisma không bị thay đổi.
        options.UseNpgsql(connectionString, postgres =>
            postgres.MigrationsHistoryTable("__EFMigrationsHistory", "ef_ai_gateway"));
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Giữ UTC (Coordinated Universal Time) để các module không áp múi giờ máy chủ khi đọc DB.
        PrismaModelConventions.Configure(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Giữ kiểu số thực của Node để chi phí không bị đổi sang số nguyên hoặc số thập phân.
        modelBuilder.Entity<AiUsageLog>(entity =>
        {
            entity.ToTable("ai_usage_logs");
            entity.HasKey(value => value.Id);
            // Sinh UUID (Universally Unique Identifier) khi thêm bản ghi để tránh cấp phát lúc đọc dữ liệu đã có.
            entity.Property(value => value.Id).ValueGeneratedOnAdd()
                .HasValueGenerator<Microsoft.EntityFrameworkCore.ValueGeneration.StringValueGenerator>();
            entity.Property(value => value.InputTokens).HasDefaultValue(0);
            entity.Property(value => value.OutputTokens).HasDefaultValue(0);
            entity.Property(value => value.CachedTokens).HasDefaultValue(0);
            entity.Property(value => value.CostUsd).HasColumnType("double precision").HasDefaultValue(0d);
            entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(value => value.CreatedAt).HasDatabaseName("ai_usage_logs_createdAt_idx");
            entity.HasIndex(value => value.Feature).HasDatabaseName("ai_usage_logs_feature_idx");
            entity.HasIndex(value => value.Status).HasDatabaseName("ai_usage_logs_status_idx");
            entity.HasIndex(value => value.ActorKeyHash).HasDatabaseName("ai_usage_logs_actorKeyHash_idx");
        });

        // Giữ khóa mặc định của cấu hình. `UpdatedAt` do ứng dụng cung cấp, không do DB sinh.
        modelBuilder.Entity<AiSetting>(entity =>
        {
            entity.ToTable("ai_settings");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasDefaultValue("singleton");
            entity.Property(value => value.MonthlyBudgetUsd).HasColumnType("double precision");
            entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        // DB hiện lưu mảng số thực. Extension `vector` không làm thay đổi kiểu của cột này.
        modelBuilder.Entity<AiEmbeddingCache>(entity =>
        {
            entity.ToTable("ai_embedding_cache");
            entity.HasKey(value => value.Id);
            // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
            entity.Property(value => value.Id).ValueGeneratedOnAdd()
                .HasValueGenerator<Microsoft.EntityFrameworkCore.ValueGeneration.StringValueGenerator>();
            entity.Property(value => value.Embedding).HasColumnType("double precision[]").IsRequired(false);
            entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(value => value.Key).IsUnique().HasDatabaseName("ai_embedding_cache_key_key");
            entity.HasIndex(value => value.Model).HasDatabaseName("ai_embedding_cache_model_idx");
        });

        // Áp dụng tên cột sau cấu hình bảng để tên khóa chính lấy đúng tên bảng hiện có.
        PrismaModelConventions.Apply(modelBuilder);
    }
}
