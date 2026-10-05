using Microsoft.AspNetCore.Http;

namespace Pickory.Modules.Identity.Contracts.Actors;

// Lưu actor trong feature của request. Route ở mọi module đọc actor qua một chỗ duy nhất.
public static class HttpContextActorExtensions
{
    public static void SetActor(this HttpContext context, Actor actor)
    {
        context.Features.Set(actor);
    }

    public static Actor GetActor(this HttpContext context)
    {
        // Thiếu actor nghĩa là pipeline đặt route trước actor middleware. Lỗi cấu hình này phải dừng request.
        return context.Features.Get<Actor>()
            ?? throw new InvalidOperationException("Actor middleware của identity chưa chạy trước route này.");
    }
}
