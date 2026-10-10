using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pickory.Modules.Shipping;
using Pickory.Modules.Shipping.Ghn;
using Pickory.Shared.Configuration;
using Pickory.Shared.Exceptions;
using Pickory.UnitTests.TestDoubles;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping.Ghn;

// SHIP-03 lát 4: giá trị `GHN_TOKEN` không xuất hiện trong log, thông báo lỗi hay stack trace, ở cả nhánh thành công và nhánh lỗi.
public sealed class GhnTokenLeakTests
{
    private const string Token = GhnClientHarness.Token;

    [Fact]
    // Chạy các hàm qua mọi nhánh lỗi đã biết. Sau đó tìm token trong mọi dòng log của adapter và mọi exception.
    public async Task Adapter_NeverPutsTokenInLogsOrExceptions()
    {
        var harness = new GhnClientHarness();
        var exceptionTexts = new List<string>();

        async Task Capture(Func<Task> call)
        {
            try
            {
                await call();
            }
            catch (AppException exception)
            {
                exceptionTexts.Add(exception.ToString());
            }
        }

        // Nhánh thành công của từng hàm.
        harness.RespondOk("[]");
        await harness.Client.GetProvincesAsync(CancellationToken.None);
        // Nhánh lỗi: GHN trả thông báo có lặp lại token. Adapter phải che trước khi nối vào lỗi và log.
        harness.RespondError(HttpStatusCode.BadRequest, "Token " + Token + " không hợp lệ");
        await Capture(() => harness.Client.GetProvincesAsync(CancellationToken.None));
        harness.Handler.EnqueueJson(HttpStatusCode.OK, "{\"code\":400,\"message\":\"Sai " + Token + "\",\"data\":null}");
        await Capture(() => harness.Client.GetWardsAsync("1", 0, 10, CancellationToken.None));
        harness.RespondOk("[{\"order_code\":\"GHN1\",\"result\":false,\"message\":\"Từ chối " + Token + "\"}]");
        await Capture(() => harness.Client.CancelOrderAsync("GHN1", GhnCancelReason.Other, null, CancellationToken.None));
        harness.RespondError(HttpStatusCode.Unauthorized, "Sai token " + Token);
        await Capture(() => harness.Client.GetOrderDetailAsync("GHN1", CancellationToken.None));
        harness.RespondError(HttpStatusCode.InternalServerError, Token);
        harness.RespondError(HttpStatusCode.InternalServerError, Token);
        await Capture(() => harness.Client.CalculateFeeAsync(new GhnFeeRequest(1, 1, 1, 1, 2, 0, 0, "1"), CancellationToken.None));
        harness.Handler.EnqueueException(new HttpRequestException("Mất kết nối tới máy chủ"));
        await Capture(() => harness.Client.CreateOrderAsync(
            new GhnCreateOrderRequest("A", "0", "a", "P", "W", "ORD", 1, 1, 1, 1, 2, 1, 0, 0, "c"), CancellationToken.None));
        harness.Handler.EnqueueJson(HttpStatusCode.OK, "<html>" + Token + "</html>");
        await Capture(() => harness.Client.GetProvincesAsync(CancellationToken.None));

        Assert.NotEmpty(harness.Logger.RecordedMessages);
        Assert.NotEmpty(exceptionTexts);
        Assert.DoesNotContain(harness.Logger.RecordedMessages, message => message.Contains(Token, StringComparison.Ordinal));
        Assert.DoesNotContain(exceptionTexts, text => text.Contains(Token, StringComparison.Ordinal));
    }

    [Fact]
    // Thông báo của GHN bị che nhưng vẫn giữ phần còn lại, để người dùng và người vận hành thấy lý do từ chối.
    public async Task RejectedMessage_MasksTokenButKeepsRestOfCarrierMessage()
    {
        var harness = new GhnClientHarness();
        harness.RespondError(HttpStatusCode.BadRequest, "Token " + Token + " hết hạn");

        var exception = await Assert.ThrowsAsync<Pickory.Modules.Shipping.Contracts.Errors.ShippingRejectedException>(
            () => harness.Client.GetProvincesAsync(CancellationToken.None));

        Assert.Equal("Đơn vị vận chuyển từ chối yêu cầu: Token *** hết hạn", exception.Message);
    }

    [Fact]
    // Dựng qua DI thật với `IHttpClientFactory` và log mức Trace. Log của chính `HttpClient` có thể in header nếu không che.
    public async Task RealDi_WithTraceLogging_NeverLogsTokenValue()
    {
        var capture = new CapturingLoggerProvider();
        var handler = new StubHttpMessageHandler();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(capture));
        services.AddShippingModule(
            "Host=localhost;Database=unused;Username=unused;Password=unused",
            new AppConfig { GhnToken = Token, GhnShopId = GhnClientHarness.ShopId, GhnApiBase = "https://ghn.test" },
            isProduction: false);
        // Cấu hình thêm cho đúng tên `HttpClient` của adapter: thay đường mạng thật bằng handler giả.
        services.AddHttpClient(GhnRegistration.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IGhnClient>();

        Assert.IsType<GhnHttpClient>(client);

        // Nhánh thành công.
        handler.EnqueueJson(HttpStatusCode.OK, """{"code":200,"message":"Success","data":[]}""");
        await client.GetProvincesAsync(CancellationToken.None);
        // Nhánh lỗi.
        handler.EnqueueJson(HttpStatusCode.BadRequest, "{\"code\":400,\"message\":\"Sai " + Token + "\",\"data\":null}");
        await Assert.ThrowsAsync<Pickory.Modules.Shipping.Contracts.Errors.ShippingRejectedException>(
            () => client.GetProvincesAsync(CancellationToken.None));

        // Phải có log của HttpClient (chứng minh test đang quan sát đúng chỗ), và không dòng nào chứa token.
        Assert.Contains(capture.Entries, entry => entry.Category.StartsWith("System.Net.Http.HttpClient.", StringComparison.Ordinal));
        Assert.DoesNotContain(capture.Entries, entry => entry.Text.Contains(Token, StringComparison.Ordinal));
        // Header vẫn đến được GHN: che chỉ áp dụng cho log, không áp dụng cho request thật.
        Assert.Equal(Token, handler.Requests[0].Headers!["Token"]);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<(string Category, string Text)> _entries = [];

        public IReadOnlyList<(string Category, string Text)> Entries
        {
            get
            {
                lock (_entries)
                {
                    return _entries.ToList();
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, List<(string Category, string Text)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                // Ghi cả exception, vì bí mật có thể nằm trong exception chứ không chỉ trong câu log.
                var text = $"{formatter(state, exception)} {exception}";
                lock (entries)
                {
                    entries.Add((category, text));
                }
            }
        }
    }
}
