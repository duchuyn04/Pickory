using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;

namespace Pickory.Modules.Commerce.Carts;

// Giá trị đã kiểm của body `POST /api/cart/items`. `Source` là `null` khi client không gửi hoặc gửi chuỗi trắng.
public sealed record AddCartItemInput(string SkuId, int Quantity, string? Source);

// Đọc body của route giỏ hàng theo luật chặt. Binder mặc định nhận số dạng chuỗi và số thập phân, nên lớp này đọc JSON thủ công.
public static class CartRequests
{
    public const string SkuIdField = "skuId";
    public const string QuantityField = "quantity";
    public const string SourceField = "source";

    public const int SourceMaxLength = 64;

    private const string BodyMustBeObjectMessage = "Dữ liệu gửi lên phải là một đối tượng JSON.";
    private const string SkuIdMessage = "Cần chọn một sản phẩm hợp lệ.";
    private const string QuantityMessage = "Số lượng phải là số nguyên từ 1 trở lên.";
    private const string SourceMessage = "Nguồn phải là chuỗi tối đa 64 ký tự.";

    // Ném `ValidationException` gom mọi lỗi của body. Route gọi hàm này trước khi mở transaction hay gọi catalog.
    public static AddCartItemInput ReadAddItem(JsonElement? body)
    {
        var failures = new List<ValidationFailure>();
        var json = RequireObject(body, failures);

        var skuId = ReadSkuId(json, failures);
        var quantity = ReadQuantity(json, failures);
        var source = ReadSource(json, failures);

        ThrowIfAny(failures);
        return new AddCartItemInput(skuId, quantity, source);
    }

    // Body của `PATCH` chỉ có `quantity`. Trả số lượng mới đã kiểm.
    public static int ReadQuantityChange(JsonElement? body)
    {
        var failures = new List<ValidationFailure>();
        var json = RequireObject(body, failures);

        var quantity = ReadQuantity(json, failures);

        ThrowIfAny(failures);
        return quantity;
    }

    private static JsonElement? RequireObject(JsonElement? body, List<ValidationFailure> failures)
    {
        // Body không phải đối tượng JSON thì không có trường nào để đọc tiếp. Lỗi gắn với cả body.
        if (body is { ValueKind: JsonValueKind.Object } json)
        {
            return json;
        }

        failures.Add(new ValidationFailure(string.Empty, BodyMustBeObjectMessage));
        return null;
    }

    private static string ReadSkuId(JsonElement? json, List<ValidationFailure> failures)
    {
        if (json is null)
        {
            return string.Empty;
        }

        // Chỉ nhận chuỗi không rỗng. Số hoặc `null` không đại diện cho id SKU nào.
        if (json.Value.TryGetProperty(SkuIdField, out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString()))
        {
            return value.GetString()!;
        }

        failures.Add(new ValidationFailure(SkuIdField, SkuIdMessage));
        return string.Empty;
    }

    private static int ReadQuantity(JsonElement? json, List<ValidationFailure> failures)
    {
        if (json is null)
        {
            return 0;
        }

        // Chỉ nhận số JSON nguyên từ 1 trở lên. Chuỗi "2" và số thập phân 1.5 bị từ chối, vì client phải gửi đúng kiểu.
        if (json.Value.TryGetProperty(QuantityField, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var quantity)
            && quantity >= 1)
        {
            return quantity;
        }

        failures.Add(new ValidationFailure(QuantityField, QuantityMessage));
        return 0;
    }

    private static string? ReadSource(JsonElement? json, List<ValidationFailure> failures)
    {
        // Thiếu trường hoặc `null` nghĩa là client không báo nguồn.
        if (json is null
            || !json.Value.TryGetProperty(SourceField, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            failures.Add(new ValidationFailure(SourceField, SourceMessage));
            return null;
        }

        // Cắt khoảng trắng trước khi kiểm độ dài, vì giá trị lưu vào sự kiện là chuỗi đã cắt.
        var source = value.GetString()!.Trim();
        if (source.Length == 0)
        {
            return null;
        }

        if (source.Length > SourceMaxLength)
        {
            failures.Add(new ValidationFailure(SourceField, SourceMessage));
            return null;
        }

        return source;
    }

    private static void ThrowIfAny(List<ValidationFailure> failures)
    {
        // Gom mọi lỗi vào một phản hồi, để UI gán lỗi vào đúng ô.
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }
}
