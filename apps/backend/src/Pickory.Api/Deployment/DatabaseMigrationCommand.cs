using Microsoft.Extensions.DependencyInjection;
using Pickory.Modules.Catalog;
using Pickory.Modules.Commerce;
using Pickory.Modules.Identity;
using Pickory.Modules.Shipping;
using Pickory.Shared.Configuration;

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
        return RunStepsAsync(CreateSteps(services), output, error, cancellationToken);
    }

    // Tách danh sách bước ra khỏi lệnh chạy để test kiểm được thứ tự module mà không cần DB.
    public static DatabaseMigrationStep[] CreateSteps(IServiceProvider services)
    {
        // Thứ tự giống `E2eStartup`, để DB dựng bằng `db:migrate` giống DB của E2E.
        return
        [
            new("Identity", token => IdentityModule.MigrateIdentityDatabaseAsync(services, token)),
            new("Catalog", token => CatalogModule.MigrateCatalogDatabaseAsync(services, token)),
            new("Shipping", token => ShippingModule.MigrateShippingDatabaseAsync(services, token)),
            new("Commerce", token => CommerceModule.MigrateCommerceDatabaseAsync(services, token)),
            // Dọn cuối cùng, sau migration của mọi module, để không xóa bảng khi bước trước còn có thể lỗi.
            new(AiGatewayCleanup.StepName, token => AiGatewayCleanup.RunAsync(services.GetRequiredService<AppConfig>(), token)),
        ];
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
