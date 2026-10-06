using FluentValidation;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Identity.Features.AdminRoles;

// Thông báo lỗi giữ nguyên bản Node. Frontend hiển thị trực tiếp các thông báo này.
public sealed class CreateRoleRequestValidator : AbstractValidator<CreateRoleRequest>
{
    public CreateRoleRequestValidator()
    {
        // Cắt khoảng trắng trước khi kiểm tra. Mã toàn khoảng trắng bị coi là trống, giống `trim().min(1)` của Zod.
        RuleFor(request => request.Code)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(RequestValidation.RequiredMessage)
            .Must(code => code!.Trim().Length > 0).WithMessage("Mã role không được để trống.");

        RuleFor(request => request.Name)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(RequestValidation.RequiredMessage)
            .Must(name => name!.Trim().Length > 0).WithMessage("Tên role không được để trống.");
    }
}
