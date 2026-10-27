using FluentValidation;
using Pickory.Modules.Catalog.Images;
using Pickory.Modules.Catalog.Products;

namespace Pickory.Modules.Catalog.Features.Products;

// Kiểm tra tính hợp lệ của từng biến thể sản phẩm trước khi đưa vào nghiệp vụ.
public sealed class ProductVariantRequestValidator : AbstractValidator<ProductVariantRequest>
{
    public ProductVariantRequestValidator()
    {
        // Tồn kho đích là số nguyên không âm. Bỏ trống nghĩa là giữ nguyên tồn kho hiện tại.
        RuleFor(variant => variant.Stock)
            .GreaterThanOrEqualTo(0).WithMessage("Số lượng tồn kho không được âm.")
            .LessThanOrEqualTo(1_000_000).WithMessage("Số lượng tồn kho không được vượt quá 1.000.000.")
            .When(variant => variant.Stock.HasValue);

        // Tập giá trị tùy chọn không được chứa khóa hoặc giá trị null.
        RuleFor(variant => variant.OptionValues)
            .Must(options => options == null || (!options.Keys.Any(key => key is null) && !options.Values.Any(value => value is null)))
            .WithMessage("Giá trị tùy chọn không được chứa phần tử rỗng.");

        // Cột price trong cơ sở dữ liệu có kiểu integer, nên giá bán không được vượt quá int.MaxValue.
        RuleFor(variant => variant.Price)
            .LessThanOrEqualTo(int.MaxValue).WithMessage("Giá bán không được vượt quá giới hạn cho phép.");

        // Cột compare_at_price lưu dạng integer, nên giá niêm yết không được vượt quá int.MaxValue.
        RuleFor(variant => variant.CompareAtPrice)
            .LessThanOrEqualTo(int.MaxValue).WithMessage("Giá niêm yết không được vượt quá giới hạn cho phép.")
            .When(variant => variant.CompareAtPrice.HasValue);

        // Kiểm tra tính toàn vẹn của giá bán và giá niêm yết qua helper định giá.
        RuleFor(variant => variant)
            .Must(variant => ProductPricing.ValidatePrice(variant.Price, variant.CompareAtPrice).Valid)
            .WithMessage(variant => ProductPricing.ValidatePrice(variant.Price, variant.CompareAtPrice).Error ?? "Quy tắc giá không hợp lệ.");

        // Chiều dài chỉ bắt buộc khi đăng bán (CAT-07), nhưng nếu gửi giá trị thì phải là số dương.
        RuleFor(variant => variant.LengthCm)
            .GreaterThan(0).WithMessage("Chiều dài phải lớn hơn 0.")
            .When(variant => variant.LengthCm.HasValue);

        // Chiều rộng chỉ bắt buộc khi đăng bán (CAT-07), nhưng nếu gửi giá trị thì phải là số dương.
        RuleFor(variant => variant.WidthCm)
            .GreaterThan(0).WithMessage("Chiều rộng phải lớn hơn 0.")
            .When(variant => variant.WidthCm.HasValue);

        // Chiều cao chỉ bắt buộc khi đăng bán (CAT-07), nhưng nếu gửi giá trị thì phải là số dương.
        RuleFor(variant => variant.HeightCm)
            .GreaterThan(0).WithMessage("Chiều cao phải lớn hơn 0.")
            .When(variant => variant.HeightCm.HasValue);

        // Khối lượng chỉ bắt buộc khi đăng bán (CAT-07), nhưng nếu gửi giá trị thì phải là số dương.
        RuleFor(variant => variant.WeightGrams)
            .GreaterThan(0).WithMessage("Khối lượng phải lớn hơn 0.")
            .When(variant => variant.WeightGrams.HasValue);
    }
}

