using Pickory.Api;
using Xunit;

namespace Pickory.UnitTests.Api;

// Chế độ tài liệu quyết định người đọc có gửi được request từ Swagger UI hay không.
public sealed class ApiDocumentationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    // Development luôn có "Try it out", dù cờ production bật hay tắt.
    public void SelectMode_InDevelopment_ReturnsInteractive(bool apiDocsEnabled)
    {
        Assert.Equal(ApiDocumentationMode.Interactive, ApiDocumentation.SelectMode(isDevelopment: true, apiDocsEnabled));
    }

    [Fact]
    // Môi trường khác chỉ có tài liệu khi bật cờ. Tài liệu đó ở chế độ chỉ xem.
    public void SelectMode_OutsideDevelopmentWithFlag_ReturnsReadOnly()
    {
        Assert.Equal(ApiDocumentationMode.ReadOnly, ApiDocumentation.SelectMode(isDevelopment: false, apiDocsEnabled: true));
    }

    [Fact]
    // Không bật cờ thì môi trường khác không gắn tài liệu. Danh sách route admin không bị lộ.
    public void SelectMode_OutsideDevelopmentWithoutFlag_ReturnsNull()
    {
        Assert.Null(ApiDocumentation.SelectMode(isDevelopment: false, apiDocsEnabled: false));
    }
}
