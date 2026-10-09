using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Shared.Http;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Identity.Features.VerifyEmail;

// `POST /api/auth/verify-email` xác thực email bằng token trong link.
public static class VerifyEmailEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/verify-email", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        VerifyEmailRequest? request,
        IValidator<VerifyEmailRequest> validator,
        VerifyEmailHandler handler)
    {
        // Kiểm tra body trước mọi bước khác. Dữ liệu sai nhận lỗi `VALIDATION_ERROR` và không chạm DB.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        // Handler ném `INVALID_TOKEN` khi token sai, hết hạn hoặc đã dùng. Route chỉ trả thành công khi email đã được xác thực.
        await handler.VerifyAsync(validRequest, context.RequestAborted);
        return Results.Ok(new ApiResponse<VerifyEmailResponse>(new VerifyEmailResponse(Success: true, "Xác thực email thành công.")));
    }
}
