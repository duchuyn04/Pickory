using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Persistence;
using Pickory.Shared.Http;

namespace Pickory.Modules.Identity.Features.GetMe;

// `GET /api/me` cho biết ai đang dùng web và người đó được làm gì.
public static class GetMeEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/me", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(HttpContext context, IdentityDbContext database)
    {
        var actor = context.GetActor();

        // Khách không có hồ sơ, nên email, tên và số điện thoại bằng null.
        // Truy vấn chỉ chọn ba trường cần hiển thị, không đọc hash mật khẩu.
        var profile = actor.UserId is null
            ? null
            : await database.Users
                .AsNoTracking()
                .Where(user => user.Id == actor.UserId)
                .Select(user => new { user.Email, user.Name, user.Phone })
                .SingleOrDefaultAsync(context.RequestAborted);

        // `Permissions` là danh sách vì JSON không có kiểu tập hợp. Thứ tự giữ nguyên thứ tự của bản Node.
        var response = new MeResponse(
            actor.AnonymousId,
            actor.UserId,
            profile?.Email,
            profile?.Name,
            profile?.Phone,
            ToKindName(actor.Kind),
            actor.Roles,
            actor.Permissions,
            actor.Consent);
        return Results.Ok(new ApiResponse<MeResponse>(response));
    }

    // Frontend so sánh `kind` với chuỗi chữ thường của bản Node.
    private static string ToKindName(ActorKind kind)
    {
        return kind switch
        {
            ActorKind.Guest => "guest",
            ActorKind.Customer => "customer",
            ActorKind.Staff => "staff",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Loại actor không xác định."),
        };
    }
}
