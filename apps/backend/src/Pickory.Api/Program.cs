using Microsoft.OpenApi;
using Pickory.Api;
using Pickory.Api.Benchmark;
using Pickory.Api.Deployment;
using Pickory.Api.E2e;
using Pickory.Api.Middleware;
using Pickory.Modules.AiGateway;
using Pickory.Modules.Catalog;
using Pickory.Modules.Catalog.Seeding;
using Pickory.Modules.Commerce;
using Pickory.Modules.Identity;
using Pickory.Modules.Shipping;
using Pickory.Shared.Configuration;
using Pickory.Shared.DependencyInjection;
using Pickory.Shared.Http;
using Pickory.Shared.Json;
using Pickory.Shared.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Cấu hình ghi log đầu ra theo định dạng JSON cho môi trường container.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = false;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});

// API đọc `.env` ở gốc repo với độ ưu tiên thấp nhất, vì `dotnet run` ở máy dev không đặt biến môi trường.
// Test tích hợp tắt bước này để cấu hình dev không lọt vào test.
if (builder.Configuration["PICKORY_SKIP_DOTENV"] != "true")
{
    DotEnvFile.AddAsLowestPriority(builder.Configuration, AppContext.BaseDirectory);
}

// Nạp cấu hình từ biến môi trường và dừng ứng dụng nếu thiếu cấu hình bắt buộc.
var config = AppConfig.Load(builder.Configuration);
builder.Services.AddSingleton(config);

// Lệnh benchmark ghi 500 sản phẩm, nên kiểm tên DB trước mọi thao tác. Sai tên thì dừng mà chưa kết nối DB.
var isBenchmarkCommand = CatalogModule.IsCatalogBenchmarkCommand(args);
if (isBenchmarkCommand)
{
    try
    {
        BenchmarkDatabase.EnsureBenchmarkDatabase(config.DatabaseUrl);
    }
    catch (InvalidOperationException exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 1;
    }
}

// Lệnh seed, benchmark, đồng bộ địa chỉ và migration tự in kết quả và lỗi ra màn hình. Log JSON của EF Core sẽ làm loãng kết quả đó.
if (isBenchmarkCommand
    || CatalogModule.IsCatalogSeedCommand(args)
    || ShippingModule.IsShippingCommand(args)
    || DatabaseMigrationCommand.IsCommand(args))
{
    builder.Logging.ClearProviders();
}

// Container cần nghe trên mọi giao diện mạng. API E2E và lệnh benchmark chỉ nghe trên loopback, benchmark dùng cổng 0 để hệ điều hành cấp cổng trống.
var listenAddress = E2eStartup.IsCommand(args) || isBenchmarkCommand ? "127.0.0.1" : "0.0.0.0";
var listenPort = isBenchmarkCommand ? 0 : config.Port;
builder.WebHost.UseUrls($"http://{listenAddress}:{listenPort}");

// Giới hạn kích thước payload tối đa 1 MB cho toàn bộ máy chủ web.
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 1024 * 1024;
});

// Đăng ký các dịch vụ nền tảng dùng chung gồm bộ điều phối sự kiện.
builder.Services.AddSharedInfrastructure();

// Mỗi module tự đăng ký dữ liệu và dịch vụ. `Program.cs` không phụ thuộc vào phần bên trong module.
// Mọi module dùng chung một chuỗi kết nối, nên một giới hạn `DATABASE_POOL_MAX` áp cho cả API và worker nền.
var databaseConnectionString = PostgresConnectionString.WithMaxPoolSize(
    PostgresConnectionString.Convert(config.DatabaseUrl), config.DatabasePoolMax);
builder.Services.AddIdentityModule(databaseConnectionString);
builder.Services.AddAiGatewayModule(databaseConnectionString);
// Catalog cần biết môi trường để dừng production khi thiếu khóa Cloudinary.
builder.Services.AddCatalogModule(databaseConnectionString, config, builder.Environment.IsProduction());
// Shipping cần biết môi trường để dừng production khi thiếu biến GHN.
builder.Services.AddShippingModule(databaseConnectionString, config, builder.Environment.IsProduction());
// Commerce cần biết môi trường để dừng production khi thiếu khóa webhook SePay.
builder.Services.AddCommerceModule(databaseConnectionString, config, builder.Environment.IsProduction());

