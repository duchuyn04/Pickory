using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Entities;
using Pickory.Shared.Http;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Identity.Features.ResetPassword;

// `POST /api/auth/reset-password` đặt mật khẩu mới cho link quên mật khẩu và link mời nhân viên.
public static class ResetPasswordEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/reset-password", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        ResetPasswordRequest? request,
        IValidator<ResetPasswordRequest> validator,
        ResetPasswordHandler handler)
    {
        // Kiểm tra body trước mọi bước khác. Dữ liệu sai nhận lỗi `VALIDATION_ERROR` và không chạm DB.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        // Handler dùng token, đổi mật khẩu và xóa mọi phiên trong một giao dịch.
        var userKind = await handler.ResetAsync(validRequest, context.RequestAborted);

        // Trả loại tài khoản. Giao diện đưa nhân viên tới trang đăng nhập nhân viên thay vì trang của khách.
        return Results.Ok(new ApiResponse<ResetPasswordResponse>(new ResetPasswordResponse(
            Success: true,
            userKind == UserKind.Staff ? "staff" : "customer",
            "Đặt lại mật khẩu thành công. Vui lòng đăng nhập bằng mật khẩu mới.")));
    }
}
