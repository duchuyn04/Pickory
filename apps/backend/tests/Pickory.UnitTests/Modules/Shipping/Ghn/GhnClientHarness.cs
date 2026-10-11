using System.Net;
using Microsoft.Extensions.Logging;
using Pickory.Modules.Shipping.Ghn;
using Pickory.UnitTests.TestDoubles;

namespace Pickory.UnitTests.Modules.Shipping.Ghn;

// Dựng `GhnHttpClient` với handler giả. Timeout ngắn để test hết giờ không phải chờ 5 giây thật.
internal sealed class GhnClientHarness
{
    // Giá trị giả đủ đặc trưng để test tìm được nếu nó lọt vào log hoặc thông báo lỗi.
    public const string Token = "tok-SECRET-9f3a71c2";
    public const string ShopId = "4455667";
    public static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan ShortLeadTimeTimeout = TimeSpan.FromMilliseconds(100);

    public StubHttpMessageHandler Handler { get; } = new();
    public RecordingLogger<GhnHttpClient> Logger { get; } = new();
    public GhnHttpClient Client { get; }

    public GhnClientHarness()
    {
        var httpClient = new HttpClient(Handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://ghn.test"),
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
        Client = new GhnHttpClient(httpClient, Token, ShopId, Logger, ShortTimeout, ShortLeadTimeTimeout);
    }

    public void RespondOk(string dataJson)
    {
        Handler.EnqueueJson(HttpStatusCode.OK, $$"""{"code":200,"message":"Success","data":{{dataJson}}}""");
    }

    // Phản hồi lỗi của GHN: HTTP 4xx hoặc 5xx với thân có `code` và `message`.
    public void RespondError(HttpStatusCode status, string message = "Dữ liệu không hợp lệ")
    {
        Handler.EnqueueJson(status, $$"""{"code":{{(int)status}},"message":"{{message}}","data":null}""");
    }

    public int RequestCount => Handler.Requests.Count;
}
