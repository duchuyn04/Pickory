using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Entities;
using Pickory.Shared.Persistence;

namespace Pickory.Modules.Identity.Persistence;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<OAuthAccount> OAuthAccounts => Set<OAuthAccount>();
    public DbSet<EmailToken> EmailTokens => Set<EmailToken>();
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public static void ConfigureOptions(DbContextOptionsBuilder options, string connectionString)
    {
        // Tách lịch sử khỏi Prisma. Npgsql phải dùng kiểu enum hiện có, không dùng số nguyên.
        options.UseNpgsql(connectionString, postgres =>
        {
            postgres.MigrationsHistoryTable("__EFMigrationsHistory", "ef_identity");
            postgres.MapEnum<UserKind>("UserKind");
            postgres.MapEnum<EmailTokenPurpose>("EmailTokenPurpose");
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Giữ UTC (Coordinated Universal Time) để các module không áp múi giờ máy chủ khi đọc DB.
        DatabaseNamingConventions.Configure(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Giữ thứ tự nhãn PostgreSQL vì phép so sánh enum dùng thứ tự khai báo, không dùng thứ tự chữ cái.
        modelBuilder.HasPostgresEnum("UserKind", ["customer", "staff"]);
        modelBuilder.HasPostgresEnum("EmailTokenPurpose", ["verify", "reset", "invite"]);

        // Mỗi bảng có lớp cấu hình riêng trong `Configurations/`. Chỉ quét assembly của module để không nhận bảng module khác.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);

        // Áp dụng quy ước đặt tên sau khi cấu hình mọi bảng. Tên khóa chính cần tên bảng đã cấu hình.
        DatabaseNamingConventions.Apply(modelBuilder);
    }
}