// Kiểm tra một ảnh gửi kèm sản phẩm trước khi API hỏi kho ảnh.
public sealed class ProductImageRequestValidator : AbstractValidator<ProductImageRequest>
{
    public ProductImageRequestValidator()
    {
        // Chỉ nhận ảnh trong thư mục sản phẩm, để không gắn được ảnh khác trong tài khoản Cloudinary.
        RuleFor(image => image.PublicId)
            .Must(publicId => !string.IsNullOrEmpty(publicId) && ImageUploadRules.IsProductPublicId(publicId))
            .WithMessage("Mã ảnh không thuộc thư mục ảnh sản phẩm.");

        // Alt text bắt buộc cho người dùng trình đọc màn hình và có giới hạn độ dài.
        RuleFor(image => image.Alt)
            .Must(alt => !string.IsNullOrWhiteSpace(alt)).WithMessage("Mô tả ảnh (alt) không được để trống.")
            .Must(alt => alt is null || alt.Trim().Length <= ImageUploadRules.MaxAltLength)
            .WithMessage($"Mô tả ảnh (alt) không được dài quá {ImageUploadRules.MaxAltLength} ký tự.");
    }
}

// Kiểm tra cả danh sách ảnh: số lượng tối đa, không có phần tử rỗng và không trùng ảnh.
public sealed class ProductImageListValidator : AbstractValidator<IReadOnlyList<ProductImageRequest>>
{
    public ProductImageListValidator()
    {
        RuleFor(images => images)
            .Must(images => images.Count <= ImageUploadRules.MaxImagesPerProduct)
            .WithMessage($"Mỗi sản phẩm có tối đa {ImageUploadRules.MaxImagesPerProduct} ảnh.")
            .Must(images => images.All(image => image is not null))
            .WithMessage("Danh sách ảnh không được chứa phần tử null.")
            // Một ảnh gắn hai lần sẽ bị xóa nhầm khi admin bỏ một trong hai vị trí.
            .Must(images => images.Where(image => image is not null)
                .Select(image => image.PublicId).Distinct(StringComparer.Ordinal).Count() == images.Count(image => image is not null))
            .WithMessage("Một ảnh không được gắn hai lần vào cùng sản phẩm.");

        RuleForEach(images => images)
            .SetValidator(new ProductImageRequestValidator())
            .When(images => images.All(image => image is not null));
    }
}

