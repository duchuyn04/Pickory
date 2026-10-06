using FluentValidation;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Identity.Features.Register;

// Thông báo lỗi giữ nguyên bản Node. Frontend hiển thị trực tiếp các thông báo này.
public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        // Thiếu email nhận lỗi bắt buộc. Email sai định dạng nhận thông báo giống `z.string().email()` của bản Node.
        RuleFor(request => request.Email)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(RequestValidation.RequiredMessage)
            .MustBeEmailAddress("Email không đúng định dạng.");

        // Giới hạn 128 ký tự chặn chuỗi rất dài. Băm Argon2 chuỗi dài có thể làm quá tải CPU.
        RuleFor(request => request.Password)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(RequestValidation.RequiredMessage)
            .MinimumLength(8).WithMessage("Mật khẩu phải chứa tối thiểu 8 ký tự.")
            .MaximumLength(128).WithMessage("Mật khẩu không được vượt quá 128 ký tự.");
    }
}
