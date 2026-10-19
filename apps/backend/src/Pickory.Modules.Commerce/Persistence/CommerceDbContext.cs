using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Shared.Persistence;

namespace Pickory.Modules.Commerce.Persistence;

public sealed class CommerceDbContext(DbContextOptions<CommerceDbContext> options) : DbContext(options)
{
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<SavedAddress> SavedAddresses => Set<SavedAddress>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ReviewReport> ReviewReports => Set<ReviewReport>();
    public DbSet<CommerceSettings> Settings => Set<CommerceSettings>();

    public static void ConfigureOptions(DbContextOptionsBuilder options, string connectionString)
    {
        // Lịch sử migration nằm ở schema riêng, tách khỏi các module khác. Npgsql dùng enum Postgres, không dùng số nguyên.
        options.UseNpgsql(connectionString, postgres =>
        {
            postgres.MigrationsHistoryTable("__EFMigrationsHistory", "ef_commerce");
            postgres.MapEnum<OrderStatus>("OrderStatus");
            postgres.MapEnum<PaymentStatus>("PaymentStatus");
            postgres.MapEnum<PaymentMethod>("PaymentMethod");
            postgres.MapEnum<ShippingFeeSource>("ShippingFeeSource");
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
        modelBuilder.HasPostgresEnum("OrderStatus",
        [
            "pending_payment", "pending_confirmation", "confirmed", "shipping", "delivered", "returned", "cancelled",
        ]);
        modelBuilder.HasPostgresEnum("PaymentStatus", ["unpaid", "paid", "refund_pending", "refunded"]);
        modelBuilder.HasPostgresEnum("PaymentMethod", ["cod", "sepay"]);
        modelBuilder.HasPostgresEnum("ShippingFeeSource", ["ghn", "cached", "fallback"]);

        // Mỗi bảng có lớp cấu hình riêng trong `Configurations/`. Chỉ quét assembly của module để không nhận bảng module khác.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CommerceDbContext).Assembly);

        // Áp dụng quy ước đặt tên sau khi cấu hình mọi bảng. Tên khóa chính cần tên bảng đã cấu hình.
        DatabaseNamingConventions.Apply(modelBuilder);
    }
}
