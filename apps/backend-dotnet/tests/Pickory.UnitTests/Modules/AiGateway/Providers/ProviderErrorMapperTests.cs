using System.Net.Sockets;
using System.Text.Json;
using Pickory.Modules.AiGateway.Providers;
using Pickory.Modules.AiGateway.Resilience;
using Xunit;

namespace Pickory.UnitTests.Modules.AiGateway.Providers;

// Port phần không phụ thuộc HTTP của `providers/error-mapper.test.ts`. Lỗi có mã HTTP được test qua adapter.
public sealed class ProviderErrorMapperTests
{
    [Fact]
    // Lỗi đã chuẩn hóa được giữ nguyên.
    public void Map_KeepsProviderException()
    {
        var original = new ProviderException(429, retryable: true);

        Assert.Same(original, ProviderErrorMapper.Map(original));
    }

    [Fact]
    // Lỗi mạng chưa có mã HTTP là sự cố tạm thời, nên được thử lại.
    public void Map_NetworkErrorsAreRetryable()
    {
        Assert.True(ProviderErrorMapper.Map(new HttpRequestException("fetch failed")).Retryable);
        Assert.True(ProviderErrorMapper.Map(new IOException("connection reset")).Retryable);
        Assert.True(ProviderErrorMapper.Map(new HttpRequestException("wrapped", new SocketException(10061))).Retryable);
    }

    [Fact]
    // Lỗi parse hoặc lỗi lập trình không có mã HTTP. Thử lại cũng không thành công.
    public void Map_ProgrammingErrorsAreNotRetryable()
    {
        var parse = ProviderErrorMapper.Map(new JsonException("Unexpected token"));
        var argument = ProviderErrorMapper.Map(new ArgumentException("bad"));

        Assert.Equal(((int?)null, false), (parse.Status, parse.Retryable));
        Assert.False(argument.Retryable);
    }

    [Fact]
    // Thông báo của exception chuẩn hóa không chứa thông báo gốc, nên không lộ khóa hoặc URL.
    public void Map_DropsOriginalMessage()
    {
        var mapped = ProviderErrorMapper.Map(new HttpRequestException("key secret_key_123 at https://api.deepseek.com"));

        Assert.DoesNotContain("secret_key_123", mapped.ToString());
        Assert.DoesNotContain("api.deepseek.com", mapped.ToString());
    }
}
