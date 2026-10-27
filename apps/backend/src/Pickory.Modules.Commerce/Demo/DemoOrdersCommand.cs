using Microsoft.Extensions.DependencyInjection;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Commerce.Persistence;

namespace Pickory.Modules.Commerce.Demo;

// Lệnh `commerce:demo-orders` tạo đơn mẫu cho báo cáo trên DB dev. `--reset` xóa đúng các đơn mẫu.
internal static class DemoOrdersCommand
{
    public const string Command = "commerce:demo-orders";
    private const string ResetOption = "--reset";
    private const string Usage = "Cách dùng: commerce:demo-orders [--reset]";

    public static bool IsCommand(IReadOnlyList<string> args) => args.Count > 0 && args[0] == Command;

    public static async Task<int> RunAsync(
        IServiceProvider services,
        IReadOnlyList<string> args,
        bool isProduction,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        // Dữ liệu mẫu chỉ dành cho DB dev. Production không bao giờ được nhận chúng.
        if (isProduction)
        {
            await error.WriteLineAsync("commerce:demo-orders không chạy ở môi trường Production.");
            return 1;
        }

        if (!IsCommand(args))
        {
            await error.WriteLineAsync(Usage);
            return 1;
        }

        var reset = args.Count == 2 && args[1] == ResetOption;
        if (args.Count > 2 || (args.Count == 2 && !reset))
        {
            await error.WriteLineAsync(Usage);
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
