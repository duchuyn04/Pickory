using Microsoft.AspNetCore.Http;
using Pickory.Modules.Identity.Sessions;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.Sessions;

public class SessionCookiesTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreateOptions_MatchesNodeCookieAttributes(bool isProduction)
    {
        var options = SessionCookies.CreateOptions(TimeSpan.FromDays(30), isProduction);

        Assert.True(options.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, options.SameSite);
        Assert.Equal("/", options.Path);
        Assert.Equal(TimeSpan.FromDays(30), options.MaxAge);
        // `Secure` chỉ bật ở production vì môi trường dev chạy HTTP.
        Assert.Equal(isProduction, options.Secure);
    }

    [Fact]
    public void Lifetimes_MatchNodeValues()
    {
        Assert.Equal(TimeSpan.FromDays(30), SessionCookies.CustomerSessionLifetime);
        Assert.Equal(TimeSpan.FromDays(365), SessionCookies.AnonymousIdLifetime);
        Assert.Equal("pk_session", SessionCookies.SessionCookieName);
        Assert.Equal("pk_anon", SessionCookies.AnonymousCookieName);
    }
}
