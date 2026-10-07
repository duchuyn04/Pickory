using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Pickory.Modules.Catalog.Images;

// Kho ảnh Cloudinary thật. Chỉ chạy khi cấu hình có đủ bộ ba khóa, test thường không dùng lớp này.
public sealed class CloudinaryImageStorage(HttpClient httpClient, CloudinaryCredentials credentials, TimeProvider timeProvider)
    : IImageStorage
{
    private const string ApiBaseUrl = "https://api.cloudinary.com/v1_1";

    public async Task<StoredImage?> FindAsync(string publicId, CancellationToken cancellationToken)
    {
        // Admin API đọc thông tin ảnh bằng xác thực Basic từ khóa và secret, không cần chữ ký.
        var url = $"{ApiBaseUrl}/{credentials.CloudName}/resources/image/upload/{Uri.EscapeDataString(publicId).Replace("%2F", "/")}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.ApiKey}:{credentials.ApiSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        // Ảnh chưa tải lên hoặc đã bị xóa thì Cloudinary trả 404. API coi đây là ảnh không tồn tại.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        // Lỗi khác (khóa sai, Cloudinary lỗi) phải dừng request, không được coi là ảnh hợp lệ.
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var bytes = document.RootElement.GetProperty("bytes").GetInt64();
        var format = document.RootElement.GetProperty("format").GetString() ?? string.Empty;
        return new StoredImage(bytes, format);
    }

    public async Task DeleteAsync(string publicId, CancellationToken cancellationToken)
    {
        // API `destroy` cần chữ ký trên `public_id` và `timestamp`, giống yêu cầu tải ảnh.
        var timestamp = timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signedParameters = new Dictionary<string, string>
        {
            ["public_id"] = publicId,
            ["timestamp"] = timestamp,
        };
        var signature = CloudinarySignature.Sign(signedParameters, credentials.ApiSecret);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>(signedParameters)
        {
            ["api_key"] = credentials.ApiKey,
            ["signature"] = signature,
        });

        using var response = await httpClient.PostAsync(
            $"{ApiBaseUrl}/{credentials.CloudName}/image/destroy", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var result = document.RootElement.GetProperty("result").GetString();
        // Ảnh đã không còn thì việc xóa coi như xong. Kết quả khác là lỗi để worker thử lại.
        if (result is not ("ok" or "not found"))
        {
            throw new InvalidOperationException($"Cloudinary không xóa được ảnh: {result}");
        }
    }
}
