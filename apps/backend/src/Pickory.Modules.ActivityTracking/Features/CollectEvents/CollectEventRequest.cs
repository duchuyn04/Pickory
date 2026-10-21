using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Pickory.Modules.ActivityTracking.Metrics;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.ActivityTracking.Features.CollectEvents;

// Body của `POST /api/events`. Body đúng chỉ có một thuộc tính `type` bằng `product_view`.
internal sealed record CollectEventRequest(string Type)
{
    public const string TypeField = "type";

    // Body hợp lệ chỉ vài chục byte. Giới hạn này chặn body lớn bất thường gửi tới route công khai.
    public const int MaxBodyBytes = 1024;

    private const string BodyMustBeObjectMessage = "Dữ liệu gửi lên phải là một đối tượng JSON.";
    private const string BodyTooLargeMessage = "Body vượt giới hạn 1 KB.";
    private const string TypeInvalidMessage = "Chỉ nhận type là product_view.";

    // Đọc body thô thay vì dùng model binding. Binder của minimal API ném `BadHttpRequestException` khi gặp JSON hỏng.
    public static async Task<CollectEventRequest> ReadAsync(Stream body, long? contentLength, CancellationToken cancellationToken)
    {
        // Độ dài khai báo đã vượt giới hạn thì từ chối ngay. Server không đọc body mà client đã khai là quá lớn.
        if (contentLength > MaxBodyBytes)
        {
            throw TooLarge();
        }

        // Đọc tối đa một byte quá giới hạn. Body lớn hơn giới hạn bị từ chối mà không phải đọc hết.
        var buffer = new byte[MaxBodyBytes + 1];
        var totalBytes = 0;
        while (totalBytes < buffer.Length)
        {
            var readBytes = await body.ReadAsync(buffer.AsMemory(totalBytes), cancellationToken);
            if (readBytes == 0)
            {
                break;
            }

            totalBytes += readBytes;
        }

        // Request không có `Content-Length` (gửi theo chunk) chỉ lộ độ dài thật khi đọc, nên phải kiểm lại ở đây.
        if (totalBytes > MaxBodyBytes)
        {
            throw TooLarge();
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(buffer.AsMemory(0, totalBytes));
        }
        catch (JsonException)
        {
            // Body rỗng hoặc JSON hỏng cùng trả 400. Client không nhận lỗi 500 từ bộ phân tích.
            throw Invalid(string.Empty, BodyMustBeObjectMessage);
        }

        using (document)
        {
            // Mảng, chuỗi, số và `null` không có thuộc tính để đọc, nên lỗi gắn với cả body.
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Invalid(string.Empty, BodyMustBeObjectMessage);
            }

            return ReadType(document.RootElement);
        }
    }

    private static CollectEventRequest ReadType(JsonElement root)
    {
        // Đúng một thuộc tính. Trường lạ như `productId` bị từ chối, vì bộ đếm không lưu dữ liệu nào ngoài số lượt.
        var properties = root.EnumerateObject().ToList();
        if (properties.Count != 1)
        {
            throw Invalid(TypeField, TypeInvalidMessage);
        }

        var property = properties[0];
        // So khớp `Ordinal` cho tên thuộc tính. `Type` viết hoa không được coi là `type`.
        if (!string.Equals(property.Name, TypeField, StringComparison.Ordinal) || property.Value.ValueKind != JsonValueKind.String)
        {
            throw Invalid(TypeField, TypeInvalidMessage);
        }

        // So khớp `Ordinal` cho giá trị. `PRODUCT_VIEW` không trùng khóa chỉ số đã lưu trong bảng đếm.
        var type = property.Value.GetString();
        if (!string.Equals(type, ActivityMetricNames.ProductView, StringComparison.Ordinal))
        {
            throw Invalid(TypeField, TypeInvalidMessage);
        }

        // Phép so khớp ở trên đã chứng minh `type` bằng hằng số này. Trả hằng số tránh giá trị `null` của kiểu chuỗi.
        return new CollectEventRequest(ActivityMetricNames.ProductView);
    }

    // Mã 413 khớp route webhook SePay, để client phân biệt body quá lớn với body sai nội dung.
    private static AppException TooLarge() => new("PAYLOAD_TOO_LARGE", 413, BodyTooLargeMessage);

    private static ValidationException Invalid(string key, string message) =>
        new([new ValidationFailure(key, message)]);
}
