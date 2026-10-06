using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Shared.Http;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Identity.Features.ForgotPassword;

// `POST /api/auth/forgot-password` gửi link đặt lại mật khẩu.
public static class ForgotPasswordEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/forgot-password", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        ForgotPasswordRequest? request,
        IValidator<ForgotPasswordRequest> validator,
        ForgotPasswordHandler handler)
    {
        // Kiểm tra body trước mọi bước khác. Dữ liệu sai nhận lỗi `VALIDATION_ERROR` và không chạm DB.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        // Gửi IP cho bộ giới hạn theo IP. Một máy không quét được hàng loạt email.
        await handler.RequestResetAsync(validRequest, context.GetClientIp(), context.RequestAborted);

        // Luôn trả cùng một thông báo. Người ngoài không biết email có tồn tại hoặc có bị giới hạn hay không.
        return Results.Ok(new ApiResponse<ForgotPasswordResponse>(
            new ForgotPasswordResponse("Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được gửi.")));
    }
}
