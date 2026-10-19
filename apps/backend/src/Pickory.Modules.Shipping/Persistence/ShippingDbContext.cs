using Microsoft.EntityFrameworkCore;
using Npgsql;
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
    public DbSet<ShippingFallbackFee> FallbackFees => Set<ShippingFallbackFee>();
    public DbSet<ShippingQuoteCache> QuoteCache => Set<ShippingQuoteCache>();

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

    // Dùng data source chung của API, nên mọi module chia nhau một pool kết nối. Ánh xạ enum của data source nằm ở `MapEnums`.
    public static void ConfigureOptions(DbContextOptionsBuilder options, NpgsqlDataSource dataSource)
    {
        options.UseNpgsql(dataSource, postgres =>
        {
            postgres.MigrationsHistoryTable("__EFMigrationsHistory", "ef_shipping");
            // EF Core cần ánh xạ enum riêng cho kiểu của nó. Thiếu bước này EF đọc enum như số nguyên.
            postgres.MapEnum<ShipmentStatus>("ShipmentStatus");
            postgres.MapEnum<ShipmentEventSource>("ShipmentEventSource");
        });
    }

    // Ánh xạ enum của module vào data source chung. Tên và nhãn phải khớp enum đã có trong DB.
    public static void MapEnums(NpgsqlDataSourceBuilder builder)
    {
        builder.MapEnum<ShipmentStatus>("ShipmentStatus");
        builder.MapEnum<ShipmentEventSource>("ShipmentEventSource");
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
