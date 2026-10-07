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
        endpoints.MapPost("/api/admin/uploads/signature", CreateSignature)
            .RequirePermission(Permissions.ProductsWrite);
    }

    private static IResult CreateSignature(CloudinaryCredentials credentials, TimeProvider timeProvider)
    {
        var timestamp = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        // Ký cả thư mục và định dạng, để trình duyệt không đổi được hai giá trị này mà chữ ký vẫn đúng.
        var signedParameters = new Dictionary<string, string>
        {
            ["allowed_formats"] = ImageUploadRules.AllowedFormatsParameter,
            ["folder"] = ImageUploadRules.Folder,
            ["timestamp"] = timestamp.ToString(CultureInfo.InvariantCulture),
        };
        var signature = CloudinarySignature.Sign(signedParameters, credentials.ApiSecret);

        // Cloudinary không giới hạn kích thước qua chữ ký. `MaxFileBytes` cho giao diện chặn trước, API kiểm lại khi gắn ảnh.
        var response = new UploadSignatureResponse(
            credentials.CloudName,
            credentials.ApiKey,
            $"https://api.cloudinary.com/v1_1/{credentials.CloudName}/image/upload",
            timestamp,
            ImageUploadRules.Folder,
            ImageUploadRules.AllowedFormatsParameter,
            ImageUploadRules.MaxBytes,
            signature);
        return Results.Ok(new ApiResponse<UploadSignatureResponse>(response));
    }
}
