using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.Features;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;
using Pickory.Shared.Json;

namespace Pickory.Api.Middleware;

// Bắt ngoại lệ toàn cục và chuẩn hóa định dạng phản hồi lỗi của hệ thống.
public class ExceptionHandlingMiddleware
{
    private const long MaxRequestBodySizeBytes = 1024 * 1024; // 1 MB
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Áp dụng giới hạn kích thước đọc body cho toàn bộ request.
        var maxBodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxBodySizeFeature is { IsReadOnly: false })
        {
            maxBodySizeFeature.MaxRequestBodySize = MaxRequestBodySizeBytes;
        }

        // Chặn request khai báo tiêu đề Content-Length vượt ngưỡng 1 MB.
        if (context.Request.ContentLength > MaxRequestBodySizeBytes)
        {
            await WriteErrorResponseAsync(
                context,
                StatusCodes.Status413PayloadTooLarge,
                "PAYLOAD_TOO_LARGE",
                "Kích thước dữ liệu vượt quá giới hạn 1 MB cho phép.");
            return;
        }

        try
        {
            await _next(context);

            // Chuyển 404 hoặc 405 thành phản hồi lỗi NOT_FOUND thống nhất.
            if (!context.Response.HasStarted &&
                (context.Response.StatusCode == StatusCodes.Status404NotFound ||
                 context.Response.StatusCode == StatusCodes.Status405MethodNotAllowed))
            {
                await WriteErrorResponseAsync(
                    context,
                    StatusCodes.Status404NotFound,
                    "NOT_FOUND",
                    "Không tìm thấy tài nguyên.");
            }
        }
        catch (BadHttpRequestException badHttpException) when (badHttpException.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            // Trả lỗi 413 khi luồng dữ liệu đọc thực tế vượt ngưỡng 1 MB.
            await WriteErrorResponseAsync(
                context,
                StatusCodes.Status413PayloadTooLarge,
                "PAYLOAD_TOO_LARGE",
                "Kích thước dữ liệu vượt quá giới hạn 1 MB cho phép.");
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
