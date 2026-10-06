using FluentValidation;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Identity.Features.SaveConsent;

public sealed class SaveConsentRequestValidator : AbstractValidator<SaveConsentRequest>
{
    public SaveConsentRequestValidator()
    {
        // Nhóm cần thiết giữ web hoạt động. Client chỉ được bỏ trống hoặc gửi `true`.
        RuleFor(request => request.Necessary)
            .Must(necessary => necessary is null or true)
            .WithMessage("Nhóm cần thiết luôn bật và không tắt được.");

        // Người dùng phải chọn rõ ràng. API không tự giả định đồng ý cá nhân hóa.
        RuleFor(request => request.Personalization)
            .NotNull().WithMessage(RequestValidation.RequiredMessage);
    }
}
