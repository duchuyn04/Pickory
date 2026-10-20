using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pickory.Modules.ActivityTracking.Entities;
using Pickory.Shared.Persistence;

namespace Pickory.Modules.ActivityTracking.Persistence;

internal sealed class ActivityTrackingDbContext(DbContextOptions<ActivityTrackingDbContext> options) : DbContext(options)
{
    public DbSet<DailyCounter> DailyCounters => Set<DailyCounter>();

    public static void ConfigureOptions(DbContextOptionsBuilder options, string connectionString)
    {
        // Lịch sử migration nằm ở schema riêng, tách khỏi các module khác.
        options.UseNpgsql(connectionString, postgres =>
            postgres.MigrationsHistoryTable("__EFMigrationsHistory", "ef_activity_tracking"));
    }

    // Dùng data source chung của API, nên mọi module chia nhau một pool kết nối.
    public static void ConfigureOptions(DbContextOptionsBuilder options, NpgsqlDataSource dataSource)
    {
        options.UseNpgsql(dataSource, postgres =>
            postgres.MigrationsHistoryTable("__EFMigrationsHistory", "ef_activity_tracking"));
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Dùng chung quy ước của các module để kiểu thời gian và tên khóa không khác nhau giữa các bảng.
        DatabaseNamingConventions.Configure(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Chỉ quét assembly của module để không nhận cấu hình bảng của module khác.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ActivityTrackingDbContext).Assembly);

        // Áp dụng quy ước đặt tên sau khi cấu hình mọi bảng. Tên khóa chính cần tên bảng đã cấu hình.
        DatabaseNamingConventions.Apply(modelBuilder);
    }
}
