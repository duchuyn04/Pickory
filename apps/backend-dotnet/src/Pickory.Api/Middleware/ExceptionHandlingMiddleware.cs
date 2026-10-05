using System.Text.Json;
using FluentValidation;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Json;
using Pickory.Shared.Models;

namespace Pickory.Api.Middleware;

// Bắt ngoại lệ toàn cục và chuẩn hóa định dạng phản hồi lỗi của hệ thống.
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);

            // Bắt trường hợp route không khớp và chưa ghi nội dung vào phản hồi.
            if (!context.Response.HasStarted && context.Response.StatusCode == StatusCodes.Status404NotFound)
            {
                await WriteErrorResponseAsync(
                    context,
                    StatusCodes.Status404NotFound,
                    "NOT_FOUND",
                    "Không tìm thấy tài nguyên.");
            }
        }
        catch (AppException appException)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            // Chuyển đổi lỗi nghiệp vụ sang mã trạng thái HTTP tương ứng.
            await WriteErrorResponseAsync(
                context,
                appException.StatusCode,
                appException.Code,
                appException.Message);
        }
        catch (ValidationException validationException)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            // Gom chi tiết lỗi validate theo định dạng path: message như bản Node.
            var details = string.Join("; ", validationException.Errors.Select(failure =>
            {
                var path = string.IsNullOrWhiteSpace(failure.PropertyName)
                    ? "body"
                    : ToCamelCasePath(failure.PropertyName);
                return $"{path}: {failure.ErrorMessage}";
            }));

            var message = $"Dữ liệu không hợp lệ ({details}).";

            // Trả lỗi 400 cùng mã VALIDATION_ERROR chuẩn hóa.
            await WriteErrorResponseAsync(
                context,
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                message);
        }
        catch (Exception exception)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            // Ghi log chi tiết lỗi nội bộ phục vụ việc điều tra sự cố.
            _logger.LogError(
                exception,
                "Xảy ra lỗi không lường trước tại yêu cầu {Method} {Path}.",
                context.Request.Method,
                context.Request.Path);

            // Trả lỗi 500 che giấu stack trace và chi tiết nội bộ.
            await WriteErrorResponseAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "INTERNAL_ERROR",
                "Đã có lỗi xảy ra, vui lòng thử lại sau.");
        }
    }

    private static async Task WriteErrorResponseAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        var errorResponse = new ErrorResponse(new ErrorDetail(code, message));
        var json = JsonSerializer.Serialize(errorResponse, PickoryJsonDefaults.Options);
        await context.Response.WriteAsync(json);
    }

    private static string ToCamelCasePath(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return "body";
        }

        // Tách đường dẫn lồng nhau để chuyển từng phần tử sang camelCase.
        var segments = propertyName.Split('.');
        var camelCaseSegments = segments.Select(segment =>
        {
            if (string.IsNullOrEmpty(segment))
            {
                return segment;
            }

            if (segment.Length == 1)
            {
                return segment.ToLowerInvariant();
            }

            return char.ToLowerInvariant(segment[0]) + segment[1..];
        });

        return string.Join(".", camelCaseSegments);
    }
}
