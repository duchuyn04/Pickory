using Microsoft.Extensions.DependencyInjection;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Commerce.Persistence;

namespace Pickory.Modules.Commerce.Demo;

// Lệnh `commerce:demo-orders` tạo đơn mẫu cho báo cáo trên DB dev. `--reset` xóa đúng các đơn mẫu.
internal static class DemoOrdersCommand
{
    public const string Command = "commerce:demo-orders";
    private const string ResetOption = "--reset";
    private const string AllowProductionOption = "--allow-production";
    private const string Usage = "Cách dùng: commerce:demo-orders [--reset] [--allow-production]";

    public static bool IsCommand(IReadOnlyList<string> args) => args.Count > 0 && args[0] == Command;

    public static async Task<int> RunAsync(
        IServiceProvider services,
        IReadOnlyList<string> args,
        bool isProduction,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (!IsCommand(args))
        {
            await error.WriteLineAsync(Usage);
            return 1;
        }

        var options = args.Skip(1).ToList();
        var reset = options.Contains(ResetOption);
        var allowProduction = options.Contains(AllowProductionOption);
        if (options.Any(option => option != ResetOption && option != AllowProductionOption))
        {
            await error.WriteLineAsync(Usage);
            return 1;
        }

        // Production chỉ nhận dữ liệu mẫu khi người chạy thêm cờ xác nhận.
        if (isProduction && !allowProduction)
        {
            await error.WriteLineAsync(
                "commerce:demo-orders không chạy ở Production. Thêm --allow-production nếu thật sự cần dữ liệu mẫu.");
            return 1;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();

            if (reset)
            {
                var database = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
                var deleted = await DemoOrderSeeder.ResetAsync(database, cancellationToken);
                await output.WriteLineAsync($"Đã xóa {deleted} đơn mẫu.");
                return 0;
            }

            var commerceDatabase = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
            var catalogReader = scope.ServiceProvider.GetRequiredService<ICatalogReader>();
            var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
            var result = await DemoOrderSeeder.SeedAsync(
                commerceDatabase, catalogReader, timeProvider.GetUtcNow(), cancellationToken);

            await output.WriteLineAsync($"Đã tạo {result.Created} đơn mẫu cho 6 tháng gần nhất.");
            if (result.Skipped > 0)
            {
                await output.WriteLineAsync($"Bỏ qua {result.Skipped} đơn mẫu đã có.");
            }

            return 0;
        }
        catch (Exception exception)
        {
            // Lệnh chạy ngoài web API, nên lỗi được ghi thẳng ra stderr cho người chạy lệnh đọc.
            await error.WriteLineAsync($"Tạo đơn mẫu thất bại: {exception.Message}");
            return 1;
        }
    }
}
