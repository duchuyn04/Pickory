using FluentValidation;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Identity.Features.ChangePassword;

// Thông báo lỗi giữ nguyên bản Node. Frontend hiển thị trực tiếp các thông báo này.
public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        // Giới hạn 128 ký tự chặn chuỗi rất dài. Băm Argon2 chuỗi dài có thể làm quá tải CPU.
        RuleFor(request => request.NewPassword)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(RequestValidation.RequiredMessage)
            .MinimumLength(8).WithMessage("Mật khẩu mới phải chứa tối thiểu 8 ký tự.")
            .MaximumLength(128).WithMessage("Mật khẩu không được vượt quá 128 ký tự.");
    }
}
