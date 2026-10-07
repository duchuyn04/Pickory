using System.Text.Json;
using System.Text.Json.Serialization;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Products;
using Pickory.Modules.Catalog.Specs;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;

namespace Pickory.Modules.Catalog.Features.Products;

// Ngoại lệ phản ánh lỗi vi phạm bộ thuộc tính kỹ thuật của danh mục lá.
public sealed class ProductSpecsException : AppException
{
    // Danh sách chi tiết lỗi thông số giúp client gán đúng vào từng ô nhập liệu.
    public IReadOnlyList<SpecError> Errors { get; }

    public ProductSpecsException(IReadOnlyList<SpecError> errors, string message = "Thông số kỹ thuật của sản phẩm không hợp lệ.")
        : base("SPECS_INVALID", 400, message)
    {
        Errors = errors;
    }
}

// Ngoại lệ phản ánh sản phẩm chưa đủ điều kiện đăng bán. `Reasons` liệt kê mọi điều kiện chưa đạt.
public sealed class ProductNotPublishableException : AppException
{
    public IReadOnlyList<string> Reasons { get; }

    public ProductNotPublishableException(IReadOnlyList<string> reasons)
        : base("PRODUCT_NOT_PUBLISHABLE", 422, "Sản phẩm chưa đủ điều kiện để đăng bán.")
    {
        Reasons = reasons;
    }
}

// Yêu cầu đổi trạng thái sản phẩm. `Status` là tên trạng thái viết thường, ví dụ `active`.
public sealed record ChangeProductStatusRequest(string? Status);

// Tên trạng thái trong API. Chỉ nhận chữ thường để giá trị số của enum không lọt vào.
public static class ProductStatusNames
{
    public static bool TryParse(string? name, out ProductStatus status)
    {
        switch (name)
        {
            case "draft": status = ProductStatus.Draft; return true;
            case "active": status = ProductStatus.Active; return true;
            case "hidden": status = ProductStatus.Hidden; return true;
            case "discontinued": status = ProductStatus.Discontinued; return true;
            default: status = default; return false;
        }
    }
}

// Bộ lọc danh sách sản phẩm quản trị phục vụ tìm kiếm, phân loại và cảnh báo tồn kho.
public sealed record ProductListQuery(
    PageQuery Page,
    string? Search = null,
    string? CategoryId = null,
    ProductStatus? Status = null,
    bool LowStock = false);

// Dữ liệu phản hồi chi tiết của một sản phẩm điện tử trong trang quản trị.
public sealed record ProductResponse(
    string Id,
    string CategoryId,
    string BrandId,
    string Name,
    string Slug,
    string ShortDesc,
    JsonElement Description,
    JsonElement Specs,
    IReadOnlyList<ProductOption> Options,
    int WarrantyMonths,
    string Status,
    int? MinPrice,
    bool InStock,
    IReadOnlyList<ProductVariantResponse> Variants,
    IReadOnlyList<ProductImageResponse> Images,
    DateTime CreatedAt,
    DateTime UpdatedAt);

// Ảnh theo thứ tự hiển thị; `SkuId` có giá trị khi ảnh thuộc riêng một biến thể.
// `Url` là URL ảnh gốc, giao diện thêm tham số biến đổi để lấy ảnh vừa kích thước.
public sealed record ProductImageResponse(string Id, string PublicId, string Alt, string? SkuId, int Position, string Url);

// Ảnh gửi kèm khi tạo hoặc sửa sản phẩm. Thứ tự trong danh sách là thứ tự hiển thị.
public sealed record ProductImageRequest(string PublicId, string Alt, string? SkuId = null);

// Dữ liệu phản hồi của biến thể (SKU) giữ thông tin bán hàng và tồn kho.
public sealed record ProductVariantResponse(
    string Id,
    string Code,
    IReadOnlyDictionary<string, string> OptionValues,
    int Price,
    int? CompareAtPrice,
    int Stock,
    bool Enabled,
    int? WeightGrams,
    int? LengthCm,
    int? WidthCm,
    int? HeightCm,
    DateTime CreatedAt,
    DateTime UpdatedAt);

