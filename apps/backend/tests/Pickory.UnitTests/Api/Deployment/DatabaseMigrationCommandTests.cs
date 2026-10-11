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
    // Mọi bước chạy theo đúng thứ tự. Lệnh trả mã 0 để Render triển khai tiếp.
    public async Task RunStepsAsync_RunsStepsInOrderAndReturnsZero()
    {
        var executedModules = new List<string>();
        DatabaseMigrationStep[] steps =
        [
            CreateRecordingStep("Identity", executedModules),
            CreateRecordingStep("AiGateway", executedModules),
            CreateRecordingStep("Catalog", executedModules),
        ];
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await DatabaseMigrationCommand.RunStepsAsync(steps, output, error, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(["Identity", "AiGateway", "Catalog"], executedModules);
        Assert.Contains("Migration Catalog đã áp xong.", output.ToString());
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
            new("AiGateway", _ => throw new InvalidOperationException("extension vector không tạo được")),
            CreateRecordingStep("Catalog", executedModules),
        ];
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await DatabaseMigrationCommand.RunStepsAsync(steps, output, error, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(["Identity"], executedModules);
        Assert.Equal("Migration AiGateway thất bại: extension vector không tạo được", error.ToString().Trim());
        Assert.DoesNotContain("Catalog", output.ToString());
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
