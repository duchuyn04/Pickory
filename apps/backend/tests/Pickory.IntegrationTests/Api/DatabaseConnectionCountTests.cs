using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Shared.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Api;

// POOL-02: `DATABASE_POOL_MAX` giới hạn kết nối thật của cả tiến trình API, không chỉ giá trị trong chuỗi kết nối.
// Mỗi module từng có pool riêng, nên API mở tới số module nhân với giới hạn.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class DatabaseConnectionCountTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    : IClassFixture<PickoryWebApplicationFactory>
{
    private const int PoolLimit = 3;
    private const int RequestsPerRoute = 12;

    // Tuyến đọc chạm đủ bốn module: Catalog, Shipping, AiGateway và Identity (qua phiên nhân viên).
    private static readonly string[] Routes =
    [
        "/api/products?pageSize=24",
        "/api/categories",
        "/api/addresses/provinces",
        "/api/admin/ai/usage",
        "/api/admin/ai/settings",
        "/api/me",
    ];

    private string MonitorConnectionString(string applicationName)
    {
        // Kết nối đo không dùng pool, để chính nó không tạo thêm kết nối nằm chờ trong pool.
        var builder = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Convert(database.DatabaseUrl))
        {
            Pooling = false,
            ApplicationName = $"{applicationName}-monitor",
        };
        return builder.ConnectionString;
    }

    private static async Task<int> CountConnectionsAsync(NpgsqlConnection monitor, string applicationName)
    {
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE application_name = @name", monitor);
        command.Parameters.AddWithValue("name", applicationName);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Api_UnderParallelLoad_NeverOpensMoreConnectionsThanThePoolLimit()
    {
        var applicationName = $"pool02-{Guid.NewGuid():N}"[..24];
        var separator = database.DatabaseUrl.Contains('?') ? "&" : "?";
        var limitedUrl = $"{database.DatabaseUrl}{separator}application_name={applicationName}";
        // `Program.cs` đọc cấu hình ngay khi dựng host, nên giá trị phải đi qua `UseSetting` như `DatabasePoolLimitTests`.
        using var limited = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("DATABASE_URL", limitedUrl);
            builder.UseSetting("DATABASE_POOL_MAX", PoolLimit.ToString());
        });

        // Phiên nhân viên nằm trong DB test, nên tạo trước khi bắt đầu đo.
        var (_, session) = await IdentityTestData.CreateStaffSessionAsync(database, "sales");

        await using var monitor = new NpgsqlConnection(MonitorConnectionString(applicationName));
        await monitor.OpenAsync();
        var peak = 0;
        using var samplingStopped = new CancellationTokenSource();
        // Lấy mẫu liên tục trong lúc host khởi động và chịu tải. Ngưỡng nhìn vào đỉnh, không nhìn vào một thời điểm.
        var sampler = Task.Run(async () =>
        {
            while (!samplingStopped.IsCancellationRequested)
            {
                peak = Math.Max(peak, await CountConnectionsAsync(monitor, applicationName));
                await Task.Delay(5);
            }
        });

        // Tạo client khởi động host, nên worker nền của mọi module cũng chạy trong khoảng đo.
        using var client = limited.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var requests = Routes
            .SelectMany(route => Enumerable.Range(0, RequestsPerRoute).Select(_ => route))
            .Select(route => client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, route, $"pk_session={session}")))
            .ToList();
        var responses = await Task.WhenAll(requests);
        samplingStopped.Cancel();
        await sampler;

        Assert.True(peak > 0, "Không thấy kết nối nào của API trong pg_stat_activity, nên phép đo không có giá trị.");
        Assert.True(peak <= PoolLimit, $"API mở tối đa {peak} kết nối, vượt giới hạn {PoolLimit}.");
        // Xếp hàng chờ kết nối không được làm request lỗi. Mọi tuyến đọc phải trả 200.
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
    }
}
