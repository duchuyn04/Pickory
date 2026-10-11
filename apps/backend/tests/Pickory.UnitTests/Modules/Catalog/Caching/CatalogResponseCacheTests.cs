using Pickory.Modules.Catalog.Caching;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Caching;

// Cache phản hồi công khai của Catalog: khóa theo query chuẩn hóa và phiên bản, xóa bằng cách tăng phiên bản.
public sealed class CatalogResponseCacheTests
{
    private sealed class Counter
    {
        public int Calls { get; set; }
    }

    private static CatalogResponseCache Enabled() => new(TimeSpan.FromSeconds(60));

    private static Func<Task<string>> Counting(Counter counter, string value = "gia-tri") => () =>
    {
        counter.Calls++;
        return Task.FromResult(value);
    };

    [Fact]
    public async Task Disabled_AlwaysRunsTheFactory()
    {
        using var cache = new CatalogResponseCache(TimeSpan.Zero);
        var counter = new Counter();

        await cache.GetOrCreateAsync("khoa", Counting(counter));
        await cache.GetOrCreateAsync("khoa", Counting(counter));

        Assert.False(cache.Enabled);
        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task SameKey_RunsTheFactoryOnce()
    {
        using var cache = Enabled();
        var counter = new Counter();

        var first = await cache.GetOrCreateAsync("khoa", Counting(counter));
        var second = await cache.GetOrCreateAsync("khoa", Counting(counter));

        Assert.Equal(first, second);
        Assert.Equal(1, counter.Calls);
    }

    [Fact]
    public async Task DifferentKeys_RunTheFactoryForEach()
    {
        using var cache = Enabled();
        var counter = new Counter();

        await cache.GetOrCreateAsync("a", Counting(counter));
        await cache.GetOrCreateAsync("b", Counting(counter));

        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task Invalidate_ForcesTheNextReadToRunTheFactory()
    {
        using var cache = Enabled();
        var counter = new Counter();
        await cache.GetOrCreateAsync("khoa", Counting(counter));

        cache.Invalidate();
        await cache.GetOrCreateAsync("khoa", Counting(counter));

        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task FactoryThatThrows_IsNotCached()
    {
        using var cache = Enabled();
        var counter = new Counter();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrCreateAsync<string>("khoa", () =>
            {
                counter.Calls++;
                throw new InvalidOperationException("loi");
            }));
        }

        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    // Dữ liệu đọc trước khi xóa cache có thể đã cũ. Kết quả đó không được ghi vào cache sau khi xóa.
    public async Task InvalidateWhileTheFactoryRuns_DoesNotStoreTheStaleResult()
    {
        using var cache = Enabled();
        var counter = new Counter();

        await cache.GetOrCreateAsync("khoa", () =>
        {
            counter.Calls++;
            cache.Invalidate();
            return Task.FromResult("cu");
        });
        var after = await cache.GetOrCreateAsync("khoa", Counting(counter, "moi"));

        Assert.Equal("moi", after);
        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public void NormalizeQuery_IgnoresParameterOrder()
    {
        var first = CatalogResponseCache.NormalizeQuery(new Dictionary<string, string>
        {
            ["category"] = "laptop", ["sort"] = "newest",
        });
        var second = CatalogResponseCache.NormalizeQuery(new Dictionary<string, string>
        {
            ["sort"] = "newest", ["category"] = "laptop",
        });

        Assert.Equal(first, second);
    }

    [Fact]
    public void NormalizeQuery_KeepsDifferentValuesApart()
    {
        var first = CatalogResponseCache.NormalizeQuery(new Dictionary<string, string> { ["page"] = "1" });
        var second = CatalogResponseCache.NormalizeQuery(new Dictionary<string, string> { ["page"] = "2" });

        Assert.NotEqual(first, second);
    }

    [Fact]
    // Giá trị chứa `&` hoặc `=` không được làm hai query khác nhau trùng khóa.
    public void NormalizeQuery_EscapesSeparatorsInValues()
    {
        var first = CatalogResponseCache.NormalizeQuery(new Dictionary<string, string> { ["a"] = "1&b=2" });
        var second = CatalogResponseCache.NormalizeQuery(new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });

        Assert.NotEqual(first, second);
    }
}
