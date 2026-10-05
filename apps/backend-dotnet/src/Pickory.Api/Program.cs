using Pickory.Api.Middleware;
using Pickory.Modules.AiGateway;
using Pickory.Modules.Catalog;
using Pickory.Modules.Identity;
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

// Nạp cấu hình từ biến môi trường và dừng ứng dụng nếu thiếu cấu hình bắt buộc.
var config = AppConfig.Load(builder.Configuration);
builder.Services.AddSingleton(config);

// Thiết lập cổng mạng lắng nghe trên mọi giao diện mạng để chạy được trong container.
builder.WebHost.UseUrls($"http://0.0.0.0:{config.Port}");

// Giới hạn kích thước payload tối đa 1 MB cho toàn bộ máy chủ web.
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 1024 * 1024;
});

// Đăng ký bộ lọc khởi động gắn ExceptionHandlingMiddleware ở lớp ngoài cùng của pipeline.
builder.Services.AddTransient<IStartupFilter, ExceptionHandlingStartupFilter>();

// Đăng ký các dịch vụ nền tảng dùng chung gồm bộ điều phối sự kiện.
builder.Services.AddSharedInfrastructure();

// Mỗi module tự đăng ký dữ liệu và dịch vụ. `Program.cs` không phụ thuộc vào phần bên trong module.
var databaseConnectionString = PostgresConnectionString.Convert(config.DatabaseUrl);
builder.Services.AddIdentityModule(databaseConnectionString);
builder.Services.AddAiGatewayModule(databaseConnectionString);
builder.Services.AddCatalogModule(databaseConnectionString);

// Chuẩn hóa cấu hình JSON của Minimal API theo camelCase và ISO 8601 UTC.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    PickoryJsonDefaults.Configure(options.SerializerOptions);
});

var app = builder.Build();

// Endpoint kiểm tra sức khỏe của dịch vụ phục vụ giám sát và kiểm thử.
app.MapGet("/api/health", () => Results.Ok(new ApiResponse<object>(new { status = "ok" })));

// Mỗi module tự gắn route của mình để `Program.cs` không cần biết danh sách endpoint.
app.MapIdentityEndpoints();
app.MapAiGatewayEndpoints();
app.MapCatalogEndpoints();

app.Run();

// Cho phép WebApplicationFactory truy cập entry point của ứng dụng trong kiểm thử.
public partial class Program
{
}
