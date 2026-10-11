using Npgsql;
using Pickory.Shared.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pickory.Modules.Catalog.Caching;
using Pickory.Modules.Catalog.Contracts.Events;
using Pickory.Modules.Catalog.Features.Brands;
using Pickory.Modules.Catalog.Features.Categories;
using Pickory.Modules.Catalog.Features.Inventory;
using Pickory.Modules.Catalog.Features.Products;
using Pickory.Modules.Catalog.Features.Storefront;
using Pickory.Modules.Catalog.Features.Uploads;
using Pickory.Modules.Catalog.Images;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Modules.Catalog.Reading;
using Pickory.Modules.Catalog.Seeding;
using Pickory.Modules.Catalog.Stock;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Catalog.Contracts.Stock;
using Pickory.Shared.Configuration;
using Pickory.Shared.Events;

namespace Pickory.Modules.Catalog;

// Điểm đăng ký duy nhất của module Catalog. `Program.cs` không cần biết `DbContext` hoặc dịch vụ bên trong module.
public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(
        this IServiceCollection services, string databaseConnectionString, AppConfig config, bool isProduction)
    {
        // Dùng cùng tùy chọn với migration để ứng dụng đọc đúng enum và lịch sử. Ứng dụng không tự chạy migration khi khởi động.
        // Enum của module vào data source chung. Mọi module chia nhau một pool kết nối, nên `DATABASE_POOL_MAX` áp cho cả tiến trình.
        services.AddSharedDataSource(databaseConnectionString, CatalogDbContext.MapEnums);
        services.AddDbContext<CatalogDbContext>((provider, options) =>
            CatalogDbContext.ConfigureOptions(options, provider.GetRequiredService<NpgsqlDataSource>()));
        // Đăng ký đồng hồ thời gian hệ thống và các dịch vụ quản lý danh mục, thương hiệu, sản phẩm.
        services.TryAddSingleton(TimeProvider.System);
        services.AddValidatorsFromAssemblyContaining<CreateCategoryRequestValidator>();
        // Cache phản hồi công khai của hai route danh sách. Singleton qua factory để container giải phóng khi tắt.
        services.AddSingleton(_ => new CatalogResponseCache(TimeSpan.FromSeconds(config.CatalogCacheSeconds)));
        // Sản phẩm và tồn kho phát sự kiện sau commit. Handler xóa cache ở đúng thời điểm đó.
        services.AddScoped<IDomainEventHandler<ProductChangedEvent>, CatalogCacheInvalidationHandler>();
        services.AddScoped<IDomainEventHandler<StockChangedEvent>, CatalogCacheInvalidationHandler>();
        services.AddScoped<CategoryManagementHandler>();
        services.AddScoped<BrandManagementHandler>();
        services.AddScoped<ProductManagementHandler>();
        services.AddScoped<ProductImageSync>();
        // Danh sách sản phẩm công khai: một dịch vụ tìm kiếm dùng chung cho route và module khác về sau.
        services.AddScoped<ProductSearchService>();
        // Chi tiết, tóm tắt và so sánh sản phẩm cho khách. `CatalogReader` ghép các dịch vụ này cho module khác.
        services.AddScoped<ProductDetailService>();
        services.AddScoped<ProductSummaryReader>();
        services.AddScoped<ProductComparisonService>();
        services.AddScoped<ICatalogReader, CatalogReader>();
        // Template trang web chỉ đọc khi có cấu hình. Đọc một lần vì bản build của web không đổi khi API đang chạy.
        if (config.WebIndexHtmlPath.Length > 0)
        {
            services.AddSingleton(WebPageTemplate.Load(config.WebIndexHtmlPath, config.WebOrigin));
        }

        // Tồn kho: handler cho API quản trị, và `IStockService` cho module `commerce`.
        services.AddScoped<InventoryHandler>();
        services.AddScoped<IStockService, StockService>();
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

    // Lệnh `catalog:seed` ghi dữ liệu mẫu. Lệnh `catalog:benchmark` do `Pickory.Api` chạy vì cần khởi động web API.
    public static bool IsCatalogSeedCommand(IReadOnlyList<string> args) => CatalogCommandLine.IsSeedCommand(args);

    public static bool IsCatalogBenchmarkCommand(IReadOnlyList<string> args) => CatalogCommandLine.IsBenchmarkCommand(args);

    public static Task<int> RunCatalogSeedCommandAsync(IServiceProvider services, bool isProduction) =>
        CatalogCommandLine.RunSeedAsync(services, isProduction, Console.Out, Console.Error, CancellationToken.None);

    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Nhóm không thêm tiền tố. Nhóm chỉ gắn tag `Catalog` cho mọi route của module trong tài liệu OpenAPI.
        var moduleRoutes = endpoints.MapGroup(string.Empty).WithTags("Catalog");
        CategoriesEndpoints.Map(moduleRoutes);
        BrandsEndpoints.Map(moduleRoutes);
        ProductsEndpoints.Map(moduleRoutes);
        PublicProductsEndpoints.Map(moduleRoutes);
        PublicProductDetailEndpoints.Map(moduleRoutes);
        // Hai route trang SEO trả HTML, nên không thuộc tài liệu API.
        MapPublicPagesWhenConfigured(endpoints.MapGroup(string.Empty).ExcludeFromDescription());
        UploadSignatureEndpoint.Map(moduleRoutes);
        InventoryEndpoints.Map(moduleRoutes);
        return endpoints;
    }

    // Hai route HTML cần template. Thiếu `WEB_INDEX_HTML_PATH` thì không gắn route, và log để người triển khai biết.
    private static void MapPublicPagesWhenConfigured(IEndpointRouteBuilder endpoints)
    {
        var services = endpoints.ServiceProvider;
        if (services.GetService<WebPageTemplate>() is not null)
        {
            PublicPageEndpoints.Map(endpoints);
            return;
        }

        services.GetRequiredService<ILoggerFactory>().CreateLogger("Pickory.Modules.Catalog")
            .LogInformation("WEB_INDEX_HTML_PATH chưa đặt, nên /p/{{slug}} và /c/{{slug}} không trả thẻ meta SEO.");
    }
}
