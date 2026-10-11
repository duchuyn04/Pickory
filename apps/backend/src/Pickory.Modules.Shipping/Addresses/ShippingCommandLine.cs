using Microsoft.Extensions.DependencyInjection;

namespace Pickory.Modules.Shipping.Addresses;

// Lệnh `shipping:sync-addresses` chạy bằng `dotnet run --project Pickory.Api -- <lệnh>`. Lệnh đồng bộ một lần rồi thoát.
internal static class ShippingCommandLine
{
    public const string SyncAddressesCommand = "shipping:sync-addresses";

    public static bool IsSyncAddressesCommand(IReadOnlyList<string> args) => args.Count > 0 && args[0] == SyncAddressesCommand;

    // Trả mã thoát của tiến trình. Mã 0 là thành công, mã 1 là GHN lỗi hoặc đồng bộ không ghi được.
    public static async Task<int> RunSyncAddressesAsync(
        IServiceProvider services, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<IAddressSyncProcessor>();
            var result = await processor.SyncAsync(cancellationToken);

            // GHN lỗi hoặc trả dữ liệu rỗng: lệnh thất bại. Lý do đi ra stderr để người chạy lệnh đọc.
            if (!result.Succeeded)
            {
                await error.WriteLineAsync($"Đồng bộ danh mục địa chỉ thất bại: {result.FailureReason}");
                return 1;
            }

            await output.WriteLineAsync(
                $"Đồng bộ danh mục địa chỉ thành công: {result.ProvinceCount} tỉnh/thành, {result.WardCount} phường/xã.");
            return 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Lệnh chạy ngoài web API, nên lỗi DB được ghi thẳng ra stderr thay vì để tiến trình sập với stack trace.
            await error.WriteLineAsync($"Đồng bộ danh mục địa chỉ thất bại: {exception.Message}");
            return 1;
        }
    }
}
