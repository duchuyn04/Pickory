using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pickory.Shared.Http;
using Xunit;

namespace Pickory.UnitTests.Shared.Http;

// `TRUST_PROXY` giữ nghĩa của `trust proxy` trong Express ở bản Node.
public class TrustProxyOptionsTests
{
    [Fact]
    // `TRUST_PROXY=false` không tin proxy nào. Client tự ghi `X-Forwarded-For` cũng không đổi được địa chỉ IP (Internet Protocol).
    public void False_IgnoresForwardedForHeader()
    {
        // Giá trị null nghĩa là API không bật middleware đọc `X-Forwarded-For`.
        Assert.Null(TrustProxyOptions.Create("false"));
    }

    [Fact]
    // Số hop tin đúng chừng ấy proxy tính từ phía máy chủ, giống `trust proxy` dạng số của Express.
    public async Task HopCount_TrustsThatManyProxiesFromTheRight()
    {
        var options = TrustProxyOptions.Create("1")!;

        // Địa chỉ đầu header có thể do client tự ghi. Địa chỉ cuối do proxy tin cậy ghi.
        var clientIp = await ResolveClientIpAsync(options, remoteIp: "10.0.0.1", forwardedFor: "198.51.100.7, 203.0.113.5");

        // Một hop tin cậy là proxy gần nhất. Proxy này báo client là địa chỉ cuối cùng trong header.
        Assert.Equal("203.0.113.5", clientIp);
    }

    [Fact]
    // Danh sách địa chỉ chỉ tin proxy có trong danh sách, gồm cả địa chỉ đơn và dải mạng.
    public async Task AddressList_TrustsOnlyListedProxies()
    {
        var options = TrustProxyOptions.Create("10.0.0.1, 192.168.0.0/16")!;

        // Gửi qua một proxy trong danh sách, một máy lạ và một máy trong dải mạng tin cậy.
        var throughKnownProxy = await ResolveClientIpAsync(options, remoteIp: "10.0.0.1", forwardedFor: "203.0.113.5");
        var throughUnknownProxy = await ResolveClientIpAsync(options, remoteIp: "172.16.0.9", forwardedFor: "203.0.113.5");
        var throughKnownNetwork = await ResolveClientIpAsync(options, remoteIp: "192.168.4.2", forwardedFor: "203.0.113.6");

        // Proxy trong danh sách được tin, nên API lấy IP client trong header.
        Assert.Equal("203.0.113.5", throughKnownProxy);
        // Máy lạ có thể tự ghi header giả. API giữ địa chỉ của chính máy đó.
        Assert.Equal("172.16.0.9", throughUnknownProxy);
        // Máy trong dải `192.168.0.0/16` cũng được tin như proxy.
        Assert.Equal("203.0.113.6", throughKnownNetwork);
    }

    [Fact]
    // Địa chỉ sai làm lỗi lúc khởi động. Cấu hình sai lặng lẽ làm giới hạn tần suất đếm nhầm IP.
    public void InvalidAddress_FailsAtStartup()
    {
        // Hàm ném lỗi thay vì bỏ qua giá trị sai.
        Assert.Throws<InvalidOperationException>(() => TrustProxyOptions.Create("khong-phai-ip"));
    }

    // Chạy middleware thật của ASP.NET Core. Test kiểm tra IP mà route nhận được, không chỉ kiểm tra giá trị cấu hình.
    private static async Task<string?> ResolveClientIpAsync(ForwardedHeadersOptions options, string remoteIp, string forwardedFor)
    {
        // `remoteIp` là máy gửi request trực tiếp. `forwardedFor` là header do máy đó gửi kèm.
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options));

        await middleware.Invoke(context);

        // Middleware ghi IP client vào `RemoteIpAddress` khi tin proxy. Không tin thì giá trị giữ nguyên.
        return context.Connection.RemoteIpAddress?.ToString();
    }
}
