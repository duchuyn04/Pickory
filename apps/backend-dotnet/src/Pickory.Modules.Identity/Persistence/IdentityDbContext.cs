using Microsoft.EntityFrameworkCore;
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
        PrismaModelConventions.Configure(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Giữ thứ tự nhãn PostgreSQL vì phép so sánh enum dùng thứ tự khai báo, không dùng thứ tự chữ cái.
        modelBuilder.HasPostgresEnum("UserKind", ["customer", "staff"]);
        modelBuilder.HasPostgresEnum("EmailTokenPurpose", ["verify", "reset", "invite"]);

        // Giữ cột nullable để tài khoản chưa khóa hoặc chưa xóa không nhận thời gian giả.
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(value => value.Id);
            // Sinh UUID (Universally Unique Identifier) khi thêm bản ghi để tránh cấp phát lúc đọc dữ liệu đã có.
            entity.Property(value => value.Id).ValueGeneratedOnAdd()
                .HasValueGenerator<Microsoft.EntityFrameworkCore.ValueGeneration.StringValueGenerator>();
            entity.Property(value => value.Email).IsRequired();
            entity.Property(value => value.EmailVerified).HasDefaultValue(false);
            entity.Property(value => value.PasswordHash).IsRequired(false);
            entity.Property(value => value.Name).IsRequired(false);
            entity.Property(value => value.Phone).IsRequired(false);
            entity.Property(value => value.Kind).IsRequired();
            entity.Property(value => value.LockedAt).IsRequired(false);
            entity.Property(value => value.DeletedAt).IsRequired(false);
            entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(value => value.UpdatedAt).IsRequired();
            entity.HasIndex(value => value.Email).IsUnique().HasDatabaseName("users_email_key");
        });

        // Giữ `CURRENT_TIMESTAMP` của DB để Node và EF dùng cùng nguồn thời gian tạo vai trò.
        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("roles");
            entity.HasKey(value => value.Id);
            // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
            entity.Property(value => value.Id).ValueGeneratedOnAdd()
                .HasValueGenerator<Microsoft.EntityFrameworkCore.ValueGeneration.StringValueGenerator>();
            entity.Property(value => value.Code).IsRequired();
            entity.Property(value => value.Name).IsRequired();
            entity.Property(value => value.IsSystem).HasDefaultValue(false);
            entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(value => value.Code).IsUnique().HasDatabaseName("roles_code_key");
        });

        // Giữ khóa ghép để một quyền không xuất hiện hai lần trong cùng vai trò.
        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.ToTable("role_permissions");
            entity.HasKey(value => new { value.RoleId, value.Permission });
            entity.Property(value => value.RoleId).IsRequired();
            entity.Property(value => value.Permission).IsRequired();
            // Xóa quyền khi vai trò bị xóa để không còn quyền tham chiếu vai trò không tồn tại.
            entity.HasOne(value => value.Role)
                .WithMany(role => role.Permissions)
                .HasForeignKey(value => value.RoleId)
                .HasConstraintName("role_permissions_roleId_fkey")
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Giữ khóa ghép để người dùng không nhận cùng vai trò hai lần.
        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("user_roles");
            entity.HasKey(value => new { value.UserId, value.RoleId });
            entity.Property(value => value.UserId).IsRequired();
            entity.Property(value => value.RoleId).IsRequired();
            // Xóa liên kết khi tài khoản bị xóa để không còn quyền của tài khoản cũ.
            entity.HasOne(value => value.User)
                .WithMany(user => user.Roles)
                .HasForeignKey(value => value.UserId)
                .HasConstraintName("user_roles_userId_fkey")
                .OnDelete(DeleteBehavior.Cascade);
            // Xóa liên kết khi vai trò bị xóa để quyền không tham chiếu vai trò không tồn tại.
            entity.HasOne(value => value.Role)
                .WithMany(role => role.Users)
                .HasForeignKey(value => value.RoleId)
                .HasConstraintName("user_roles_roleId_fkey")
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Giữ token duy nhất để hai phiên không có cùng thông tin xác thực.
        modelBuilder.Entity<Session>(entity =>
        {
            entity.ToTable("sessions");
            entity.HasKey(value => value.Id);
            // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
            entity.Property(value => value.Id).ValueGeneratedOnAdd()
                .HasValueGenerator<Microsoft.EntityFrameworkCore.ValueGeneration.StringValueGenerator>();
            entity.Property(value => value.TokenHash).IsRequired();
            entity.Property(value => value.UserId).IsRequired();
            entity.Property(value => value.ExpiresAt).IsRequired();
            entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(value => value.TokenHash).IsUnique().HasDatabaseName("sessions_tokenHash_key");
            entity.HasIndex(value => value.UserId).HasDatabaseName("sessions_userId_idx");
            // Xóa phiên cùng tài khoản để phiên không còn tham chiếu tài khoản đã xóa.
            entity.HasOne(value => value.User)
                .WithMany(user => user.Sessions)
                .HasForeignKey(value => value.UserId)
                .HasConstraintName("sessions_userId_fkey")
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Giữ khóa duy nhất theo nhà cung cấp để một danh tính ngoài không liên kết hai lần.
        modelBuilder.Entity<OAuthAccount>(entity =>
        {
            entity.ToTable("oauth_accounts");
            entity.HasKey(value => value.Id);
            // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
            entity.Property(value => value.Id).ValueGeneratedOnAdd()
                .HasValueGenerator<Microsoft.EntityFrameworkCore.ValueGeneration.StringValueGenerator>();
            entity.Property(value => value.Provider).IsRequired();
            entity.Property(value => value.ProviderUserId).IsRequired();
            entity.Property(value => value.UserId).IsRequired();
            entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(value => value.UserId).HasDatabaseName("oauth_accounts_userId_idx");
            entity.HasIndex(value => new { value.Provider, value.ProviderUserId })
                .IsUnique()
                .HasDatabaseName("oauth_accounts_provider_providerUserId_key");
            // Xóa danh tính ngoài cùng tài khoản để không còn liên kết đăng nhập của tài khoản cũ.
            entity.HasOne(value => value.User)
                .WithMany(user => user.OAuthAccounts)
                .HasForeignKey(value => value.UserId)
                .HasConstraintName("oauth_accounts_userId_fkey")
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Giữ token duy nhất để cùng token không cấp quyền cho nhiều bản ghi.
        modelBuilder.Entity<EmailToken>(entity =>
        {
            entity.ToTable("email_tokens");
            entity.HasKey(value => value.Id);
            // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
            entity.Property(value => value.Id).ValueGeneratedOnAdd()
                .HasValueGenerator<Microsoft.EntityFrameworkCore.ValueGeneration.StringValueGenerator>();
            entity.Property(value => value.TokenHash).IsRequired();
            entity.Property(value => value.UserId).IsRequired();
            entity.Property(value => value.Purpose).IsRequired();
            entity.Property(value => value.ExpiresAt).IsRequired();
            entity.Property(value => value.UsedAt).IsRequired(false);
            entity.Property(value => value.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(value => value.TokenHash).IsUnique().HasDatabaseName("email_tokens_tokenHash_key");
            entity.HasIndex(value => value.UserId).HasDatabaseName("email_tokens_userId_idx");
            // Xóa token cùng tài khoản để token không còn tham chiếu tài khoản đã xóa.
            entity.HasOne(value => value.User)
                .WithMany(user => user.EmailTokens)
                .HasForeignKey(value => value.UserId)
                .HasConstraintName("email_tokens_userId_fkey")
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Cho phép khách chưa có tài khoản ghi đồng thuận bằng `AnonymousId`.
        modelBuilder.Entity<ConsentRecord>(entity =>
        {
            entity.ToTable("consent_records");
            entity.HasKey(value => value.Id);
            // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
            entity.Property(value => value.Id).ValueGeneratedOnAdd()
                .HasValueGenerator<Microsoft.EntityFrameworkCore.ValueGeneration.StringValueGenerator>();
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
        });

        // Giữ `jsonb` nullable để nhật ký có thể thiếu trạng thái trước hoặc sau.
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(value => value.Id);
            // Sinh UUID khi thêm bản ghi để không cấp phát UUID lúc đọc dữ liệu đã có.
            entity.Property(value => value.Id).ValueGeneratedOnAdd()
                .HasValueGenerator<Microsoft.EntityFrameworkCore.ValueGeneration.StringValueGenerator>();
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
        });

        // Áp dụng quy ước Prisma cho tên cột và khóa chính sau khi đã định nghĩa toàn bộ thực thể.
        PrismaModelConventions.Apply(modelBuilder);
    }
}
