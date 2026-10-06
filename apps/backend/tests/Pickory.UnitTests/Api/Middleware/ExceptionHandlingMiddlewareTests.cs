using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pickory.Api.Middleware;
using Pickory.UnitTests.TestDoubles;
using Xunit;

namespace Pickory.UnitTests.Api.Middleware;

public sealed class ExceptionHandlingMiddlewareTests
{
    [Fact]
    // Client đóng kết nối khi request còn chạy. Đây không phải lỗi của API, nên log không có mức `Error`.
    public async Task InvokeAsync_ClientAbortsRequest_DoesNotLogError()
    {
        using var abort = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = abort.Token };
        var logger = new RecordingLogger<ExceptionHandlingMiddleware>();
        var middleware = new ExceptionHandlingMiddleware(
            _ =>
            {
                abort.Cancel();
                throw new OperationCanceledException(abort.Token);
            },
            logger);

        await middleware.InvokeAsync(context);

        Assert.DoesNotContain(LogLevel.Error, logger.RecordedLevels);
    }

    [Fact]
    // Lệnh bị hủy khi client vẫn chờ là lỗi thật của API. Middleware vẫn ghi `Error` và trả 500.
    public async Task InvokeAsync_CancellationWithoutClientAbort_LogsErrorAndReturns500()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var logger = new RecordingLogger<ExceptionHandlingMiddleware>();
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new OperationCanceledException(),
            logger);

        await middleware.InvokeAsync(context);

        Assert.Contains(LogLevel.Error, logger.RecordedLevels);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
    }
}
