using System.Globalization;
using System.Text.Json;

namespace Pickory.Modules.Catalog.Images;

// Tải ảnh lên Cloudinary bằng URL nguồn. Cloudinary tự tải ảnh từ trang hãng nên máy chạy lệnh không tốn băng thông.
public sealed class CloudinaryImageUploader(HttpClient httpClient, CloudinaryCredentials credentials, TimeProvider timeProvider)
    : IImageUploader
{
    private const string ApiBaseUrl = "https://api.cloudinary.com/v1_1";

    // Giới hạn cạnh dài trước khi lưu. Ảnh gốc của hãng có thể rất lớn và vượt `ImageUploadRules.MaxBytes`.
    private const string IncomingTransformation = "c_limit,w_1600,h_1600";

    // WebP nhỏ hơn JPG cùng chất lượng và nằm trong `ImageUploadRules.AllowedFormats`.
    private const string StoredFormat = "webp";

    public async Task<ImageUploadOutcome> UploadFromUrlAsync(string publicId, string sourceUrl, CancellationToken cancellationToken)
    {
        // Mọi tham số trừ `file` và `api_key` đều nằm trong chuỗi ký. Cloudinary từ chối yêu cầu nếu thiếu một tham số.
        var signedParameters = new Dictionary<string, string>
        {
            ["public_id"] = publicId,
            // `false` để ảnh đã có trên kho không bị ghi đè khi lệnh chạy lại.
            ["overwrite"] = "false",
            ["timestamp"] = timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            ["transformation"] = IncomingTransformation,
            ["format"] = StoredFormat,
        };
        var signature = CloudinarySignature.Sign(signedParameters, credentials.ApiSecret);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>(signedParameters)
        {
            // Cloudinary nhận URL HTTPS ở trường `file` và tự tải ảnh về.
            ["file"] = sourceUrl,
            ["api_key"] = credentials.ApiKey,
            ["signature"] = signature,
        });

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync($"{ApiBaseUrl}/{credentials.CloudName}/image/upload", content, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            // Lỗi mạng được đổi thành lỗi tải ảnh để lệnh seed nêu được sản phẩm bị hỏng.
            throw new ImageUploadException($"Không gọi được Cloudinary: {exception.Message}", isTransient: true);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Hủy không do người gọi nghĩa là hết thời gian chờ của `HttpClient`.
            throw new ImageUploadException($"Cloudinary không phản hồi kịp: {exception.Message}", isTransient: true);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return ReadOutcome(body);
            }

            var statusCode = (int)response.StatusCode;
            var message = $"Cloudinary từ chối ảnh (HTTP {statusCode}): {ReadErrorMessage(body)}";
            // Cloudinary báo hết lượt bằng 420 (Admin API) hoặc 429. Lệnh seed phải dừng thay vì thử lại.
            if (statusCode is 420 or 429)
            {
                throw new ImageRateLimitedException(message);
            }

            // Lỗi 5xx là lỗi phía Cloudinary và thường qua đi. Lỗi 4xx là lỗi dữ liệu nên không thử lại.
            throw new ImageUploadException(message, isTransient: statusCode >= 500);
        }
    }

    // Với `overwrite=false`, Cloudinary trả `"existing": true` khi `publicId` đã có và không tải lại ảnh.
    private static ImageUploadOutcome ReadOutcome(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var existing = document.RootElement.TryGetProperty("existing", out var flag) && flag.ValueKind == JsonValueKind.True;
            return existing ? ImageUploadOutcome.AlreadyExisted : ImageUploadOutcome.Uploaded;
        }
        catch (JsonException)
        {
            // Phản hồi 200 không phải JSON vẫn là tải thành công. Chỉ phần đếm ảnh có sẵn bị thiếu.
            return ImageUploadOutcome.Uploaded;
        }
    }

    // Cloudinary trả lỗi dạng `{"error":{"message":"..."}}`. Thân không đúng dạng thì giữ nguyên để người chạy lệnh đọc.
    private static string ReadErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.GetProperty("error").GetProperty("message").GetString() ?? body;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return body.Length > 200 ? body[..200] : body;
        }
    }
}
