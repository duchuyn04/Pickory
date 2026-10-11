using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Npgsql;
using Pickory.Modules.AiGateway;
using Pickory.Modules.Catalog;
using Pickory.Modules.Catalog.Products;
using Pickory.Modules.Catalog.Seeding;
using Pickory.Modules.Identity;
using Pickory.Modules.Shipping;
using Pickory.Shared.Configuration;
using Pickory.Shared.Persistence;

namespace Pickory.Api.Benchmark;

// Lệnh `catalog:benchmark`: sinh 500 sản phẩm rồi đo p95 của `GET /api/products` qua HTTP thật trong cùng tiến trình.
public static class CatalogBenchmark
{
    public const int ProductCount = 500;
    public const int WarmupRequests = 20;
    public const int MeasuredRequests = 400;
    public const double P95LimitMilliseconds = 300;

    public static async Task<int> RunAsync(WebApplication app, AppConfig config)
    {
        // Tên DB đã được kiểm trước khi dựng ứng dụng. Từ đây mọi thao tác ghi chỉ chạm vào DB benchmark.
        await CreateDatabaseIfMissingAsync(config.DatabaseUrl);
        // Dựng schema theo thứ tự của tài liệu migration. Migration AiGateway tạo extension `vector` dùng chung.
        await IdentityModule.MigrateIdentityDatabaseAsync(app.Services, CancellationToken.None);
        await AiGatewayModule.MigrateAiGatewayDatabaseAsync(app.Services, CancellationToken.None);
        await CatalogModule.MigrateCatalogDatabaseAsync(app.Services, CancellationToken.None);
        await ShippingModule.MigrateShippingDatabaseAsync(app.Services, CancellationToken.None);

        var catalog = BenchmarkProducts.Generate(CatalogSeedData.Create(), ProductCount);
        var seedTimer = Stopwatch.StartNew();
        // Dùng cùng đường ghi với `catalog:seed`, nên dữ liệu đo giống dữ liệu thật về ràng buộc và chỉ mục.
        var report = await CatalogSeeder.SeedAsync(app.Services, catalog, CancellationToken.None);
        Console.WriteLine($"Sinh dữ liệu mất {seedTimer.Elapsed.TotalSeconds:0.0} giây:");
        foreach (var line in report.Lines())
        {
            Console.WriteLine($"  {line}");
        }

        await app.StartAsync();
        try
        {
            return await MeasureAsync(app, catalog);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static async Task<int> MeasureAsync(WebApplication app, SeedCatalog catalog)
    {
        // Cổng 0 để hệ điều hành cấp cổng trống. Địa chỉ thật chỉ biết sau khi máy chủ đã khởi động.
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var client = new HttpClient { BaseAddress = new Uri(address) };

        var categorySlugs = catalog.Categories.Select(category => SlugGenerator.ToSlug(category.Name)).ToList();
        var brandSlugs = catalog.BrandNames.Take(3).Select(SlugGenerator.ToSlug).ToList();
        var paths = BenchmarkQueries.BuildPaths(categorySlugs, brandSlugs, "attr.ram_gb=16");

        // Làm nóng để lần gọi đầu (biên dịch JIT, mở kết nối, nạp cache kế hoạch truy vấn) không tính vào số đo.
        foreach (var path in BenchmarkQueries.Repeat(paths, WarmupRequests))
        {
            await SendAsync(client, path);
        }

        var latencies = new List<double>(MeasuredRequests);
        foreach (var path in BenchmarkQueries.Repeat(paths, MeasuredRequests))
        {
            var timer = Stopwatch.StartNew();
            await SendAsync(client, path);
            latencies.Add(timer.Elapsed.TotalMilliseconds);
        }

        var summary = LatencyStatistics.Summarize(latencies);
        PrintResult(summary, paths.Count);
        if (summary.IsP95Below(P95LimitMilliseconds))
        {
            return 0;
        }

        Console.Error.WriteLine($"Không đạt: p95 {summary.P95:0.0} ms, ngưỡng {P95LimitMilliseconds} ms.");
        return 1;
    }

    // Phản hồi lỗi làm số đo vô nghĩa, nên dừng ngay thay vì tính cả thời gian trả lỗi.
    private static async Task SendAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GET {path} trả {(int)response.StatusCode}.");
        }

        // Đọc hết thân phản hồi, vì khách chỉ thấy trang sau khi nhận đủ dữ liệu.
        await response.Content.ReadAsByteArrayAsync();
    }

    private static void PrintResult(LatencySummary summary, int distinctQueries)
    {
        Console.WriteLine($"GET /api/products với {ProductCount} sản phẩm active, {distinctQueries} kiểu truy vấn:");
        Console.WriteLine($"  Số request đo: {summary.Count} (làm nóng {WarmupRequests})");
        Console.WriteLine($"  p50: {summary.P50:0.0} ms");
        Console.WriteLine($"  p95: {summary.P95:0.0} ms");
        Console.WriteLine($"  max: {summary.Max:0.0} ms");
        var memoryGb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024 / 1024;
        Console.WriteLine(
            $"  Máy: {RuntimeInformation.OSDescription}, {Environment.ProcessorCount} luồng CPU, {memoryGb:0.0} GB RAM, {RuntimeInformation.FrameworkDescription}");
    }

    private static async Task CreateDatabaseIfMissingAsync(string databaseUrl)
    {
        var target = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Convert(databaseUrl));
        var databaseName = target.Database!;
        // Kết nối DB hệ thống `postgres` để tạo DB benchmark trên volume PostgreSQL có sẵn.
        target.Database = "postgres";
        target.Pooling = false;
        await using var connection = new NpgsqlConnection(target.ConnectionString);
        await connection.OpenAsync();

        await using var existsCommand = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
        existsCommand.Parameters.AddWithValue("name", databaseName);
        if (await existsCommand.ExecuteScalarAsync() is not null)
        {
            return;
        }

        // Tên DB đã qua `EnsureBenchmarkDatabase`. Dấu ngoặc kép bao tên và nhân đôi dấu ngoặc kép bên trong.
        var quotedName = databaseName.Replace("\"", "\"\"");
        await using var createCommand = new NpgsqlCommand($"CREATE DATABASE \"{quotedName}\"", connection);
        await createCommand.ExecuteNonQueryAsync();
        Console.WriteLine($"Đã tạo DB {databaseName}.");
    }
}
