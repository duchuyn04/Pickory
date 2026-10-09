using Microsoft.AspNetCore.Http;
using Pickory.Modules.Identity.Actors;
using Pickory.Shared.Exceptions;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.Actors;

// Kiểm tra bước chống CSRF (Cross-Site Request Forgery): chỉ trang web của Pickory được gửi request ghi.
public class RequestOriginGuardTests
{
    private const string WebOrigin = "http://localhost:5173";
    // Thông điệp giữ nguyên bản Node vì frontend hiển thị trực tiếp.
    private const string MissingOrMismatchMessage = "Yêu cầu bị từ chối do thiếu hoặc Origin/Referer không hợp lệ.";

    [Fact]
    // Request đọc từ nguồn lạ được cho qua. Request đọc không đổi dữ liệu nên không cần chống CSRF.
    public void Get_WithForeignOrigin_IsAllowed()
    {
        var request = CreateRequest("GET", origin: "https://malicious-site.com");

        // Hàm không ném lỗi là kết quả mong đợi.
        RequestOriginGuard.EnsureTrustedOrigin(request, WebOrigin);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("post")]
    // Mọi phương thức ghi từ nguồn lạ bị chặn, kể cả khi tên phương thức viết thường.
    public void MutatingMethod_WithForeignOrigin_IsRejected(string method)
    {
        var request = CreateRequest(method, origin: "https://malicious-site.com");

        // Bước kiểm tra nguồn ném lỗi thay vì trả giá trị. Middleware lỗi đổi lỗi này thành 403.
        var exception = Assert.Throws<AppException>(() => RequestOriginGuard.EnsureTrustedOrigin(request, WebOrigin));

        // Mã, mã HTTP và thông điệp giống bản Node.
        Assert.Equal("FORBIDDEN_ORIGIN", exception.Code);
        Assert.Equal(403, exception.StatusCode);
        Assert.Equal(MissingOrMismatchMessage, exception.Message);
    }

    [Theory]
    [InlineData("http://localhost:5173")]
    [InlineData("http://LOCALHOST:5173")]
    [InlineData("http://localhost:5173/")]
    // Nguồn web được cho qua dù tên máy viết hoa hoặc có dấu gạch chéo cuối. Trình duyệt coi các dạng này là cùng nguồn.
    public void Post_WithWebOrigin_IsAllowed(string origin)
    {
        var request = CreateRequest("POST", origin: origin);

        // Hàm không ném lỗi là kết quả mong đợi.
        RequestOriginGuard.EnsureTrustedOrigin(request, WebOrigin);
    }

    [Fact]
    // Dấu gạch chéo cuối trong `WEB_ORIGIN` không làm request hợp lệ bị chặn.
    public void Post_WithWebOriginConfiguredWithTrailingSlash_IsAllowed()
    {
        var request = CreateRequest("POST", origin: "http://localhost:5173");

        // Truyền cấu hình có dấu gạch chéo cuối. Hàm không ném lỗi là kết quả mong đợi.
        RequestOriginGuard.EnsureTrustedOrigin(request, "http://localhost:5173/");
    }

    [Theory]
    [InlineData("https://localhost:5173")]
    [InlineData("http://localhost:5174")]
    [InlineData("ftp://localhost:5173")]
    // Khác scheme hoặc khác cổng là khác nguồn. Scheme lạ như `ftp` có nguồn "null" nên cũng không khớp.
    public void Post_WithDifferentSchemeOrPort_IsRejectedAsMismatch(string origin)
    {
        var request = CreateRequest("POST", origin: origin);

        var exception = Assert.Throws<AppException>(() => RequestOriginGuard.EnsureTrustedOrigin(request, WebOrigin));

        // Các trường hợp này nhận thông điệp không khớp, không nhận thông điệp nguồn hỏng.
        Assert.Equal(MissingOrMismatchMessage, exception.Message);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("null")]
    [InlineData("/relative-path")]
    // `Origin` không đọc được là dấu hiệu request giả mạo. Đường dẫn tương đối cũng bị coi là URL sai, giống bản Node.
    public void Post_WithUnreadableOrigin_IsRejectedAsInvalidOrigin(string origin)
    {
        var request = CreateRequest("POST", origin: origin);

        var exception = Assert.Throws<AppException>(() => RequestOriginGuard.EnsureTrustedOrigin(request, WebOrigin));

        // API từ chối ngay với thông điệp riêng cho `Origin` hỏng.
        Assert.Equal("FORBIDDEN_ORIGIN", exception.Code);
        Assert.Equal("Yêu cầu bị từ chối do Origin không hợp lệ.", exception.Message);
    }

    [Fact]
    // Thiếu `Origin` thì `Referer` từ trang web là nguồn thay thế. Một số trình duyệt không gửi `Origin`.
    public void Post_WithoutOriginButWithWebReferer_IsAllowed()
    {
        var request = CreateRequest("POST", referer: "http://localhost:5173/some-page");

        // Hàm không ném lỗi là kết quả mong đợi.
        RequestOriginGuard.EnsureTrustedOrigin(request, WebOrigin);
    }

    [Fact]
    // `Origin` chỉ có khoảng trắng được coi là thiếu. API chuyển sang đọc `Referer`.
    public void Post_WithBlankOriginFallsBackToReferer()
    {
        var request = CreateRequest("POST", origin: "   ", referer: "http://localhost:5173/cart");

        // Hàm không ném lỗi là kết quả mong đợi.
        RequestOriginGuard.EnsureTrustedOrigin(request, WebOrigin);
    }

    [Fact]
    // `Referer` không đọc được bị từ chối như `Origin` hỏng.
    public void Post_WithUnreadableReferer_IsRejectedAsInvalidReferer()
    {
        var request = CreateRequest("POST", referer: "not a url");

        var exception = Assert.Throws<AppException>(() => RequestOriginGuard.EnsureTrustedOrigin(request, WebOrigin));

        // Thông điệp nêu đúng header bị hỏng, giống bản Node.
        Assert.Equal("Yêu cầu bị từ chối do Referer không hợp lệ.", exception.Message);
    }

    [Fact]
    // Request ghi thiếu cả hai header bị chặn. API không chứng minh được request đến từ trang web.
    public void Post_WithoutOriginAndReferer_IsRejected()
    {
        var request = CreateRequest("POST");

        var exception = Assert.Throws<AppException>(() => RequestOriginGuard.EnsureTrustedOrigin(request, WebOrigin));

        // Trường hợp thiếu header dùng chung thông điệp với trường hợp không khớp.
        Assert.Equal(MissingOrMismatchMessage, exception.Message);
    }

    // Tạo request trong bộ nhớ. Test gọi bước kiểm tra nguồn mà không cần khởi động máy chủ.
    private static HttpRequest CreateRequest(string method, string? origin = null, string? referer = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        // Chỉ đặt header khi test truyền giá trị. Giá trị null mô phỏng trình duyệt không gửi header.
        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }

        if (referer is not null)
        {
            context.Request.Headers.Referer = referer;
        }

        return context.Request;
    }
}
