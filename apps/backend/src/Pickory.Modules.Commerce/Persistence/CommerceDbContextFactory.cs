using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pickory.Shared.Persistence;

namespace Pickory.Modules.Commerce.Persistence;

public sealed class CommerceDbContextFactory : IDesignTimeDbContextFactory<CommerceDbContext>
{
    public CommerceDbContext CreateDbContext(string[] args)
    {
        // Yêu cầu biến môi trường rõ ràng để công cụ migration kết nối đúng DB đích.
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? throw new InvalidOperationException("Thiếu biến môi trường DATABASE_URL cho migration EF Core của commerce.");
        var options = new DbContextOptionsBuilder<CommerceDbContext>();

        // Áp dụng cấu hình chuẩn của module để migration tạo metadata đồng nhất.
        CommerceDbContext.ConfigureOptions(options, PostgresConnectionString.Convert(databaseUrl));
        return new CommerceDbContext(options.Options);
    }
}
