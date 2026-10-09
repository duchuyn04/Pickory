using Microsoft.Extensions.Configuration;
using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.UnitTests.Shared.Configuration;

// Khóa Cloudinary đi theo bộ ba. Thiếu một phần thì tải ảnh lên được nhưng không xóa được, nên phải dừng ngay khi khởi động.
public sealed class AppConfigCloudinaryTests
{
    private static AppConfig Load(params (string Key, string Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://pickory:pickory@localhost:5432/pickory",
        };
        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return AppConfig.Load(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }

    [Fact]
    // Dev và test không có khóa thì module dùng kho ảnh giả.
    public void Load_WithoutCloudinaryKeys_ReportsCloudinaryNotConfigured()
    {
        var config = Load();

        Assert.False(config.HasCloudinary);
        Assert.Equal(string.Empty, config.CloudinaryCloudName);
    }

    [Fact]
    public void Load_WithAllCloudinaryKeys_ReadsTrimmedValues()
    {
        var config = Load(
            ("CLOUDINARY_CLOUD_NAME", " pickory-demo "),
            ("CLOUDINARY_API_KEY", "123456789"),
            ("CLOUDINARY_API_SECRET", "bi-mat"));

        Assert.True(config.HasCloudinary);
        Assert.Equal("pickory-demo", config.CloudinaryCloudName);
        Assert.Equal("123456789", config.CloudinaryApiKey);
        Assert.Equal("bi-mat", config.CloudinaryApiSecret);
    }

    [Theory]
    [InlineData("CLOUDINARY_CLOUD_NAME")]
    [InlineData("CLOUDINARY_API_KEY")]
    [InlineData("CLOUDINARY_API_SECRET")]
    // Thiếu một khóa thì dừng khởi động. Thông báo không chứa giá trị secret để secret không lọt vào log.
    public void Load_WithPartialCloudinaryKeys_Throws(string missingKey)
    {
        var keys = new[] { "CLOUDINARY_CLOUD_NAME", "CLOUDINARY_API_KEY", "CLOUDINARY_API_SECRET" };
        var provided = keys.Where(key => key != missingKey).Select(key => (key, "gia-tri-bi-mat")).ToArray();

        var exception = Assert.Throws<InvalidOperationException>(() => Load(provided));

        Assert.Contains("CLOUDINARY", exception.Message);
        Assert.DoesNotContain("gia-tri-bi-mat", exception.Message);
    }
}
