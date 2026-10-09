using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pickory.Shared.Persistence;

namespace Pickory.Modules.Identity.Persistence;

public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        // Yêu cầu đích kết nối rõ ràng để công cụ migration không chọn nhầm DB.
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? throw new InvalidOperationException("Thiếu DATABASE_URL cho migration EF Core.");
        var options = new DbContextOptionsBuilder<IdentityDbContext>();

        // Dùng cùng cấu hình với ứng dụng để migration giữ đúng kiểu dữ liệu và lịch sử.
        IdentityDbContext.ConfigureOptions(options, PostgresConnectionString.Convert(databaseUrl));
        return new IdentityDbContext(options.Options);
    }
}
