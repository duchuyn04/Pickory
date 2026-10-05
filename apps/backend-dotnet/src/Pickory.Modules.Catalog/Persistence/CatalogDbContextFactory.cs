using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pickory.Shared.Persistence;

namespace Pickory.Modules.Catalog.Persistence;

public sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        // Yêu cầu biến môi trường rõ ràng để công cụ migration kết nối đúng DB đích.
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? throw new InvalidOperationException("Thiếu biến môi trường DATABASE_URL cho migration EF Core của catalog.");
        var options = new DbContextOptionsBuilder<CatalogDbContext>();

        // Áp dụng cấu hình chuẩn của module để migration tạo metadata đồng nhất.
        CatalogDbContext.ConfigureOptions(options, PostgresConnectionString.Convert(databaseUrl));
        return new CatalogDbContext(options.Options);
    }
}
