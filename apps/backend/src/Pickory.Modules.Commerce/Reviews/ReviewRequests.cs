using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;

namespace Pickory.Modules.Commerce.Reviews;

// Giá trị đã kiểm của body `POST /api/products/{productId}/reviews`. `Body` là `null` khi khách không viết nhận xét.
internal sealed record ReviewInput(int Rating, string? Body);

// Giá trị đã kiểm của body `PATCH .../reviews/mine`. `HasBody` phân biệt "không gửi `body`" với "gửi `body: null` để xóa".
internal sealed record ReviewPatch(int? Rating, bool HasBody, string? Body);

// Đọc body của hai route ghi đánh giá theo luật chặt. Binder mặc định nhận trường lạ và kiểu sai, nên lớp này đọc JSON thủ công.
internal static class ReviewRequests
{
    public const string RatingField = "rating";
    public const string BodyField = "body";
    public const int MinRating = 1;
    public const int MaxRating = 5;
    public const int BodyMaxLength = 2000;

    private const string BodyMustBeObjectMessage = "Dữ liệu gửi lên phải là một đối tượng JSON.";
    private const string UnknownFieldMessage = "Trường này không được hỗ trợ.";
    private const string RatingMessage = "Số sao phải là số nguyên từ 1 đến 5.";
    private const string BodyMessage = "Nhận xét là chuỗi tối đa 2.000 ký tự.";
    private const string EmptyPatchMessage = "Cần gửi ít nhất một trường cần sửa.";

    // Ném `ValidationException` gom mọi lỗi của body. Route gọi hàm này trước khi đọc DB hay gọi catalog.
    public static ReviewInput ReadCreate(JsonElement? body)
    {
        var failures = new List<ValidationFailure>();
        var json = RequireObject(body, failures);
        ReportUnknownFields(json, failures);

        // Số sao bắt buộc khi viết đánh giá mới. Thiếu trường là lỗi, không có giá trị mặc định.
        var rating = 0;
        if (!json.TryGetProperty(RatingField, out var ratingElement) || !TryReadRating(ratingElement, out rating))
        {
            failures.Add(new ValidationFailure(RatingField, RatingMessage));
        }

        var reviewBody = ReadBody(json, failures);
        ThrowIfAny(failures);
        return new ReviewInput(rating, reviewBody);
    }

    // Ném `ValidationException` gom mọi lỗi của body. Chỉ trường có trong body mới được đọc và đổi.
    public static ReviewPatch ReadPatch(JsonElement? body)
    {
        var failures = new List<ValidationFailure>();
        var json = RequireObject(body, failures);
        ReportUnknownFields(json, failures);

        // Trường có mặt thì phải hợp lệ như lúc tạo. `rating: null` là lỗi, vì số sao không có trạng thái trống.
        int? rating = null;
        if (json.TryGetProperty(RatingField, out var ratingElement))
        {
            if (TryReadRating(ratingElement, out var value))
            {
                rating = value;
            }
            else
            {
                failures.Add(new ValidationFailure(RatingField, RatingMessage));
            }
        }

        var hasBody = json.TryGetProperty(BodyField, out _);
        var reviewBody = hasBody ? ReadBody(json, failures) : null;

        // Body không có trường nào để đổi thì request không có ý nghĩa. Từ chối thay vì trả 200 mà không đổi gì.
        if (rating is null && !hasBody && failures.Count == 0)
        {
            failures.Add(new ValidationFailure(string.Empty, EmptyPatchMessage));
        }

        ThrowIfAny(failures);
        return new ReviewPatch(rating, hasBody, reviewBody);
    }

    private static JsonElement RequireObject(JsonElement? body, List<ValidationFailure> failures)
    {
        // Body không phải đối tượng JSON thì không có trường nào để đọc tiếp. Lỗi gắn với cả body.
        if (body is { ValueKind: JsonValueKind.Object } json)
        {
            return json;
        }

        failures.Add(new ValidationFailure(string.Empty, BodyMustBeObjectMessage));
        throw new ValidationException(failures);
    }

    // Trường lạ bị từ chối, để client gõ sai tên trường nhận lỗi thay vì bị bỏ qua âm thầm.
    private static void ReportUnknownFields(JsonElement json, List<ValidationFailure> failures)
    {
        foreach (var property in json.EnumerateObject())
        {
            if (property.Name != RatingField && property.Name != BodyField)
            {
                failures.Add(new ValidationFailure(property.Name, UnknownFieldMessage));
            }
        }
    }

    private static bool TryReadRating(JsonElement element, out int rating)
    {
        // Chỉ nhận số nguyên JSON trong khoảng 1 đến 5. Chuỗi như `"5"` và số thập phân như `3.5` không phải ý rõ ràng.
        rating = 0;
        return element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out rating)
            && rating is >= MinRating and <= MaxRating;
    }

    private static string? ReadBody(JsonElement json, List<ValidationFailure> failures)
    {
        // Nhận xét là tùy chọn. Thiếu hoặc `null` nghĩa là khách không viết gì.
        if (!json.TryGetProperty(BodyField, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            failures.Add(new ValidationFailure(BodyField, BodyMessage));
            return null;
        }

        // Cắt khoảng trắng trước khi kiểm độ dài. Nhận xét toàn khoảng trắng lưu thành `null`.
        var text = element.GetString()!.Trim();
        if (text.Length > BodyMaxLength)
        {
            failures.Add(new ValidationFailure(BodyField, BodyMessage));
            return null;
        }

        return text.Length == 0 ? null : text;
    }

    private static void ThrowIfAny(List<ValidationFailure> failures)
    {
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }
}
