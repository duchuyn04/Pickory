using Microsoft.AspNetCore.Http;
using Pickory.Modules.Identity.Sessions;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.Sessions;

// Kiểm tra tên, thời hạn và thuộc tính cookie giữ nguyên bản Node. Frontend và phiên cũ dựa vào các giá trị này.
public class SessionCookiesTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    // Cookie phiên có thuộc tính bảo mật giống bản Node ở cả dev và production.
    public void CreateOptions_MatchesNodeCookieAttributes(bool isProduction)
    {
        var options = SessionCookies.CreateOptions(TimeSpan.FromDays(30), isProduction);

        // `HttpOnly` chặn script đọc token. `SameSite=Lax` chặn trang khác gửi cookie trong request ghi.
        Assert.True(options.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, options.SameSite);
        // Cookie dùng cho mọi đường dẫn và có hạn bằng thời hạn được truyền vào.
        Assert.Equal("/", options.Path);
        Assert.Equal(TimeSpan.FromDays(30), options.MaxAge);
        // `Secure` chỉ bật ở production vì môi trường dev chạy HTTP.
        Assert.Equal(isProduction, options.Secure);
    }

    [Fact]
    // Thời hạn và tên cookie giống bản Node. Trình duyệt vẫn gửi cookie cũ sau khi đổi sang bản .NET.
    public void Lifetimes_MatchNodeValues()
    {
        // Phiên khách hàng sống 30 ngày. Mã ẩn danh sống 1 năm.
        Assert.Equal(TimeSpan.FromDays(30), SessionCookies.CustomerSessionLifetime);
        Assert.Equal(TimeSpan.FromDays(365), SessionCookies.AnonymousIdLifetime);
        // Đổi tên cookie sẽ làm mọi người dùng mất phiên và mất giỏ hàng.
        Assert.Equal("pk_session", SessionCookies.SessionCookieName);
        Assert.Equal("pk_anon", SessionCookies.AnonymousCookieName);
    }
}