// Yêu cầu tạo mới sản phẩm điện tử cùng các biến thể ban đầu từ trang quản trị.
public sealed record CreateProductRequest(
    string CategoryId,
    string BrandId,
    string Name,
    string ShortDesc = "",
    JsonElement? Description = null,
    JsonElement? Specs = null,
    IReadOnlyList<ProductOption>? Options = null,
    int? WarrantyMonths = null,
    IReadOnlyList<ProductVariantRequest>? Variants = null,
    IReadOnlyList<ProductImageRequest>? Images = null)
{
    // Giữ các trường dữ liệu ngoài mô hình để phát hiện trường cấm như tồn kho.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

// Yêu cầu khai báo hoặc cập nhật thông tin của một biến thể sản phẩm.
public sealed record ProductVariantRequest(
    IReadOnlyDictionary<string, string>? OptionValues = null,
    string? Code = null,
    decimal Price = 0,
    decimal? CompareAtPrice = null,
    bool Enabled = true,
    int? WeightGrams = null,
    int? LengthCm = null,
    int? WidthCm = null,
    int? HeightCm = null)
{
    // Bắt trường tồn kho gửi kèm để từ chối vì quyền ghi kho thuộc nghiệp vụ riêng.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

// Yêu cầu cập nhật thông tin sản phẩm và đồng bộ biến thể theo cơ chế PATCH từng phần.
public sealed class UpdateProductRequest
{
    private string? _categoryId;
    private string? _brandId;
    private string? _name;
    private string? _shortDesc;
    private JsonElement? _description;
    private JsonElement? _specs;
    private IReadOnlyList<ProductOption>? _options;
    private int? _warrantyMonths;
    private IReadOnlyList<ProductVariantRequest>? _variants;
    private IReadOnlyList<ProductImageRequest>? _images;

    public string? CategoryId
    {
        get => _categoryId;
        set
        {
            _categoryId = value;
            // Đánh dấu trường `categoryId` có trong JSON để phân biệt không gửi và gửi giá trị null.
            CategoryIdSpecified = true;
        }
    }

    [JsonIgnore]
    public bool CategoryIdSpecified { get; private set; }

    public string? BrandId
    {
        get => _brandId;
        set
        {
            _brandId = value;
            // Đánh dấu trường `brandId` có trong JSON để phân biệt không gửi và gửi giá trị null.
            BrandIdSpecified = true;
        }
    }

    [JsonIgnore]
    public bool BrandIdSpecified { get; private set; }

    public string? Name
    {
        get => _name;
        set
        {
            _name = value;
            // Đánh dấu trường `name` có trong JSON để phân biệt không gửi và gửi giá trị null.
            NameSpecified = true;
        }
    }

    [JsonIgnore]
    public bool NameSpecified { get; private set; }

    public string? ShortDesc
    {
        get => _shortDesc;
        set
        {
            _shortDesc = value;
            // Đánh dấu trường `shortDesc` có trong JSON để phân biệt không gửi và gửi giá trị null.
            ShortDescSpecified = true;
        }
    }

    [JsonIgnore]
    public bool ShortDescSpecified { get; private set; }

    public JsonElement? Description
    {
        get => _description;
        set
        {
            _description = value;
            // Đánh dấu trường `description` có trong JSON để phân biệt không gửi và gửi giá trị null.
            DescriptionSpecified = true;
        }
    }

    [JsonIgnore]
    public bool DescriptionSpecified { get; private set; }

    public JsonElement? Specs
    {
        get => _specs;
        set
        {
            _specs = value;
            // Đánh dấu trường `specs` có trong JSON để phân biệt không gửi và gửi giá trị null.
            SpecsSpecified = true;
        }
    }

    [JsonIgnore]
    public bool SpecsSpecified { get; private set; }

    public IReadOnlyList<ProductOption>? Options
    {
        get => _options;
        set
        {
            _options = value;
            // Đánh dấu trường `options` có trong JSON để phân biệt không gửi và gửi giá trị null.
            OptionsSpecified = true;
        }
    }

    [JsonIgnore]
    public bool OptionsSpecified { get; private set; }

    public int? WarrantyMonths
    {
        get => _warrantyMonths;
        set
        {
            _warrantyMonths = value;
            // Đánh dấu trường `warrantyMonths` có trong JSON để phân biệt không gửi và gửi giá trị null.
            WarrantyMonthsSpecified = true;
        }
    }

    [JsonIgnore]
    public bool WarrantyMonthsSpecified { get; private set; }

    public IReadOnlyList<ProductVariantRequest>? Variants
    {
        get => _variants;
        set
        {
            _variants = value;
            // Đánh dấu trường `variants` có trong JSON để phân biệt không gửi và gửi giá trị null.
            VariantsSpecified = true;
        }
    }

    [JsonIgnore]
    public bool VariantsSpecified { get; private set; }

    public IReadOnlyList<ProductImageRequest>? Images
    {
        get => _images;
        set
        {
            _images = value;
            // Đánh dấu trường `images` có trong JSON. Không gửi thì giữ ảnh cũ, gửi thì thay toàn bộ danh sách ảnh.
            ImagesSpecified = true;
        }
    }

    [JsonIgnore]
    public bool ImagesSpecified { get; private set; }
    // Giữ các trường mở rộng không thuộc mô hình để từ chối các trường cấm như tồn kho.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
