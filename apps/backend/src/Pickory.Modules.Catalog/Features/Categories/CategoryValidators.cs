using FluentValidation;

namespace Pickory.Modules.Catalog.Features.Categories;

// Kiểm tra tính hợp lệ của dữ liệu đầu vào khi tạo mới danh mục.
public sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        // Tên danh mục là bắt buộc và không vượt quá 200 ký tự.
        RuleFor(request => request.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Tên danh mục không được để trống.")
            .Must(name => name.Trim().Length > 0).WithMessage("Tên danh mục không được để trống.")
            .MaximumLength(200).WithMessage("Tên danh mục không được quá 200 ký tự.");
    }
}

// Kiểm tra tính hợp lệ của dữ liệu đầu vào khi cập nhật danh mục.
public sealed class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator()
    {
        // Khi client gửi tên mới thì tên đó không được là chuỗi rỗng.
        RuleFor(request => request.Name)
            .Cascade(CascadeMode.Stop)
            .Must(name => name!.Trim().Length > 0).WithMessage("Tên danh mục không được để trống.")
            .MaximumLength(200).WithMessage("Tên danh mục không được quá 200 ký tự.")
            .When(request => request.Name is not null);
    }
}
