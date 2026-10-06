using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Persistence;
using Pickory.Modules.Identity.Sessions;
using Pickory.Modules.Identity.Tokens;
using Pickory.Modules.Identity.Users;
using Pickory.Shared.Http;

namespace Pickory.Modules.Identity.Features.Logout;

// `POST /api/auth/logout` đăng xuất khách và nhân viên.
public static class LogoutEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/logout", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(HttpContext context, IdentityDbContext database)
    {
        var rawToken = context.Request.Cookies[SessionCookies.SessionCookieName];
        // Đăng xuất khi chưa đăng nhập không phải lỗi. Không có cookie thì không có phiên để xóa.
        if (!string.IsNullOrEmpty(rawToken))
        {
            // Xóa phiên trong DB trước. Bản sao cookie còn nằm ở nơi khác cũng không dùng lại được.
            var tokenHash = SecureTokens.HashToken(rawToken);
            await database.Sessions.Where(session => session.TokenHash == tokenHash).ExecuteDeleteAsync(context.RequestAborted);
        }

        // Xóa cookie cả khi DB không có phiên. Trình duyệt ngừng gửi token không còn dùng được.
        SessionCookies.DeleteSessionCookie(context.Response);
        return Results.Ok(new ApiResponse<SuccessResponse>(new SuccessResponse(Success: true)));
    }
}
