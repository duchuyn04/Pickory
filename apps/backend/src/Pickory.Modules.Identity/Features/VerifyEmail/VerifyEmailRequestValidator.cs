using FluentValidation;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Identity.Features.VerifyEmail;

// Thông báo lỗi giữ nguyên bản Node. Frontend hiển thị trực tiếp các thông báo này.
public sealed class VerifyEmailRequestValidator : AbstractValidator<VerifyEmailRequest>
{
    public VerifyEmailRequestValidator()
    {
        // Token rỗng không thể khớp hash nào. API từ chối trước khi truy vấn DB.
        RuleFor(request => request.Token)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(RequestValidation.RequiredMessage)
            .MinimumLength(1).WithMessage("Token xác thực không được để trống.");
    }
}
