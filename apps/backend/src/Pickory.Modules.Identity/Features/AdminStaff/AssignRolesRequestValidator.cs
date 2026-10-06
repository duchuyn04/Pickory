using FluentValidation;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Identity.Features.AdminStaff;

public sealed class AssignRolesRequestValidator : AbstractValidator<AssignRolesRequest>
{
    public AssignRolesRequestValidator()
    {
        // Thiếu `roleCodes` khác với mảng rỗng. Route không đoán ý định gỡ hết role khi client quên gửi trường này.
        RuleFor(request => request.RoleCodes).NotNull().WithMessage(RequestValidation.RequiredMessage);
    }
}