// Development luôn có tài liệu. Môi trường khác chỉ sinh tài liệu khi `API_DOCS_ENABLED` bật, vì tài liệu liệt kê cả route admin.
var apiDocumentationMode = ApiDocumentation.SelectMode(builder.Environment.IsDevelopment(), config.ApiDocsEnabled);
if (apiDocumentationMode is not null)
{
    builder.Services.AddOpenApi(ApiDocumentation.DocumentName, options =>
    {
        // Tiêu đề mặc định lấy từ tên assembly. Tên này không giúp người đọc nhận ra API.
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = ApiDocumentation.Title;
            document.Info.Version = ApiDocumentation.DocumentName;
            // Mặc định tài liệu ghi địa chỉ tuyệt đối của API. Địa chỉ `/` giữ origin của trang web, để "Try it out" qua proxy Vite không bị chặn CORS.
            document.Servers = [new OpenApiServer { Url = "/" }];
            return Task.CompletedTask;
        });
    });
}

// Chuẩn hóa cấu hình JSON của Minimal API theo camelCase và ISO 8601 UTC.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    PickoryJsonDefaults.Configure(options.SerializerOptions);
});

var app = builder.Build();

// Lệnh như `identity:seed` dùng cùng cấu hình DB với API. Lệnh chạy xong thì thoát, không mở cổng mạng.
if (IdentityModule.IsIdentityCommand(args))
{
    return await IdentityModule.RunIdentityCommandAsync(args, app.Services);
}

// Lệnh `catalog:seed` ghi dữ liệu mẫu rồi thoát, giống `identity:seed`.
if (CatalogModule.IsCatalogSeedCommand(args))
{
    return await CatalogModule.RunCatalogSeedCommandAsync(app.Services, builder.Environment.IsProduction());
}

// Lệnh `shipping:sync-addresses` đồng bộ danh mục địa chỉ một lần rồi thoát. Lệnh không mở cổng mạng và không chạy worker nền.
if (ShippingModule.IsShippingCommand(args))
{
    return await ShippingModule.RunShippingCommandAsync(app.Services);
}

// Lệnh `db:migrate` áp migration rồi thoát. Lệnh không mở cổng mạng và không chạy worker nền.
if (DatabaseMigrationCommand.IsCommand(args))
{
    return await DatabaseMigrationCommand.RunAsync(app.Services, Console.Out, Console.Error, CancellationToken.None);
}

// Lệnh `e2e:start` dựng lại DB E2E rồi chạy API như bình thường. Playwright chỉ thấy API sẵn sàng sau bước chuẩn bị.
if (E2eStartup.IsCommand(args))
{
    await E2eStartup.PrepareAsync(app.Services, app.Configuration, CancellationToken.None);
}

// Đọc IP thật của client sau proxy trước mọi middleware khác. Giới hạn tần suất của identity đếm theo IP này.
var forwardedHeadersOptions = TrustProxyOptions.Create(config.TrustProxy);
if (forwardedHeadersOptions is not null)
{
    app.UseForwardedHeaders(forwardedHeadersOptions);
}

// Middleware lỗi đứng sau trang lỗi dev của môi trường Development. Lỗi từ middleware và route luôn thành JSON `{ error }` như bản Node.
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Actor middleware kiểm tra nguồn và xác định người gửi. Middleware phải chạy trước mọi route, kể cả `/api/health`.
app.UseIdentityActor();

// Endpoint kiểm tra sức khỏe của dịch vụ phục vụ giám sát và kiểm thử.
app.MapGet("/api/health", () => Results.Ok(new ApiResponse<object>(new { status = "ok" }))).WithTags("System");

// Mỗi module tự gắn route của mình để `Program.cs` không cần biết danh sách endpoint.
app.MapIdentityEndpoints();
app.MapAiGatewayEndpoints();
app.MapCatalogEndpoints();
app.MapShippingEndpoints();
app.MapCommerceEndpoints();

// Lệnh benchmark cần đủ route để đo `GET /api/products`, nên chạy sau khi route đã gắn.
if (isBenchmarkCommand)
{
    return await CatalogBenchmark.RunAsync(app, config);
}

// Gắn tài liệu theo chế độ đã chọn khi đăng ký dịch vụ. Đặt sau các nhánh lệnh để lệnh seed và benchmark không bị ảnh hưởng.
if (apiDocumentationMode is { } selectedDocumentationMode)
{
    ApiDocumentation.Map(app, selectedDocumentationMode);
}

app.Run();
return 0;

// Cho phép WebApplicationFactory truy cập entry point của ứng dụng trong kiểm thử.
public partial class Program
{
}
