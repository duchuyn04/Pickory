using Microsoft.Extensions.DependencyInjection;
using Pickory.Modules.Catalog.Images;
using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Images;

// Kho ảnh chọn theo môi trường. Production thiếu khóa phải dừng, dev và test phải dùng bản giả.
public sealed class ImageStorageRegistrationTests
{
    private static readonly AppConfig WithoutCloudinary = new() { DatabaseUrl = "postgresql://localhost/pickory" };

    private static readonly AppConfig WithCloudinary = WithoutCloudinary with
    {
        CloudinaryCloudName = "pickory-demo",
        CloudinaryApiKey = "123",
        CloudinaryApiSecret = "bi-mat",
    };

    [Fact]
    public void EnsureConfigured_ProductionWithoutKeys_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ImageStorageRegistration.EnsureConfigured(WithoutCloudinary, isProduction: true));

        Assert.Contains("CLOUDINARY_API_SECRET", exception.Message);
    }

    [Fact]
    // Không có khóa ở dev thì API vẫn chạy với kho ảnh giả, không gọi Cloudinary thật.
    public void AddCatalogImageStorage_DevelopmentWithoutKeys_UsesFakeStorage()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);

        services.AddCatalogImageStorage(WithoutCloudinary, isProduction: false);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<FakeImageStorage>(provider.GetRequiredService<IImageStorage>());
        Assert.Equal(FakeImageStorage.Credentials, provider.GetRequiredService<CloudinaryCredentials>());
    }

    [Fact]
    public void AddCatalogImageStorage_WithKeys_UsesCloudinary()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);

        services.AddCatalogImageStorage(WithCloudinary, isProduction: true);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<CloudinaryImageStorage>(provider.GetRequiredService<IImageStorage>());
        Assert.Equal("pickory-demo", provider.GetRequiredService<CloudinaryCredentials>().CloudName);
    }

    [Fact]
    // In bản ghi khóa ra log không được lộ secret.
    public void CloudinaryCredentials_ToString_HidesSecret()
    {
        var credentials = new CloudinaryCredentials("pickory-demo", "123", "bi-mat");

        Assert.DoesNotContain("bi-mat", credentials.ToString());
    }

    [Theory]
    [InlineData("pickory/products/abc123", true)]
    [InlineData("pickory/products/2026/ao-do_1", true)]
    [InlineData("pickory/products/", false)]
    [InlineData("pickory/brands/logo", false)]
    [InlineData("pickory/products/../brands/logo", false)]
    [InlineData("pickory/products/a?b", false)]
    // Chỉ ảnh trong thư mục sản phẩm được gắn vào sản phẩm.
    public void IsProductPublicId_AcceptsOnlyProductFolder(string publicId, bool expected)
    {
        Assert.Equal(expected, ImageUploadRules.IsProductPublicId(publicId));
    }
}
