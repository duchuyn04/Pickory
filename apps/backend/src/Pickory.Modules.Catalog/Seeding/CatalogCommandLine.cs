namespace Pickory.Modules.Catalog.Seeding;

// Lệnh `catalog:seed` và `catalog:benchmark` chạy bằng `dotnet run --project Pickory.Api -- <lệnh>`. Lệnh chạy xong thì thoát.
public static class CatalogCommandLine
{
    public const string SeedCommand = "catalog:seed";
    public const string BenchmarkCommand = "catalog:benchmark";

    public static bool IsSeedCommand(IReadOnlyList<string> args) => args.Count > 0 && args[0] == SeedCommand;

    public static bool IsBenchmarkCommand(IReadOnlyList<string> args) => args.Count > 0 && args[0] == BenchmarkCommand;

    // Trả mã thoát của tiến trình. Mã 0 là thành công, mã 1 là lệnh bị từ chối hoặc dữ liệu mẫu có lỗi.
    public static async Task<int> RunSeedAsync(
        IServiceProvider services, bool isProduction, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        // Dữ liệu mẫu là sản phẩm giả. Production không bao giờ được nhận chúng, nên từ chối trước mọi thao tác ghi.
        if (isProduction)
        {
            await error.WriteLineAsync("catalog:seed không chạy ở môi trường Production.");
            return 1;
        }

        try
        {
            var report = await CatalogSeeder.SeedAsync(services, CatalogSeedData.Create(), cancellationToken);
            await output.WriteLineAsync("Seed catalog thành công:");
            foreach (var line in report.Lines())
            {
                await output.WriteLineAsync($"  {line}");
            }

            return 0;
        }
        catch (Exception exception)
        {
            // Lệnh chạy ngoài web API, nên lỗi được ghi thẳng ra stderr cho người chạy lệnh đọc.
            await error.WriteLineAsync($"Seed catalog thất bại: {exception.Message}");
            return 1;
        }
    }
}
