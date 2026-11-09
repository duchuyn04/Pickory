using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// Hàm kiểm chung cho test route quản trị của support-desk. Mỗi test không phải tự đọc lại cấu trúc lỗi.
internal static class SupportDeskHttpAssertions
{
    // Đòi response là 400 `VALIDATION_ERROR` và danh sách chi tiết lỗi có đủ các khóa mong muốn.
    public static async Task AssertValidationErrorAsync(HttpResponseMessage response, params string[] expectedKeys)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var error = document!.RootElement.GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        var keys = error.GetProperty("details").EnumerateArray()
            .Select(detail => detail.GetProperty("key").GetString())
            .ToArray();
        foreach (var expectedKey in expectedKeys)
        {
            Assert.Contains(expectedKey, keys);
        }
    }
}
