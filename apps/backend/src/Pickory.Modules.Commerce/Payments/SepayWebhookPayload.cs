using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Commerce.Payments;

internal sealed record SepayWebhookPayload(
    long Id, int Amount, string Content, string TransferType, string AccountNumber, JsonDocument Raw)
{
    private const int MaxBodyBytes = 16 * 1024;
    private static readonly string[] RequiredStrings =
        ["gateway", "transactionDate", "accountNumber", "subAccount", "content", "transferType", "description", "referenceCode"];

    public static async Task<SepayWebhookPayload> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        // Kiểm độ dài đã biết trước để không cấp phát bộ đệm cho request chắc chắn quá giới hạn.
        if (request.ContentLength > MaxBodyBytes)
        {
            throw TooLarge();
        }
        // Một byte dư phát hiện body vượt giới hạn, kể cả khi request không có `Content-Length`.
        var buffer = new byte[MaxBodyBytes + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            // Đọc có giới hạn ngăn body không tin cậy chiếm bộ nhớ không giới hạn.
            var read = await request.Body.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0)
            {
                break;
            }
            length += read;
        }
        // Giới hạn body đứng trước `Content-Type` theo thứ tự kiểm request đã chốt.
        if (length > MaxBodyBytes)
        {
            throw TooLarge();
        }
        if (!request.HasJsonContentType())
        {
            throw new AppException("UNSUPPORTED_MEDIA_TYPE", 415, "Webhook chỉ nhận JSON.");
        }
        JsonDocument document;
        try
        {
            // Chỉ parse sau khi body và `Content-Type` hợp lệ, trước mọi lần ghi DB.
            document = JsonDocument.Parse(buffer.AsMemory(0, length));
        }
        catch (JsonException)
        {
            throw Invalid("Body không phải JSON hợp lệ.");
        }
        try
        {
            var root = document.RootElement;
            // Chỉ nhận đối tượng đủ 12 trường theo hợp đồng, không tự suy ra trường thiếu.
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("Body phải là đối tượng JSON.");
            }
            foreach (var field in RequiredStrings)
            {
                if (!root.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String)
                {
                    throw Invalid($"Trường {field} phải là chuỗi.");
                }
            }
            // `code` được phép null, nhưng vẫn phải có mặt vì khác với trường bị thiếu.
            if (!root.TryGetProperty("code", out var code) || code.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                throw Invalid("Trường code phải là chuỗi hoặc null.");
            }
            // ID và số dư là số nguyên, còn `amount` phải vừa cột integer và luôn dương.
            if (!TryInteger(root, "id", out var id) || !TryInteger(root, "accumulated", out _)
                || !TryInteger(root, "transferAmount", out var amount) || amount < 1 || amount > int.MaxValue)
            {
                throw Invalid("ID, số dư hoặc số tiền không hợp lệ.");
            }
            return new SepayWebhookPayload(id, (int)amount, root.GetProperty("content").GetString()!,
                root.GetProperty("transferType").GetString()!, root.GetProperty("accountNumber").GetString()!, document);
        }
        catch
        {
            // Payload sai không được giữ tài nguyên JSON sau khi trả lỗi.
            document.Dispose();
            throw;
        }
    }

    private static bool TryInteger(JsonElement root, string field, out long value)
    {
        value = 0;
        return root.TryGetProperty(field, out var element) && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt64(out value);
    }

    private static AppException TooLarge() => new("PAYLOAD_TOO_LARGE", 413, "Body vượt giới hạn 16 KB.");
    private static ValidationException Invalid(string message) => new([new ValidationFailure("body", message)]);
}
