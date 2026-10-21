using Pickory.Modules.ActivityTracking.Features.CollectEvents;
using Xunit;

namespace Pickory.UnitTests.Modules.ActivityTracking.CollectEvents;

// ACT-06: nhận diện crawler bằng danh sách tên cụ thể. Crawler không phải khách thật nên không được tính vào lượt xem.
public sealed class CrawlerUserAgentsTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)")]
    [InlineData("Mozilla/5.0 (compatible; bingbot/2.0; +http://www.bing.com/bingbot.htm)")]
    [InlineData("facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)")]
    [InlineData("Mozilla/5.0 (compatible; AhrefsBot/7.0; +http://ahrefs.com/robot/)")]
    [InlineData("Mozilla/5.0 (compatible; SemrushBot/7~bl; +http://www.semrush.com/bot.html)")]
    [InlineData("Mozilla/5.0 (compatible; Baiduspider/2.0; +http://www.baidu.com/search/spider.html)")]
    [InlineData("Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko; compatible; GPTBot/1.1; +https://openai.com/gptbot)")]
    [InlineData("Twitterbot/1.0")]
    public void IsCrawler_KnownCrawlerUserAgent_ReturnsTrue(string userAgent)
    {
        Assert.True(CrawlerUserAgents.IsCrawler(userAgent));
    }

    [Theory]
    [InlineData("GOOGLEBOT/2.1")]
    [InlineData("googlebot/2.1")]
    [InlineData("Mozilla/5.0 (compatible; BINGBOT/2.0)")]
    [InlineData("FACEBOOKEXTERNALHIT/1.1")]
    public void IsCrawler_DifferentLetterCase_ReturnsTrue(string userAgent)
    {
        Assert.True(CrawlerUserAgents.IsCrawler(userAgent));
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_2 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Mobile/15E148 Safari/604.1")]
    // CUBOT là tên hãng điện thoại có chứa chữ `bot`. Danh sách không được dùng token chung này.
    [InlineData("Mozilla/5.0 (Linux; Android 10; CUBOT X30) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Mobile Safari/537.36")]
    public void IsCrawler_BrowserUserAgent_ReturnsFalse(string userAgent)
    {
        Assert.False(CrawlerUserAgents.IsCrawler(userAgent));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    // Thiếu user agent thì không có căn cứ coi là crawler, nên request vẫn được đếm.
    public void IsCrawler_MissingUserAgent_ReturnsFalse(string? userAgent)
    {
        Assert.False(CrawlerUserAgents.IsCrawler(userAgent));
    }
}
