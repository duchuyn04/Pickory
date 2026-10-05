using FluentValidation;
using Pickory.Api.Middleware;
using Pickory.Shared.Configuration;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Extensions;
using Pickory.Shared.Json;
using Pickory.Shared.Models;

var builder = WebApplication.CreateBuilder(args);

// Cấu hình ghi log đầu ra theo định dạng JSON cho môi trường container.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = false;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});

// Nạp cấu hình từ biến môi trường và dừng ứng dụng nếu thiếu cấu hình bắt buộc.
var config = AppConfig.Load(builder.Configuration);
builder.Services.AddSingleton(config);
// Thiết lập cổng mạng lắng nghe dựa trên cấu hình khởi động.
builder.WebHost.UseUrls($"http://localhost:{config.Port}");

// Đăng ký các dịch vụ nền tảng dùng chung gồm bộ điều phối sự kiện.
builder.Services.AddSharedInfrastructure();

// Chuẩn hóa cấu hình JSON của Minimal API theo camelCase và ISO 8601 UTC.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    PickoryJsonDefaults.Configure(options.SerializerOptions);
});

var app = builder.Build();

// Đăng ký middleware bắt lỗi toàn cục trước mọi route của hệ thống.
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Endpoint kiểm tra sức khỏe của dịch vụ phục vụ giám sát và kiểm thử.
app.MapGet("/api/health", () => Results.Ok(new ApiResponse<object>(new { status = "ok" })));

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    // Đăng ký endpoint mô phỏng lỗi nghiệp vụ phục vụ kiểm thử tích hợp.
    app.MapGet("/api/test/business-error", () =>
    {
        throw new AppException("SAMPLE_BUSINESS_ERROR", StatusCodes.Status422UnprocessableEntity, "Thao tác không hợp lệ.");
    });

    // Đăng ký endpoint mô phỏng lỗi xác thực FluentValidation phục vụ kiểm thử.
    app.MapGet("/api/test/validation-error", () =>
    {
        throw new ValidationException(new[]
        {
            new FluentValidation.Results.ValidationFailure("Specs.Brand", "Thương hiệu không hợp lệ"),
            new FluentValidation.Results.ValidationFailure(string.Empty, "Nội dung yêu cầu bắt buộc"),
        });
    });

    // Đăng ký endpoint mô phỏng lỗi máy chủ không lường trước phục vụ kiểm thử.
    app.MapGet("/api/test/unhandled-error", () =>
    {
        throw new InvalidOperationException("Bí mật nội bộ không được rò rỉ ra ngoài.");
    });

    // Đăng ký endpoint kiểm thử định dạng thời gian ISO 8601 UTC qua HTTP.
    app.MapGet("/api/test/datetime", () =>
    {
        return Results.Ok(new ApiResponse<object>(new
        {
            Timestamp = new DateTime(2026, 10, 5, 7, 0, 0, DateTimeKind.Utc),
        }));
    });
}

app.Run();

// Cho phép WebApplicationFactory truy cập entry point của ứng dụng trong kiểm thử.
public partial class Program
{
}
