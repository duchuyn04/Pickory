using FluentValidation;

namespace Pickory.Modules.Identity.Features.AdminRoles;

// Thông báo lỗi giữ nguyên bản Node. Frontend hiển thị trực tiếp các thông báo này.
public sealed class UpdateRoleRequestValidator : AbstractValidator<UpdateRoleRequest>
{
    public UpdateRoleRequestValidator()
    {
        // Tên chỉ bị kiểm tra khi client gửi. Tên toàn khoảng trắng bị coi là trống.
        RuleFor(request => request.Name)
            .Must(name => name!.Trim().Length > 0).WithMessage("Tên role không được để trống.")
            .When(request => request.Name is not null);
    }
}
