using System.Text.Json;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Pickory.Api.Middleware;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;
using Pickory.Shared.Json;

namespace Pickory.IntegrationTests.Infrastructure;

// Bộ lọc khởi động đăng ký các route phục vụ kiểm thử tích hợp mà không ảnh hưởng code app.
public class TestEndpointsStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            // Route kiểm thử nằm ngoài pipeline của app. Bộ lọc gắn cùng middleware lỗi để route kiểm thử trả JSON lỗi như route thật.
            app.UseMiddleware<ExceptionHandlingMiddleware>();

            // Gắn middleware tiếp nhận các route kiểm thử đặc thù trước khi chuyển luồng tới endpoint chính.
            app.Use(async (context, nextMiddleware) =>
            {
                var path = context.Request.Path.Value ?? string.Empty;

                if (path == "/api/test/business-error")
                {
                    // Mô phỏng ngoại lệ nghiệp vụ có mã HTTP chỉ định.
                    throw new AppException("SAMPLE_BUSINESS_ERROR", StatusCodes.Status422UnprocessableEntity, "Thao tác không hợp lệ.");
                }

                if (path == "/api/test/validation-error")
                {
                    // Mô phỏng ngoại lệ xác thực đầu vào với nhiều trường dữ liệu lỗi.
                    throw new FluentValidation.ValidationException(new[]
                    {
                        new ValidationFailure("Specs.Brand", "Thương hiệu không hợp lệ"),
                        new ValidationFailure(string.Empty, "Nội dung yêu cầu bắt buộc"),
                    });
                }

                if (path == "/api/test/unhandled-error")
                {
                    // Mô phỏng ngoại lệ máy chủ không lường trước chứa thông tin nhạy cảm.
                    throw new InvalidOperationException("Bí mật nội bộ không được rò rỉ ra ngoài.");
                }

                if (path == "/api/test/datetime")
                {
                    // Trả dữ liệu thời gian để kiểm thử định dạng JSON qua HTTP.
                    context.Response.ContentType = "application/json; charset=utf-8";
                    var sample = new ApiResponse<object>(new
                    {
                        Timestamp = new DateTime(2026, 10, 5, 7, 0, 0, DateTimeKind.Utc),
                    });
                    await context.Response.WriteAsync(JsonSerializer.Serialize(sample, PickoryJsonDefaults.Options));
                    return;
                }

                if (path == "/api/test/echo-body" && context.Request.Method == HttpMethods.Post)
                {
                    // Đọc toàn bộ luồng nội dung gửi lên để kích hoạt kiểm tra giới hạn kích thước payload.
                    using var reader = new StreamReader(context.Request.Body);
                    _ = await reader.ReadToEndAsync();
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsync("{\"data\":{\"received\":true}}");
                    return;
                }

                await nextMiddleware();
            });

            // Chuyển giao luồng tới các endpoint thực thi chính của ứng dụng gồm /api/health.
            next(app);
        };
    }
}
