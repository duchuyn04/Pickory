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
    // Dev và E2E không tải được ảnh thật, nên kho giả ở dev coi ảnh trong thư mục sản phẩm là JPG nhỏ hợp lệ.
    public async Task AddCatalogImageStorage_DevelopmentWithoutKeys_AcceptsUnknownProductImages()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);

        services.AddCatalogImageStorage(WithoutCloudinary, isProduction: false);
        using var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IImageStorage>();

        var image = await storage.FindAsync("pickory/products/chua-tai-that", CancellationToken.None);
        Assert.NotNull(image);
        Assert.Equal("jpg", image.Format);
        Assert.True(image.Bytes <= ImageUploadRules.MaxBytes);
        // Ảnh ngoài thư mục sản phẩm vẫn không tồn tại, kể cả ở chế độ dễ dãi.
        Assert.Null(await storage.FindAsync("pickory/brands/logo", CancellationToken.None));
    }

    [Fact]
    // Kho giả tạo bằng hàm khởi tạo mặc định là bản nghiêm ngặt mà test tích hợp dùng.
    public async Task FakeImageStorage_Default_RejectsUnknownImages()
    {
        var storage = new FakeImageStorage();

        Assert.Null(await storage.FindAsync("pickory/products/chua-tai", CancellationToken.None));
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
    public void AddCatalogImageStorage_WithKeys_UsesCloudinaryUploader()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);

        services.AddCatalogImageStorage(WithCloudinary, isProduction: false);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<CloudinaryImageUploader>(provider.GetRequiredService<IImageUploader>());
    }

    [Fact]
    // Không có khóa thì bộ tải là bản giả, nên lệnh seed ở dev không gọi mạng.
    public void AddCatalogImageStorage_WithoutKeys_UsesFakeUploader()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);

        services.AddCatalogImageStorage(WithoutCloudinary, isProduction: false);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<FakeImageUploader>(provider.GetRequiredService<IImageUploader>());
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
