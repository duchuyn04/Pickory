using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Pickory.Modules.Shipping.FallbackFees;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;

namespace Pickory.Modules.Shipping.Features.AdminSettings;

// Đọc body của `PUT /api/admin/shipping/fallback-fee` theo luật chặt, giống `UpdateShippingSettingsRequest`.
// Binder mặc định bỏ qua trường lạ và nhận số dạng chuỗi, nên lớp này đọc JSON thủ công.
internal static class UpdateFallbackFeeRequest
{
    public const string EnabledField = "enabled";
    public const string SameProvinceBaseFeeField = "sameProvinceBaseFee";
    public const string SameProvinceBaseWeightGramsField = "sameProvinceBaseWeightGrams";
    public const string SameProvinceStepFeeField = "sameProvinceStepFee";
    public const string SameRegionBaseFeeField = "sameRegionBaseFee";
    public const string SameRegionBaseWeightGramsField = "sameRegionBaseWeightGrams";
    public const string SameRegionStepFeeField = "sameRegionStepFee";
    public const string CrossRegionBaseFeeField = "crossRegionBaseFee";
    public const string CrossRegionBaseWeightGramsField = "crossRegionBaseWeightGrams";
    public const string CrossRegionStepFeeField = "crossRegionStepFee";

    // Mã lỗi 422 khi bật phí dự phòng mà thiếu giá trị. Body đúng kiểu nhưng chưa đủ để bật.
    public const string IncompleteCode = "FALLBACK_FEE_INCOMPLETE";

    // Giá gốc và giá bước từ 0 đến 5.000.000 đồng. Khối lượng gốc từ 1 đến 20.000 gram.
    public const int MaxFee = 5_000_000;
    public const int MinBaseWeightGrams = 1;
    public const int MaxBaseWeightGrams = 20_000;

    private const string FeeMessage = "Giá phải là số nguyên từ 0 đến 5.000.000 đồng.";
    private const string WeightMessage = "Khối lượng gốc phải là số nguyên từ 1 đến 20.000 gram.";
    private const string MissingMessage = "Cần nhập giá trị này khi bật phí dự phòng.";

    private static readonly string[] KnownFields =
    [
        EnabledField,
        SameProvinceBaseFeeField, SameProvinceBaseWeightGramsField, SameProvinceStepFeeField,
        SameRegionBaseFeeField, SameRegionBaseWeightGramsField, SameRegionStepFeeField,
        CrossRegionBaseFeeField, CrossRegionBaseWeightGramsField, CrossRegionStepFeeField,
    ];

    // Sai kiểu, sai khoảng hoặc trường lạ ném `ValidationException` (400). Bật mà thiếu giá trị ném `AppException` (422), và chỉ sau khi lỗi kiểu đã hết.
    public static FallbackFeeInput Parse(JsonElement? body)
    {
        var failures = new List<ValidationFailure>();
        var input = Read(body, failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        // Bật phí dự phòng thì SHIP-14 cần giá của cả 3 tuyến. Thiếu một giá trị là không tính được phí cho tuyến đó.
        if (input.Enabled)
        {
            ThrowWhenIncomplete(input);
        }

        return input;
    }

    private static FallbackFeeInput Read(JsonElement? body, List<ValidationFailure> failures)
    {
        // Body không phải đối tượng JSON thì không có trường nào để đọc tiếp.
        if (body is not { ValueKind: JsonValueKind.Object } json)
        {
            failures.Add(new ValidationFailure(string.Empty, "Dữ liệu gửi lên phải là một đối tượng JSON."));
            return new FallbackFeeInput(false, null, null, null, null, null, null, null, null, null);
        }

        // Trường lạ bị từ chối. Tên gần giống tên thật cũng bị chặn, vì binder mặc định sẽ bỏ qua âm thầm.
        foreach (var property in json.EnumerateObject())
        {
            if (!KnownFields.Contains(property.Name, StringComparer.Ordinal))
            {
                failures.Add(new ValidationFailure(property.Name, "Trường này không được hỗ trợ."));
            }
        }

        return new FallbackFeeInput(
            ReadEnabled(json, failures),
            ReadFee(json, SameProvinceBaseFeeField, failures),
            ReadBaseWeight(json, SameProvinceBaseWeightGramsField, failures),
            ReadFee(json, SameProvinceStepFeeField, failures),
            ReadFee(json, SameRegionBaseFeeField, failures),
            ReadBaseWeight(json, SameRegionBaseWeightGramsField, failures),
            ReadFee(json, SameRegionStepFeeField, failures),
            ReadFee(json, CrossRegionBaseFeeField, failures),
            ReadBaseWeight(json, CrossRegionBaseWeightGramsField, failures),
            ReadFee(json, CrossRegionStepFeeField, failures));
    }

    private static bool ReadEnabled(JsonElement json, List<ValidationFailure> failures)
    {
        // Công tắc bắt buộc và chỉ nhận `true` hoặc `false`. Chuỗi "true" hay số 1 bị từ chối.
        if (json.TryGetProperty(EnabledField, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        failures.Add(new ValidationFailure(EnabledField, "enabled là bắt buộc và phải là true hoặc false."));
        return false;
    }

    private static int? ReadFee(JsonElement json, string field, List<ValidationFailure> failures) =>
        ReadInteger(json, field, 0, MaxFee, FeeMessage, failures);

    private static int? ReadBaseWeight(JsonElement json, string field, List<ValidationFailure> failures) =>
        ReadInteger(json, field, MinBaseWeightGrams, MaxBaseWeightGrams, WeightMessage, failures);

    // Trả `null` khi trường thiếu hoặc là `null`, vì tắt thì giá được trống. Giá trị khác `null` phải là số nguyên trong khoảng.
    private static int? ReadInteger(
        JsonElement json, string field, int min, int max, string message, List<ValidationFailure> failures)
    {
        if (!json.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        // Chuỗi như "1000" bị từ chối. Số thập phân như 1500.5 không qua `TryGetInt32`.
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < min || number > max)
        {
            failures.Add(new ValidationFailure(field, message));
            return null;
        }

        return number;
    }

    private static void ThrowWhenIncomplete(FallbackFeeInput input)
    {
        var missing = new List<ErrorFieldDetail>();
        AddWhenMissing(missing, SameProvinceBaseFeeField, input.SameProvinceBaseFee);
        AddWhenMissing(missing, SameProvinceBaseWeightGramsField, input.SameProvinceBaseWeightGrams);
        AddWhenMissing(missing, SameProvinceStepFeeField, input.SameProvinceStepFee);
        AddWhenMissing(missing, SameRegionBaseFeeField, input.SameRegionBaseFee);
        AddWhenMissing(missing, SameRegionBaseWeightGramsField, input.SameRegionBaseWeightGrams);
        AddWhenMissing(missing, SameRegionStepFeeField, input.SameRegionStepFee);
        AddWhenMissing(missing, CrossRegionBaseFeeField, input.CrossRegionBaseFee);
        AddWhenMissing(missing, CrossRegionBaseWeightGramsField, input.CrossRegionBaseWeightGrams);
        AddWhenMissing(missing, CrossRegionStepFeeField, input.CrossRegionStepFee);

        if (missing.Count > 0)
        {
            throw new AppException(
                IncompleteCode, 422, "Bật phí dự phòng cần nhập đủ giá của cả 3 tuyến.", missing);
        }
    }

    private static void AddWhenMissing(List<ErrorFieldDetail> missing, string field, int? value)
    {
        if (value is null)
        {
            missing.Add(new ErrorFieldDetail(field, MissingMessage));
        }
    }
}