// Kiểm tra dữ liệu đầu vào bắt buộc khi tạo mới một sản phẩm.
public sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        // Tồn kho phải nằm trong từng biến thể. Trạng thái do route đổi trạng thái quyết định.
        RuleFor(request => request.ExtensionData)
            .Must(extension => extension == null || !extension.Keys.Any(key =>
                key.Equals("stock", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("status", StringComparison.OrdinalIgnoreCase)))
            .WithMessage("Tồn kho phải khai báo trong từng biến thể; không được thiết lập trạng thái khi tạo sản phẩm.");

        // Thiếu mã danh mục thì hệ thống không thể xác định bộ thuộc tính để thẩm định thông số.
        RuleFor(request => request.CategoryId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Mã danh mục không được để trống.")
            .Must(categoryId => categoryId.Trim().Length > 0).WithMessage("Mã danh mục không được để trống.");

        // Thiếu mã thương hiệu thì bản ghi sản phẩm vi phạm ràng buộc toàn vẹn khóa ngoại.
        RuleFor(request => request.BrandId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Mã thương hiệu không được để trống.")
            .Must(brandId => brandId.Trim().Length > 0).WithMessage("Mã thương hiệu không được để trống.");

        // Giới hạn 200 ký tự để vừa hiển thị thẻ giao diện và không làm tràn cột cơ sở dữ liệu.
        RuleFor(request => request.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Tên sản phẩm không được để trống.")
            .Must(name => name.Trim().Length > 0).WithMessage("Tên sản phẩm không được để trống.")
            .MaximumLength(200).WithMessage("Tên sản phẩm không được vượt quá 200 ký tự.");

        // Giới hạn 1000 ký tự để phù hợp làm tóm tắt hiển thị và thẻ chia sẻ mạng xã hội.
        RuleFor(request => request.ShortDesc)
            .NotNull().WithMessage("Mô tả ngắn không được để trống.")
            .MaximumLength(1000).WithMessage("Mô tả ngắn không được vượt quá 1000 ký tự.");

        // Kiểm tra sự hiện diện của mô tả chi tiết để schema kiểm tra nghiệp vụ sau đó.
        RuleFor(request => request.Description)
            .NotNull().WithMessage("Mô tả chi tiết không được để trống.");

        // Kiểm tra sự hiện diện của thông số kỹ thuật để validator thông số thẩm định.
        RuleFor(request => request.Specs)
            .NotNull().WithMessage("Thông số kỹ thuật không được để trống.");

        // Thời gian bảo hành là khoảng thời gian không thể âm, với số 0 đại diện cho không bảo hành.
        RuleFor(request => request.WarrantyMonths)
            .GreaterThanOrEqualTo(0).WithMessage("Số tháng bảo hành không được âm.")
            .When(request => request.WarrantyMonths.HasValue);

        // Trục tùy chọn không được chứa phần tử rỗng hoặc giá trị rỗng.
        RuleFor(request => request.Options)
            .Must(options => options == null || options.All(option =>
                option is not null &&
                !string.IsNullOrWhiteSpace(option.Name) &&
                option.Values is not null &&
                !option.Values.Any(value => value is null)))
            .WithMessage("Danh sách tùy chọn không được chứa phần tử không hợp lệ.");

        // Danh sách biến thể là bắt buộc và không được chứa phần tử null.
        RuleFor(request => request.Variants)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Danh sách biến thể không được để trống.")
            .NotEmpty().WithMessage("Danh sách biến thể không được để trống.")
            .Must(variants => variants!.All(variant => variant is not null))
            .WithMessage("Danh sách biến thể không được chứa phần tử null.");

        // Áp dụng bộ kiểm tra chi tiết cho từng biến thể trong danh sách.
        RuleForEach(request => request.Variants)
            .SetValidator(new ProductVariantRequestValidator())
            .When(request => request.Variants is not null);

        // Ảnh không bắt buộc khi tạo. Có gửi thì phải qua luật của danh sách ảnh.
        RuleFor(request => request.Images!)
            .SetValidator(new ProductImageListValidator())
            .When(request => request.Images is not null);
    }
}

// Kiểm tra dữ liệu cập nhật từng phần cho sản phẩm đã tồn tại.
public sealed class UpdateProductRequestValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductRequestValidator()
    {
        // Tồn kho phải nằm trong từng biến thể. Trạng thái do route đổi trạng thái quyết định.
        RuleFor(request => request.ExtensionData)
            .Must(extension => extension == null || !extension.Keys.Any(key =>
                key.Equals("stock", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("status", StringComparison.OrdinalIgnoreCase)))
            .WithMessage("Tồn kho phải khai báo trong từng biến thể; không được cập nhật trạng thái qua API này.");

        // Không cho phép xóa rỗng mã danh mục khi trường này được gửi lên.
        RuleFor(request => request.CategoryId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Mã danh mục không được để trống.")
            .Must(id => id!.Trim().Length > 0).WithMessage("Mã danh mục không được để trống.")
            .When(request => request.CategoryIdSpecified);

        // Không cho phép xóa rỗng mã thương hiệu khi trường này được gửi lên.
        RuleFor(request => request.BrandId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Mã thương hiệu không được để trống.")
            .Must(id => id!.Trim().Length > 0).WithMessage("Mã thương hiệu không được để trống.")
            .When(request => request.BrandIdSpecified);

        // Giới hạn 200 ký tự để vừa hiển thị thẻ giao diện và không làm tràn cột cơ sở dữ liệu.
        RuleFor(request => request.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Tên sản phẩm không được để trống.")
            .Must(name => name!.Trim().Length > 0).WithMessage("Tên sản phẩm không được để trống.")
            .MaximumLength(200).WithMessage("Tên sản phẩm không được vượt quá 200 ký tự.")
            .When(request => request.NameSpecified);

        // Giới hạn 1000 ký tự để phù hợp làm tóm tắt hiển thị và thẻ chia sẻ mạng xã hội.
        RuleFor(request => request.ShortDesc)
            .NotNull().WithMessage("Mô tả ngắn không được để trống.")
            .MaximumLength(1000).WithMessage("Mô tả ngắn không được vượt quá 1000 ký tự.")
            .When(request => request.ShortDescSpecified);

        // Mô tả chi tiết không được để null nếu trường này được chỉ định.
        RuleFor(request => request.Description)
            .NotNull().WithMessage("Mô tả chi tiết không được để trống.")
            .When(request => request.DescriptionSpecified);

        // Thông số kỹ thuật không được để null nếu trường này được chỉ định.
        RuleFor(request => request.Specs)
            .NotNull().WithMessage("Thông số kỹ thuật không được để trống.")
            .When(request => request.SpecsSpecified);

        // Thời gian bảo hành là khoảng thời gian không thể âm, với số 0 đại diện cho không bảo hành.
        RuleFor(request => request.WarrantyMonths)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Số tháng bảo hành không được để trống.")
            .GreaterThanOrEqualTo(0).WithMessage("Số tháng bảo hành không được âm.")
            .When(request => request.WarrantyMonthsSpecified);

        // Tùy chọn không được để null hoặc chứa phần tử rỗng khi được chỉ định.
        RuleFor(request => request.Options)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Danh sách tùy chọn không được để trống.")
            .Must(options => options!.All(option =>
                option is not null &&
                !string.IsNullOrWhiteSpace(option.Name) &&
                option.Values is not null &&
                !option.Values.Any(value => value is null)))
            .WithMessage("Danh sách tùy chọn không được chứa phần tử không hợp lệ.")
            .When(request => request.OptionsSpecified);

        // Danh sách biến thể không được để null hoặc rỗng khi cập nhật.
        RuleFor(request => request.Variants)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Danh sách biến thể không được để trống.")
            .NotEmpty().WithMessage("Danh sách biến thể không được để trống.")
            .Must(variants => variants!.All(variant => variant is not null))
            .WithMessage("Danh sách biến thể không được chứa phần tử null.")
            .When(request => request.VariantsSpecified);

        // Áp dụng bộ kiểm tra cho từng biến thể khi danh sách biến thể được gửi.
        RuleForEach(request => request.Variants)
            .SetValidator(new ProductVariantRequestValidator())
            .When(request => request.VariantsSpecified && request.Variants is not null);

        // Gửi `images: null` không rõ ý định. Muốn bỏ hết ảnh thì gửi danh sách rỗng.
        RuleFor(request => request.Images)
            .NotNull().WithMessage("Danh sách ảnh không được null. Gửi danh sách rỗng để bỏ hết ảnh.")
            .When(request => request.ImagesSpecified);

        RuleFor(request => request.Images!)
            .SetValidator(new ProductImageListValidator())
            .When(request => request.ImagesSpecified && request.Images is not null);
    }
}

// Kiểm tra tên trạng thái trong yêu cầu đổi trạng thái sản phẩm.
public sealed class ChangeProductStatusRequestValidator : AbstractValidator<ChangeProductStatusRequest>
{
    public ChangeProductStatusRequestValidator()
    {
        // Từ chối giá trị lạ trước khi đọc DB, để lỗi nhập liệu không lẫn với lỗi nghiệp vụ.
        RuleFor(request => request.Status)
            .Must(status => ProductStatusNames.TryParse(status, out _))
            .WithMessage("Trạng thái phải là draft, active, hidden hoặc discontinued.");
    }
}
