using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Pickory.Modules.Identity.Contracts.Actors;

namespace Pickory.Modules.Identity.Contracts.Authorization;

// Gắn quy tắc của `ActorAuthorization` vào route. Route khai báo quyền ngay tại chỗ gắn route.
public static class ActorAuthorizationEndpointExtensions
{
    public static TBuilder RequireAuth<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            // Kiểm tra trước khi handler chạy. Khách chưa đăng nhập không chạm tới logic của route.
            ActorAuthorization.EnsureAuthenticated(context.HttpContext.GetActor());
            return await next(context);
        });
    }

    public static TBuilder RequireStaff<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            // Kiểm tra trước khi handler chạy. Khách hàng không chạm tới logic của khu vực nhân viên.
            ActorAuthorization.EnsureStaff(context.HttpContext.GetActor());
            return await next(context);
        });
    }

    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, params string[] requiredPermissions)
        where TBuilder : IEndpointConventionBuilder
    {
        // Lỗi gõ sai tên quyền phải dừng lúc khởi động, không chờ tới lúc người dùng gọi route.
        foreach (var permission in requiredPermissions)
        {
            if (!Permissions.IsKnown(permission))
            {
                throw new ArgumentException($"Quyền '{permission}' không có trong danh mục.", nameof(requiredPermissions));
            }
        }

        return builder.AddEndpointFilter(async (context, next) =>
        {
            // Quyền nạp lại ở mỗi request. Quyền bị thu hồi có hiệu lực ngay ở request kế tiếp.
            ActorAuthorization.EnsurePermissions(context.HttpContext.GetActor(), requiredPermissions);
            return await next(context);
        });
    }
}
