using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pickory.Modules.AiGateway.Entities;
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
        // Cột `ai_settings.id` giữ mặc định `'singleton'` của schema Prisma, vì bỏ mặc định sẽ đổi DB thật.
        // Ứng dụng luôn ghi id tường minh, nên cảnh báo về mặc định của khóa không áp dụng cho bảng này.
        options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.ModelValidationKeyDefaultValueWarning));
    }

    // Dùng data source chung của API, nên mọi module chia nhau một pool kết nối. Module này không có enum.
    public static void ConfigureOptions(DbContextOptionsBuilder options, NpgsqlDataSource dataSource)
    {
        options.UseNpgsql(dataSource, postgres =>
            postgres.MigrationsHistoryTable("__EFMigrationsHistory", "ef_ai_gateway"));
        // Cột `ai_settings.id` giữ mặc định `'singleton'` của schema Prisma, vì bỏ mặc định sẽ đổi DB thật.
        // Ứng dụng luôn ghi id tường minh, nên cảnh báo về mặc định của khóa không áp dụng cho bảng này.
        options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.ModelValidationKeyDefaultValueWarning));
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Giữ UTC (Coordinated Universal Time) để các module không áp múi giờ máy chủ khi đọc DB.
        DatabaseNamingConventions.Configure(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Ghi extension vào model để migration sau mốc không coi `vector` là thành phần chưa được tạo.
        modelBuilder.HasPostgresExtension(schema: "public", name: "vector");

        // Mỗi bảng có lớp cấu hình riêng trong `Configurations/`. Chỉ quét assembly của module để không nhận bảng module khác.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AiGatewayDbContext).Assembly);

        // Áp dụng tên cột sau cấu hình bảng để tên khóa chính lấy đúng tên bảng hiện có.
        DatabaseNamingConventions.Apply(modelBuilder);
    }
}
