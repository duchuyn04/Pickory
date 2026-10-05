using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Catalog.Entities;
using Pickory.Shared.Persistence;

namespace Pickory.Modules.Catalog.Persistence;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Sku> Skus => Set<Sku>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductSlugHistory> ProductSlugHistories => Set<ProductSlugHistory>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<ImageDeletionJob> ImageDeletionJobs => Set<ImageDeletionJob>();

    public static void ConfigureOptions(DbContextOptionsBuilder options, string connectionString)
    {
        // Tách lịch sử khỏi Prisma. Npgsql phải dùng kiểu enum hiện có, không dùng số nguyên.
        options.UseNpgsql(connectionString, postgres =>
        {
            postgres.MigrationsHistoryTable("__EFMigrationsHistory", "ef_catalog");
            postgres.MapEnum<ProductStatus>("ProductStatus");
            postgres.MapEnum<InventoryMovementType>("InventoryMovementType");
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
        modelBuilder.HasPostgresEnum("ProductStatus", ["draft", "active", "hidden", "discontinued"]);
        modelBuilder.HasPostgresEnum("InventoryMovementType", ["manual", "order_reserve", "order_release", "return"]);

        // Mỗi bảng có lớp cấu hình riêng trong `Configurations/`. Chỉ quét assembly của module để không nhận bảng module khác.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);

        // Áp dụng quy ước đặt tên sau khi cấu hình mọi bảng. Tên khóa chính cần tên bảng đã cấu hình.
        DatabaseNamingConventions.Apply(modelBuilder);
    }
}
