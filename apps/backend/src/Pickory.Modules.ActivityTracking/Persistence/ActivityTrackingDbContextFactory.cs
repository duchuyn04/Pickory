using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pickory.Shared.Persistence;

namespace Pickory.Modules.ActivityTracking.Persistence;

internal sealed class ActivityTrackingDbContextFactory : IDesignTimeDbContextFactory<ActivityTrackingDbContext>
{
    public ActivityTrackingDbContext CreateDbContext(string[] args)
    {
        // Yêu cầu biến môi trường rõ ràng để công cụ migration kết nối đúng DB đích.
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? throw new InvalidOperationException("Thiếu biến môi trường DATABASE_URL cho migration EF Core của activity-tracking.");
        var options = new DbContextOptionsBuilder<ActivityTrackingDbContext>();

        // Áp dụng cấu hình chuẩn của module để migration tạo metadata đồng nhất.
        ActivityTrackingDbContext.ConfigureOptions(options, PostgresConnectionString.Convert(databaseUrl));
        return new ActivityTrackingDbContext(options.Options);
    }
}
