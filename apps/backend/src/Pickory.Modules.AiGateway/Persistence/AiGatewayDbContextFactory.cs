using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pickory.Shared.Persistence;

namespace Pickory.Modules.AiGateway.Persistence;

public sealed class AiGatewayDbContextFactory : IDesignTimeDbContextFactory<AiGatewayDbContext>
{
    public AiGatewayDbContext CreateDbContext(string[] args)
    {
        // Yêu cầu đích kết nối rõ ràng để công cụ migration không chọn nhầm DB.
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? throw new InvalidOperationException("Thiếu DATABASE_URL cho migration EF Core.");
        var options = new DbContextOptionsBuilder<AiGatewayDbContext>();

        // Dùng cùng cấu hình với ứng dụng để migration giữ đúng kiểu dữ liệu và lịch sử.
        AiGatewayDbContext.ConfigureOptions(options, PostgresConnectionString.Convert(databaseUrl));
        return new AiGatewayDbContext(options.Options);
    }
}
