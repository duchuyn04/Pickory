using Pickory.Modules.AiGateway;
using Pickory.Modules.Catalog;
using Pickory.Modules.Commerce;
using Pickory.Modules.Identity;
using Pickory.Modules.Shipping;

namespace Pickory.Api.Deployment;

// Một bước migration gắn với tên module. Tên module giúp người đọc log biết bước nào lỗi.
public sealed record DatabaseMigrationStep(string ModuleName, Func<CancellationToken, Task> MigrateAsync);

// Lệnh `db:migrate` áp migration của mọi module rồi thoát. Render chạy lệnh này trước mỗi lần triển khai.
public static class DatabaseMigrationCommand
{
    public const string Command = "db:migrate";

    public static bool IsCommand(IReadOnlyList<string> args)
    {
        return args.Count > 0 && args[0] == Command;
    }

    public static Task<int> RunAsync(IServiceProvider services, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        // Thứ tự giống `E2eStartup`. Migration AiGateway tạo extension `vector` mà các module sau dùng chung.
        DatabaseMigrationStep[] steps =
        [
            new("Identity", token => IdentityModule.MigrateIdentityDatabaseAsync(services, token)),
            new("AiGateway", token => AiGatewayModule.MigrateAiGatewayDatabaseAsync(services, token)),
            new("Catalog", token => CatalogModule.MigrateCatalogDatabaseAsync(services, token)),
            new("Shipping", token => ShippingModule.MigrateShippingDatabaseAsync(services, token)),
            new("Commerce", token => CommerceModule.MigrateCommerceDatabaseAsync(services, token)),
        ];
        return RunStepsAsync(steps, output, error, cancellationToken);
    }

    // Trả mã thoát của tiến trình. Mã 1 làm Render hủy lần triển khai, nên bản API đang chạy được giữ.
    public static async Task<int> RunStepsAsync(
        IReadOnlyList<DatabaseMigrationStep> steps, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        foreach (var step in steps)
        {
            try
            {
                await step.MigrateAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Dừng ở module lỗi. Module sau có thể cần bảng hoặc extension mà bước lỗi chưa tạo.
                await error.WriteLineAsync($"Migration {step.ModuleName} thất bại: {exception.Message}");
                return 1;
            }

            await output.WriteLineAsync($"Migration {step.ModuleName} đã áp xong.");
        }

        return 0;
    }
}
