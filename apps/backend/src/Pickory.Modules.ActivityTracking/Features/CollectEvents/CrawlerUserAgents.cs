namespace Pickory.Modules.ActivityTracking.Features.CollectEvents;

// Nhận diện crawler theo danh sách tên cụ thể. Lượt xem của crawler không phản ánh khách mua hàng, nên không được đếm.
internal static class CrawlerUserAgents
{
    // Danh sách không có token chung như `bot`, `spider` hay `crawl`. Điện thoại CUBOT có chữ `bot` trong user agent và vẫn là khách thật.
    private static readonly string[] CrawlerNames =
    [
        "Googlebot",
        "bingbot",
        "Slurp",
        "DuckDuckBot",
        "Baiduspider",
        "YandexBot",
        "Applebot",
        "facebookexternalhit",
        "Twitterbot",
        "LinkedInBot",
        "AhrefsBot",
        "SemrushBot",
        "MJ12bot",
        "PetalBot",
        "GPTBot",
        "CCBot",
        "Bytespider",
    ];

    public static bool IsCrawler(string? userAgent)
    {
        // Thiếu user agent thì không có căn cứ coi là crawler. Request vẫn được đếm.
        if (string.IsNullOrEmpty(userAgent))
        {
            return false;
        }

        // Crawler viết hoa thường không đồng nhất giữa các phiên bản, nên so chuỗi con không phân biệt hoa thường.
        foreach (var crawlerName in CrawlerNames)
        {
            if (userAgent.Contains(crawlerName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
