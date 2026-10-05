using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Sessions;
using Pickory.Modules.Identity.Users;
using Pickory.Shared.Http;

namespace Pickory.Modules.Identity.Features.DeleteAccount;

// `DELETE /api/me` cho khách tự xóa tài khoản sau khi xác nhận lại danh tính.
public static class DeleteAccountEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Chỉ người đã đăng nhập mới có tài khoản để xóa.
        endpoints.MapDelete("/api/me", HandleAsync).RequireAuth();
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        // Request `DELETE` có thể không có body. Tài khoản chỉ dùng Google không gửi mật khẩu.
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DeleteAccountRequest? request,
        DeleteAccountHandler handler)
    {
        var actor = context.GetActor();
        // `SessionCreatedAt` cho biết người dùng vừa đăng nhập lại Google hay chưa.
        // `RequireAuth` đã chặn khách, nên actor luôn có `UserId` ở bước này.
        await handler.DeleteAsync(actor.UserId!, request?.Password, actor.SessionCreatedAt, context.RequestAborted);

        // Mọi phiên đã bị xóa trong DB. Xóa cookie để trình duyệt về trạng thái đăng xuất.
        SessionCookies.DeleteSessionCookie(context.Response);
        return Results.Ok(new ApiResponse<AccountActionResponse>(
            new AccountActionResponse(Success: true, "Tài khoản của bạn đã được xóa thành công.")));
    }
}
