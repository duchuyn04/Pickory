using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Modules.Shipping.Entities;
using Pickory.Shared.Persistence;

// Contracts cũng có kiểu `Shipment` cho bên gọi. DbContext chỉ dùng entity của module.
using Shipment = Pickory.Modules.Shipping.Entities.Shipment;

namespace Pickory.Modules.Shipping.Persistence;

public sealed class ShippingDbContext(DbContextOptions<ShippingDbContext> options) : DbContext(options)
{
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentEvent> ShipmentEvents => Set<ShipmentEvent>();
    public DbSet<AddressProvince> AddressProvinces => Set<AddressProvince>();
    public DbSet<AddressWard> AddressWards => Set<AddressWard>();
    public DbSet<ShippingSettings> Settings => Set<ShippingSettings>();

    public static void ConfigureOptions(DbContextOptionsBuilder options, string connectionString)
    {
        // Lịch sử migration nằm ở schema riêng, tách khỏi các module khác. Npgsql dùng enum Postgres, không dùng số nguyên.
        options.UseNpgsql(connectionString, postgres =>
        {
            postgres.MigrationsHistoryTable("__EFMigrationsHistory", "ef_shipping");
            postgres.MapEnum<ShipmentStatus>("ShipmentStatus");
            postgres.MapEnum<ShipmentEventSource>("ShipmentEventSource");
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Giữ UTC (Coordinated Universal Time) để các module không áp múi giờ máy chủ khi đọc DB.
        DatabaseNamingConventions.Configure(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Tên enum PascalCase như `ProductStatus` của catalog. Nhãn là snake_case, khớp tên mà Npgsql suy ra từ tên giá trị C#.
        modelBuilder.HasPostgresEnum("ShipmentStatus",
        [
            "pending_pickup", "in_transit", "delivering", "delivered", "delivery_failed",
            "returning", "returned", "cancelled", "exception",
        ]);
        modelBuilder.HasPostgresEnum("ShipmentEventSource", ["webhook", "poll", "manual"]);

        // Mỗi bảng có lớp cấu hình riêng trong `Configurations/`. Chỉ quét assembly của module để không nhận bảng module khác.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShippingDbContext).Assembly);

        // Áp dụng quy ước đặt tên sau khi cấu hình mọi bảng. Tên khóa chính cần tên bảng đã cấu hình.
        DatabaseNamingConventions.Apply(modelBuilder);
    }
}
