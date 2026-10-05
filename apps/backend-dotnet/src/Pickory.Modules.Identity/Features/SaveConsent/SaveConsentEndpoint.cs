using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Consent;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Persistence;
using Pickory.Shared.Http;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Identity.Features.SaveConsent;

// `PUT /api/me/consent` lưu lựa chọn consent. Route công khai vì khách vãng lai cũng cần chọn.
public static class SaveConsentEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/me/consent", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        SaveConsentRequest? request,
        IValidator<SaveConsentRequest> validator,
        IdentityDbContext database)
    {
        // Kiểm tra body trước mọi bước khác. Dữ liệu sai nhận lỗi `VALIDATION_ERROR` và không chạm DB.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        var actor = context.GetActor();

        // Gắn bản ghi với mã ẩn danh và tài khoản. `Marketing` mặc định tắt khi client không gửi, giống bản Node.
        var record = ConsentRecorder.Add(
            database,
            actor.AnonymousId,
            actor.UserId,
            validRequest.Personalization!.Value,
            validRequest.Marketing ?? false);
        await database.SaveChangesAsync(context.RequestAborted);

        return Results.Ok(new ApiResponse<SaveConsentResponse>(
            new SaveConsentResponse(new ConsentChoice(Necessary: true, record.Personalization, record.Marketing))));
    }
}
