using Microsoft.Extensions.DependencyInjection;
using Pickory.Shared.Configuration;

namespace Pickory.Modules.Catalog.Images;

// Chọn kho ảnh theo cấu hình: Cloudinary thật khi có khóa, bản giả ở dev và test, lỗi khởi động ở production.
public static class ImageStorageRegistration
{
    private const string CloudinaryHttpClient = "catalog-cloudinary";
    private const string CloudinaryUploadHttpClient = "catalog-cloudinary-upload";

    public static IServiceCollection AddCatalogImageStorage(
        this IServiceCollection services, AppConfig config, bool isProduction)
    {
        EnsureConfigured(config, isProduction);

        if (config.HasCloudinary)
        {
            var credentials = new CloudinaryCredentials(
                config.CloudinaryCloudName, config.CloudinaryApiKey, config.CloudinaryApiSecret);
            services.AddSingleton(credentials);
            // Giới hạn thời gian gọi Cloudinary để request lưu sản phẩm không treo khi mạng lỗi.
            services.AddHttpClient(CloudinaryHttpClient, client => client.Timeout = TimeSpan.FromSeconds(15));
            services.AddSingleton<IImageStorage>(provider => new CloudinaryImageStorage(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient(CloudinaryHttpClient),
                credentials,
                provider.GetRequiredService<TimeProvider>()));
            // Cloudinary phải tự tải ảnh từ trang hãng nên thời gian chờ dài hơn lệnh đọc và xóa ảnh.
            services.AddHttpClient(CloudinaryUploadHttpClient, client => client.Timeout = TimeSpan.FromSeconds(120));
            services.AddSingleton<IImageUploader>(provider => new CloudinaryImageUploader(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient(CloudinaryUploadHttpClient),
                credentials,
                provider.GetRequiredService<TimeProvider>()));
            return services;
        }

        // Dev dùng kho giả dễ dãi, vì trình duyệt không tải được ảnh thật. Test tích hợp thay bằng bản nghiêm ngặt.
        services.AddSingleton(FakeImageStorage.Credentials);
        services.AddSingleton(new FakeImageStorage(acceptUnknownProductImages: true));
        services.AddSingleton<IImageStorage>(provider => provider.GetRequiredService<FakeImageStorage>());
        // Bộ tải giả không gọi mạng. Kho giả nhận mọi ảnh sản phẩm nên lệnh seed ở dev không cần tải gì.
        services.AddSingleton(provider => new FakeImageUploader(provider.GetRequiredService<FakeImageStorage>()));
        services.AddSingleton<IImageUploader>(provider => provider.GetRequiredService<FakeImageUploader>());
        return services;
    }

    // Production thiếu khóa thì dừng ngay khi khởi động, thay vì nhận ảnh rồi không xóa được.
    public static void EnsureConfigured(AppConfig config, bool isProduction)
    {
        if (isProduction && !config.HasCloudinary)
        {
            throw new InvalidOperationException(
                "Production cần CLOUDINARY_CLOUD_NAME, CLOUDINARY_API_KEY và CLOUDINARY_API_SECRET.");
        }
    }
}
