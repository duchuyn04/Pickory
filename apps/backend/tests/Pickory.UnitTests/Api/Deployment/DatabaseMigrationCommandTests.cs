using Microsoft.Extensions.DependencyInjection;
using Pickory.Api.Deployment;
using Xunit;

namespace Pickory.UnitTests.Api.Deployment;

// Lệnh `db:migrate` chạy trước mỗi lần triển khai. Mã thoát quyết định Render triển khai tiếp hay hủy.
public sealed class DatabaseMigrationCommandTests
{
    [Theory]
    [InlineData(new[] { "db:migrate" }, true)]
    [InlineData(new[] { "db:migrate", "--thua" }, true)]
    [InlineData(new[] { "--db:migrate" }, false)]
    [InlineData(new[] { "identity:seed" }, false)]
    [InlineData(new string[0], false)]
    // Chỉ tham số đầu tiên chọn lệnh. API chạy bình thường khi không có tham số.
    public void IsCommand_MatchesOnlyFirstArgument(string[] args, bool expected)
    {
        Assert.Equal(expected, DatabaseMigrationCommand.IsCommand(args));
    }

    [Fact]
    // Danh sách bước thật gồm sáu module theo thứ tự phụ thuộc, rồi bước dọn dấu vết module AI đã gỡ ở cuối.
    public void CreateSteps_ListsRemainingModulesInDependencyOrderThenAiCleanup()
    {
        var services = new ServiceCollection().BuildServiceProvider();

        var steps = DatabaseMigrationCommand.CreateSteps(services);

        Assert.Equal(["Identity", "Catalog", "Shipping", "Commerce", "ActivityTracking", "SupportDesk", "AiGatewayCleanup"], steps.Select(step => step.ModuleName));
    }

    [Fact]
    // Mọi bước chạy theo đúng thứ tự. Lệnh trả mã 0 để Render triển khai tiếp.
    public async Task RunStepsAsync_RunsStepsInOrderAndReturnsZero()
    {
        var executedModules = new List<string>();
        DatabaseMigrationStep[] steps =
        [
            CreateRecordingStep("Identity", executedModules),
            CreateRecordingStep("Catalog", executedModules),
            CreateRecordingStep("Shipping", executedModules),
        ];
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await DatabaseMigrationCommand.RunStepsAsync(steps, output, error, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(["Identity", "Catalog", "Shipping"], executedModules);
        Assert.Contains("Migration Shipping đã áp xong.", output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    // Bước lỗi dừng các bước sau. Lỗi ghi ra stderr kèm tên module để người đọc log biết chỗ hỏng.
    public async Task RunStepsAsync_StopsAtFailedStepAndReturnsOne()
    {
        var executedModules = new List<string>();
        DatabaseMigrationStep[] steps =
        [
            CreateRecordingStep("Identity", executedModules),
            new("Catalog", _ => throw new InvalidOperationException("không tạo được bảng sản phẩm")),
            CreateRecordingStep("Shipping", executedModules),
        ];
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await DatabaseMigrationCommand.RunStepsAsync(steps, output, error, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(["Identity"], executedModules);
        Assert.Equal("Migration Catalog thất bại: không tạo được bảng sản phẩm", error.ToString().Trim());
        Assert.DoesNotContain("Shipping", output.ToString());
    }

    [Fact]
    // Hủy lệnh không phải lỗi migration. Lệnh ném lại để tiến trình dừng thay vì báo lỗi sai lý do.
    public async Task RunStepsAsync_RethrowsCancellation()
    {
        DatabaseMigrationStep[] steps =
        [
            new("Identity", _ => throw new OperationCanceledException()),
        ];

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => DatabaseMigrationCommand.RunStepsAsync(steps, new StringWriter(), new StringWriter(), CancellationToken.None));
    }

    private static DatabaseMigrationStep CreateRecordingStep(string moduleName, List<string> executedModules)
    {
        return new DatabaseMigrationStep(moduleName, _ =>
        {
            executedModules.Add(moduleName);
            return Task.CompletedTask;
        });
    }
}
