using FluentValidation;

namespace Pickory.Modules.Catalog.Features.Brands;

// Kiểm tra tính hợp lệ của dữ liệu đầu vào khi tạo mới thương hiệu.
public sealed class CreateBrandRequestValidator : AbstractValidator<CreateBrandRequest>
{
    public CreateBrandRequestValidator()
    {
        // Tên thương hiệu là bắt buộc và không vượt quá 200 ký tự.
        RuleFor(request => request.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Tên thương hiệu không được để trống.")
            .Must(name => name.Trim().Length > 0).WithMessage("Tên thương hiệu không được để trống.")
            .MaximumLength(200).WithMessage("Tên thương hiệu không được quá 200 ký tự.");
        // Giới hạn độ dài mã định danh ảnh logo để tránh tràn bộ nhớ lưu trữ.
        RuleFor(request => request.LogoPublicId)
            .MaximumLength(255).WithMessage("Mã logo không được quá 255 ký tự.")
            .When(request => request.LogoPublicId is not null);
    }
}

// Kiểm tra tính hợp lệ của dữ liệu đầu vào khi cập nhật thương hiệu.
public sealed class UpdateBrandRequestValidator : AbstractValidator<UpdateBrandRequest>
{
    public UpdateBrandRequestValidator()
    {
        // Khi client gửi tên mới thì tên đó không được là chuỗi rỗng.
        RuleFor(request => request.Name)
            .Cascade(CascadeMode.Stop)
            .Must(name => name!.Trim().Length > 0).WithMessage("Tên thương hiệu không được để trống.")
            .MaximumLength(200).WithMessage("Tên thương hiệu không được quá 200 ký tự.")
            .When(request => request.Name is not null);
        // Giới hạn độ dài mã định danh ảnh logo khi cập nhật.
        RuleFor(request => request.LogoPublicId)
            .MaximumLength(255).WithMessage("Mã logo không được quá 255 ký tự.")
            .When(request => request.LogoPublicId is not null);
    }
}
