using Pickory.Api.E2e;
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

// API đọc `.env` ở gốc repo với độ ưu tiên thấp nhất, vì `dotnet run` ở máy dev không đặt biến môi trường.
// Test tích hợp tắt bước này để cấu hình dev không lọt vào test.
if (builder.Configuration["PICKORY_SKIP_DOTENV"] != "true")
{
    DotEnvFile.AddAsLowestPriority(builder.Configuration, AppContext.BaseDirectory);
}

// Nạp cấu hình từ biến môi trường và dừng ứng dụng nếu thiếu cấu hình bắt buộc.
var config = AppConfig.Load(builder.Configuration);
builder.Services.AddSingleton(config);

// Container cần nghe trên mọi giao diện mạng. API E2E chỉ nghe trên loopback để máy khác không truy cập được.
var listenAddress = E2eStartup.IsCommand(args) ? "127.0.0.1" : "0.0.0.0";
builder.WebHost.UseUrls($"http://{listenAddress}:{config.Port}");

// Giới hạn kích thước payload tối đa 1 MB cho toàn bộ máy chủ web.
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 1024 * 1024;
});

// Đăng ký các dịch vụ nền tảng dùng chung gồm bộ điều phối sự kiện.
builder.Services.AddSharedInfrastructure();

// Mỗi module tự đăng ký dữ liệu và dịch vụ. `Program.cs` không phụ thuộc vào phần bên trong module.
var databaseConnectionString = PostgresConnectionString.Convert(config.DatabaseUrl);
builder.Services.AddIdentityModule(databaseConnectionString);
builder.Services.AddAiGatewayModule(databaseConnectionString);
// Catalog cần biết môi trường để dừng production khi thiếu khóa Cloudinary.
builder.Services.AddCatalogModule(databaseConnectionString, config, builder.Environment.IsProduction());

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
app.MapGet("/api/health", () => Results.Ok(new ApiResponse<object>(new { status = "ok" })));

// Mỗi module tự gắn route của mình để `Program.cs` không cần biết danh sách endpoint.
app.MapIdentityEndpoints();
app.MapAiGatewayEndpoints();
app.MapCatalogEndpoints();

app.Run();
return 0;

// Cho phép WebApplicationFactory truy cập entry point của ứng dụng trong kiểm thử.
public partial class Program
{
}
