using FluentValidation;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Identity.Features.AdminStaff;

// Thông báo lỗi giữ nguyên bản Node. Frontend hiển thị trực tiếp các thông báo này.
public sealed class InviteStaffRequestValidator : AbstractValidator<InviteStaffRequest>
{
    public InviteStaffRequestValidator()
    {
        // Thiếu email nhận lỗi bắt buộc. Email sai định dạng nhận thông báo giống `z.string().email()` của bản Node.
        RuleFor(request => request.Email)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(RequestValidation.RequiredMessage)
            .MustBeEmailAddress("Email không đúng định dạng.");

        // Cắt khoảng trắng trước khi kiểm tra. Tên toàn khoảng trắng bị coi là trống.
        RuleFor(request => request.Name)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(RequestValidation.RequiredMessage)
            .Must(name => name!.Trim().Length > 0).WithMessage("Họ và tên không được để trống.");

        RuleFor(request => request.RoleCodes)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(RequestValidation.RequiredMessage)
            .Must(roleCodes => roleCodes!.Length > 0).WithMessage("Cần chọn ít nhất một vai trò.");
    }
}
