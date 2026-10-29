using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pickory.Shared.Persistence;

namespace Pickory.Modules.SupportDesk.Persistence;

internal sealed class SupportDeskDbContextFactory : IDesignTimeDbContextFactory<SupportDeskDbContext>
{
    public SupportDeskDbContext CreateDbContext(string[] args)
    {
        // Yêu cầu biến môi trường rõ ràng để công cụ migration kết nối đúng DB đích.
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? throw new InvalidOperationException("Thiếu biến môi trường DATABASE_URL cho migration EF Core của support-desk.");
        var options = new DbContextOptionsBuilder<SupportDeskDbContext>();

        // Áp dụng cấu hình chuẩn của module để migration tạo metadata đồng nhất.
        SupportDeskDbContext.ConfigureOptions(options, PostgresConnectionString.Convert(databaseUrl));
        return new SupportDeskDbContext(options.Options);
    }
}
