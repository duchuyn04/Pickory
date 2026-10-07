using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pickory.Modules.Catalog.Features.Brands;
using Pickory.Modules.Catalog.Features.Categories;
using Pickory.Modules.Catalog.Features.Products;
using Pickory.Modules.Catalog.Features.Uploads;
using Pickory.Modules.Catalog.Images;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Shared.Configuration;

namespace Pickory.Modules.Catalog;

// Điểm đăng ký duy nhất của module Catalog. `Program.cs` không cần biết `DbContext` hoặc dịch vụ bên trong module.
public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(
        this IServiceCollection services, string databaseConnectionString, AppConfig config, bool isProduction)
    {
        // Dùng cùng tùy chọn với migration để ứng dụng đọc đúng enum và lịch sử. Ứng dụng không tự chạy migration khi khởi động.
        services.AddDbContext<CatalogDbContext>(options =>
            CatalogDbContext.ConfigureOptions(options, databaseConnectionString));
        // Đăng ký đồng hồ thời gian hệ thống và các dịch vụ quản lý danh mục, thương hiệu, sản phẩm.
        services.TryAddSingleton(TimeProvider.System);
        services.AddValidatorsFromAssemblyContaining<CreateCategoryRequestValidator>();
        services.AddScoped<CategoryManagementHandler>();
        services.AddScoped<BrandManagementHandler>();
        services.AddScoped<ProductManagementHandler>();
        services.AddScoped<ProductImageSync>();
        // Kho ảnh chọn theo khóa Cloudinary. Worker chạy nền xóa ảnh mà sản phẩm đã bỏ.
        services.AddCatalogImageStorage(config, isProduction);
        services.AddScoped<ImageDeletionProcessor>();
        services.AddHostedService<ImageDeletionWorker>();
        return services;
    }

    // Dựng hoặc cập nhật schema của module bằng migration EF Core.
    // API không tự chạy migration khi khởi động, nên chỉ lệnh chuẩn bị DB gọi hàm này.
    public static async Task MigrateCatalogDatabaseAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        CategoriesEndpoints.Map(endpoints);
        BrandsEndpoints.Map(endpoints);
        ProductsEndpoints.Map(endpoints);
        UploadSignatureEndpoint.Map(endpoints);
        return endpoints;
    }
}
