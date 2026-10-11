using Microsoft.Extensions.Caching.Memory;

namespace Pickory.Modules.Catalog.Caching;

// Cache ngắn trong bộ nhớ cho phản hồi công khai của Catalog. Cache theo từng instance của API.
// Khóa gồm phiên bản. Xóa cache bằng cách tăng phiên bản, nên request đang đọc dữ liệu cũ không ghi đè dữ liệu mới.
public sealed class CatalogResponseCache : IDisposable
{
    // Số mục tối đa. Query lạ (tên bộ lọc, trang, sắp xếp) không được làm phình bộ nhớ.
    // Mỗi mục là một trang tối đa 48 sản phẩm, khoảng vài chục KB, nên 200 mục nằm trong vài chục MB.
    public const int DefaultMaxEntries = 200;

    private readonly MemoryCache? _cache;
    private readonly TimeSpan _lifetime;
    private long _version;

    public CatalogResponseCache(TimeSpan lifetime, int maxEntries = DefaultMaxEntries)
    {
        _lifetime = lifetime;
        // Thời hạn bằng 0 nghĩa là tắt cache. Không dựng bộ nhớ đệm nào để dev và test giữ hành vi cũ.
        if (lifetime > TimeSpan.Zero)
        {
            // Mỗi mục có kích thước 1, nên `SizeLimit` là số mục tối đa.
            _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = maxEntries });
        }
    }

    public bool Enabled => _cache is not null;

    // Chuẩn hóa tham số query thành một khóa. Cùng tham số khác thứ tự cho cùng khóa.
    public static string NormalizeQuery(IReadOnlyDictionary<string, string> parameters)
    {
        // Escape tên và giá trị để `&` hoặc `=` trong giá trị không làm hai query khác nhau trùng khóa.
        return string.Join('&', parameters
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    }

    // Trả kết quả đã cache, hoặc chạy `factory` và cache kết quả. Lỗi của `factory` không bao giờ được cache.
    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory)
        where T : class
    {
        if (_cache is null)
        {
            return await factory();
        }

        // Lấy phiên bản trước khi đọc DB. Dữ liệu đọc ở phiên bản cũ chỉ được ghi vào khóa của phiên bản cũ.
        var version = Interlocked.Read(ref _version);
        var versionedKey = $"{version}|{key}";
        if (_cache.TryGetValue(versionedKey, out T? cached) && cached is not null)
        {
            return cached;
        }

        var value = await factory();
        // Cache đã bị xóa trong lúc đọc thì kết quả có thể cũ. Trả cho request này nhưng không lưu.
        if (Interlocked.Read(ref _version) == version)
        {
            _cache.Set(versionedKey, value, new MemoryCacheEntryOptions
            {
                Size = 1,
                AbsoluteExpirationRelativeToNow = _lifetime,
            });
        }

        return value;
    }

    // Gọi sau khi transaction ghi đã commit. Xóa trước commit thì request song song có thể nạp lại dữ liệu cũ.
    public void Invalidate()
    {
        if (_cache is null)
        {
            return;
        }

        // Tăng phiên bản trước để request đang chạy không ghi vào phiên bản mới. Sau đó giải phóng mục cũ.
        Interlocked.Increment(ref _version);
        _cache.Clear();
    }

    public void Dispose()
    {
        _cache?.Dispose();
    }
}
