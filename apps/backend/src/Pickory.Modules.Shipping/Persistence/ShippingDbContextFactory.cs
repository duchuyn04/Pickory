using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pickory.Shared.Persistence;

namespace Pickory.Modules.Shipping.Persistence;

public sealed class ShippingDbContextFactory : IDesignTimeDbContextFactory<ShippingDbContext>
{
    public ShippingDbContext CreateDbContext(string[] args)
    {
        // Yêu cầu biến môi trường rõ ràng để công cụ migration kết nối đúng DB đích.
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? throw new InvalidOperationException("Thiếu biến môi trường DATABASE_URL cho migration EF Core của shipping.");
        var options = new DbContextOptionsBuilder<ShippingDbContext>();

        // Áp dụng cấu hình chuẩn của module để migration tạo metadata đồng nhất.
        ShippingDbContext.ConfigureOptions(options, PostgresConnectionString.Convert(databaseUrl));
        return new ShippingDbContext(options.Options);
    }
}
