using Microsoft.AspNetCore.Http;
using Pickory.Modules.Identity.Actors;
using Pickory.Shared.Exceptions;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.Actors;

public class RequestOriginGuardTests
{
    private const string WebOrigin = "http://localhost:5173";
    private const string MissingOrMismatchMessage = "Yêu cầu bị từ chối do thiếu hoặc Origin/Referer không hợp lệ.";

    [Fact]
    public void Get_WithForeignOrigin_IsAllowed()
    {
        var request = CreateRequest("GET", origin: "https://malicious-site.com");

        RequestOriginGuard.EnsureTrustedOrigin(request, WebOrigin);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("post")]
    public void MutatingMethod_WithForeignOrigin_IsRejected(string method)
    {
        var request = CreateRequest(method, origin: "https://malicious-site.com");

        var exception = Assert.Throws<AppException>(() => RequestOriginGuard.EnsureTrustedOrigin(request, WebOrigin));

        Assert.Equal("FORBIDDEN_ORIGIN", exception.Code);
        Assert.Equal(403, exception.StatusCode);
        Assert.Equal(MissingOrMismatchMessage, exception.Message);
    }

    [Theory]
    [InlineData("http://localhost:5173")]
    [InlineData("http://LOCALHOST:5173")]
    [InlineData("http://localhost:5173/")]
    public void Post_WithWebOrigin_IsAllowed(string origin)
    {
        var request = CreateRequest("POST", origin: origin);

        RequestOriginGuard.EnsureTrustedOrigin(request, WebOrigin);
    }

    [Fact]
    public void Post_WithWebOriginConfiguredWithTrailingSlash_IsAllowed()
    {
        var request = CreateRequest("POST", origin: "http://localhost:5173");

        RequestOriginGuard.EnsureTrustedOrigin(request, "http://localhost:5173/");
    }

    [Theory]
    [InlineData("https://localhost:5173")]
    [InlineData("http://localhost:5174")]
    [InlineData("ftp://localhost:5173")]
    public void Post_WithDifferentSchemeOrPort_IsRejectedAsMismatch(string origin)
    {
        var request = CreateRequest("POST", origin: origin);

        var exception = Assert.Throws<AppException>(() => RequestOriginGuard.EnsureTrustedOrigin(request, WebOrigin));

        Assert.Equal(MissingOrMismatchMessage, exception.Message);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("null")]
    [InlineData("/relative-path")]
    public void Post_WithUnreadableOrigin_IsRejectedAsInvalidOrigin(string origin)
    {
        var request = CreateRequest("POST", origin: origin);

        var exception = Assert.Throws<AppException>(() => RequestOriginGuard.EnsureTrustedOrigin(request, WebOrigin));

        Assert.Equal("FORBIDDEN_ORIGIN", exception.Code);
        Assert.Equal("Yêu cầu bị từ chối do Origin không hợp lệ.", exception.Message);
    }

    [Fact]
    public void Post_WithoutOriginButWithWebReferer_IsAllowed()
    {
        var request = CreateRequest("POST", referer: "http://localhost:5173/some-page");

        RequestOriginGuard.EnsureTrustedOrigin(request, WebOrigin);
    }

    [Fact]
    public void Post_WithBlankOriginFallsBackToReferer()
    {
        var request = CreateRequest("POST", origin: "   ", referer: "http://localhost:5173/cart");

        RequestOriginGuard.EnsureTrustedOrigin(request, WebOrigin);
    }

    [Fact]
    public void Post_WithUnreadableReferer_IsRejectedAsInvalidReferer()
    {
        var request = CreateRequest("POST", referer: "not a url");

        var exception = Assert.Throws<AppException>(() => RequestOriginGuard.EnsureTrustedOrigin(request, WebOrigin));

        Assert.Equal("Yêu cầu bị từ chối do Referer không hợp lệ.", exception.Message);
    }

    [Fact]
    public void Post_WithoutOriginAndReferer_IsRejected()
    {
        var request = CreateRequest("POST");

        var exception = Assert.Throws<AppException>(() => RequestOriginGuard.EnsureTrustedOrigin(request, WebOrigin));

        Assert.Equal(MissingOrMismatchMessage, exception.Message);
    }

    private static HttpRequest CreateRequest(string method, string? origin = null, string? referer = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
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
