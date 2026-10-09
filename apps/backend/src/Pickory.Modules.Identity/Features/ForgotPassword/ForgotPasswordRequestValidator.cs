using FluentValidation;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Identity.Features.ForgotPassword;

// Thông báo lỗi giữ nguyên bản Node. Frontend hiển thị trực tiếp các thông báo này.
public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        // Thiếu email nhận lỗi bắt buộc. Email sai định dạng nhận thông báo giống `z.string().email()` của bản Node.
        RuleFor(request => request.Email)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(RequestValidation.RequiredMessage)
            .MustBeEmailAddress("Email không đúng định dạng.");
    }
}
