using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Catalog.Images;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Http;

namespace Pickory.Modules.Catalog.Features.Uploads;

// Dữ liệu trình duyệt cần để tải ảnh thẳng lên Cloudinary. Không có secret.
public sealed record UploadSignatureResponse(
    string CloudName,
    string ApiKey,
    string UploadUrl,
    long Timestamp,
    string Folder,
    string AllowedFormats,
    long MaxFileBytes,
    string Signature);

// Cấp chữ ký tải ảnh. Ảnh đi thẳng từ trình duyệt lên Cloudinary, không đi qua server API.
public static class UploadSignatureEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Chỉ quyền ghi sản phẩm được tải ảnh, vì chữ ký cho phép ghi vào tài khoản Cloudinary của cửa hàng.
        endpoints.MapPost("/api/admin/uploads/signature", CreateProductSignature)
            .RequirePermission(Permissions.ProductsWrite);
        // Logo thương hiệu dùng route riêng để chữ ký khóa đúng thư mục logo.
        endpoints.MapPost("/api/admin/uploads/brand-logo/signature", CreateBrandLogoSignature)
            .RequirePermission(Permissions.ProductsWrite);
    }

    private static IResult CreateProductSignature(
        CloudinaryCredentials credentials,
        TimeProvider timeProvider) => CreateSignature(credentials, timeProvider, ImageUploadRules.Folder);

    private static IResult CreateBrandLogoSignature(
        CloudinaryCredentials credentials,
        TimeProvider timeProvider) => CreateSignature(credentials, timeProvider, ImageUploadRules.BrandFolder);

    private static IResult CreateSignature(
        CloudinaryCredentials credentials,
        TimeProvider timeProvider,
        string folder)
    {
        var timestamp = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        // Route chọn thư mục cố định và ký giá trị đó. Đầu vào HTTP không được chọn thư mục tải lên.
        var signedParameters = new Dictionary<string, string>
        {
            ["allowed_formats"] = ImageUploadRules.AllowedFormatsParameter,
            ["folder"] = folder,
            ["timestamp"] = timestamp.ToString(CultureInfo.InvariantCulture),
        };
        var signature = CloudinarySignature.Sign(signedParameters, credentials.ApiSecret);

        // Cloudinary không giới hạn kích thước qua chữ ký. `MaxFileBytes` cho giao diện chặn trước, API kiểm lại khi gắn ảnh.
        var response = new UploadSignatureResponse(
            credentials.CloudName,
            credentials.ApiKey,
            $"https://api.cloudinary.com/v1_1/{credentials.CloudName}/image/upload",
            timestamp,
            folder,
            ImageUploadRules.AllowedFormatsParameter,
            ImageUploadRules.MaxBytes,
            signature);
        return Results.Ok(new ApiResponse<UploadSignatureResponse>(response));
    }
}
