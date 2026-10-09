using FluentValidation;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Identity.Features.ResetPassword;

// Thông báo lỗi giữ nguyên bản Node. Frontend hiển thị trực tiếp các thông báo này.
public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        // Token rỗng không thể khớp hash nào. API từ chối trước khi truy vấn DB.
        RuleFor(request => request.Token)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(RequestValidation.RequiredMessage)
            .MinimumLength(1).WithMessage("Token không được để trống.");

        // Kiểm tra độ dài trước khi chạm DB. Mật khẩu không hợp lệ không làm mất token.
        RuleFor(request => request.NewPassword)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(RequestValidation.RequiredMessage)
            .MinimumLength(8).WithMessage("Mật khẩu mới phải chứa tối thiểu 8 ký tự.")
            .MaximumLength(128).WithMessage("Mật khẩu không được vượt quá 128 ký tự.");
    }
}
