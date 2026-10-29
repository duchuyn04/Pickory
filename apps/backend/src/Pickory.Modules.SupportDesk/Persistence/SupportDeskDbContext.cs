using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Shared.Persistence;

namespace Pickory.Modules.SupportDesk.Persistence;

internal sealed class SupportDeskDbContext(DbContextOptions<SupportDeskDbContext> options) : DbContext(options)
{
    public DbSet<SupportConversation> Conversations => Set<SupportConversation>();
    public DbSet<SupportMessage> Messages => Set<SupportMessage>();
    public DbSet<SupportSession> Sessions => Set<SupportSession>();
    public DbSet<CallbackRequest> CallbackRequests => Set<CallbackRequest>();
    public DbSet<CannedResponse> CannedResponses => Set<CannedResponse>();
    public DbSet<StaffPresence> StaffPresences => Set<StaffPresence>();
    public DbSet<SupportSettings> Settings => Set<SupportSettings>();

    public static void ConfigureOptions(DbContextOptionsBuilder options, string connectionString)
    {
        // Lịch sử migration nằm ở schema riêng, tách khỏi các module khác. Npgsql dùng enum Postgres, không dùng số nguyên.
        options.UseNpgsql(connectionString, postgres =>
        {
            postgres.MigrationsHistoryTable("__EFMigrationsHistory", "ef_support_desk");
            MapEfEnums(postgres);
        });
    }

    // Dùng data source chung của API, nên mọi module chia nhau một pool kết nối. Ánh xạ enum của data source nằm ở `MapEnums`.
    public static void ConfigureOptions(DbContextOptionsBuilder options, NpgsqlDataSource dataSource)
    {
        options.UseNpgsql(dataSource, postgres =>
        {
            postgres.MigrationsHistoryTable("__EFMigrationsHistory", "ef_support_desk");
            // EF Core cần ánh xạ enum riêng cho kiểu của nó. Thiếu bước này EF đọc enum như số nguyên.
            MapEfEnums(postgres);
        });
    }

    // Ánh xạ enum của module vào data source chung. Tên và nhãn phải khớp enum tạo trong migration.
    public static void MapEnums(NpgsqlDataSourceBuilder builder)
    {
        builder.MapEnum<SupportConversationStatus>("SupportConversationStatus");
        builder.MapEnum<SupportMessageRole>("SupportMessageRole");
        builder.MapEnum<CallbackRequestStatus>("CallbackRequestStatus");
        builder.MapEnum<StaffPresenceStatus>("StaffPresenceStatus");
    }

    // Hai cách tạo tùy chọn (chuỗi kết nối và data source) dùng chung một danh sách enum, nên danh sách không lệch nhau.
    private static void MapEfEnums(NpgsqlDbContextOptionsBuilder postgres)
    {
        postgres.MapEnum<SupportConversationStatus>("SupportConversationStatus");
        postgres.MapEnum<SupportMessageRole>("SupportMessageRole");
        postgres.MapEnum<CallbackRequestStatus>("CallbackRequestStatus");
        postgres.MapEnum<StaffPresenceStatus>("StaffPresenceStatus");
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Giữ UTC (Coordinated Universal Time) để các module không áp múi giờ máy chủ khi đọc DB.
        DatabaseNamingConventions.Configure(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Giữ thứ tự nhãn PostgreSQL. Phép so sánh enum dùng thứ tự khai báo, không dùng thứ tự chữ cái.
        modelBuilder.HasPostgresEnum("SupportConversationStatus", ["open", "closed"]);
        modelBuilder.HasPostgresEnum("SupportMessageRole", ["customer", "staff", "system"]);
        modelBuilder.HasPostgresEnum("CallbackRequestStatus", ["pending", "done", "unreachable"]);
        modelBuilder.HasPostgresEnum("StaffPresenceStatus", ["available", "away"]);

        // Mỗi bảng có lớp cấu hình riêng trong `Configurations/`. Chỉ quét assembly của module để không nhận bảng module khác.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SupportDeskDbContext).Assembly);

        // Áp dụng quy ước đặt tên sau khi cấu hình mọi bảng. Tên khóa chính cần tên bảng đã cấu hình.
        DatabaseNamingConventions.Apply(modelBuilder);
    }
}
