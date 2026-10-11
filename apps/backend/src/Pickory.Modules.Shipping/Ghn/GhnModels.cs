using System.Globalization;
using System.Text.Json.Serialization;

namespace Pickory.Modules.Shipping.Ghn;

// Kiểu request và response của API GHN, dùng JSON snake_case qua `JsonNamingPolicy.SnakeCaseLower`. Riêng `_id` có `JsonPropertyName` vì không theo quy tắc đó.

// Vỏ phản hồi chung của GHN. `Code` khác 200 là GHN từ chối dù HTTP trả 200.
internal sealed record GhnEnvelope<T>(int Code, string? Message, T? Data);

// Tỉnh hoặc thành phố theo địa giới hai cấp. `Status` là 1 khi đang dùng, 2 khi bị tắt, 10 khi đã xóa.
internal sealed record GhnProvince([property: JsonPropertyName("_id")] long Id, string Name, int Status)
{
    // Mã lưu dạng chuỗi theo quyết định 3 của plan, nên bên dùng không phải ép kiểu.
    [JsonIgnore]
    public string Code => Id.ToString(CultureInfo.InvariantCulture);
}

// Phường hoặc xã. `ParentId` là `_id` của tỉnh.
internal sealed record GhnWard([property: JsonPropertyName("_id")] long Id, string Name, long ParentId, int Status)
{
    [JsonIgnore]
    public string Code => Id.ToString(CultureInfo.InvariantCulture);
}

// Yêu cầu báo phí với địa chỉ nhận 2 cấp. GHN chỉ nhận địa chỉ mới qua `is_new_to_address`, `to_province_id_v2` và `to_ward_id_v2` dạng số (ADR 0003).
internal sealed record GhnFeeRequest(
    int Weight,
    int Length,
    int Width,
    int Height,
    int ServiceTypeId,
    int InsuranceValue,
    int CodValue,
    long ToProvinceIdV2,
    long ToWardIdV2)
{
    // Luôn bật cờ. Thiếu cờ thì GHN đọc mã theo danh mục cũ và trả `to district not found`.
    public bool IsNewToAddress { get; init; } = true;
}

internal sealed record GhnFee(int Total, int ServiceFee, int InsuranceFee, int CodFee);

// Yêu cầu thời gian giao dự kiến, bỏ các trường `from_*` vì GHN dùng địa chỉ lấy hàng của shop.
// Cùng 3 trường địa chỉ như `GhnFeeRequest` (ADR 0003, tiêu chí 4).
internal sealed record GhnLeadTimeRequest(
    long ToProvinceIdV2,
    long ToWardIdV2,
    int ServiceTypeId,
    int Weight,
    int Length,
    int Width,
    int Height)
{
    // Luôn bật cờ. Thiếu cờ thì GHN bắt buộc `to_district_id` của danh mục cũ.
    public bool IsNewToAddress { get; init; } = true;
}

// `Leadtime` là số giây Unix của thời điểm giao dự kiến. Thiếu trường này thì giá trị là `null`.
internal sealed record GhnLeadTime(long? Leadtime);

// Yêu cầu tạo đơn với địa chỉ nhận hai cấp. Tên tỉnh và tên phường là chuỗi `name` của danh mục mới, và không có quận hoặc huyện.
internal sealed record GhnCreateOrderRequest(
    string ToName,
    string ToPhone,
    string ToAddress,
    string ToProvinceName,
    string ToWardName,
    string ClientOrderCode,
    int Weight,
    int Length,
    int Width,
    int Height,
    int ServiceTypeId,
    int PaymentTypeId,
    int CodAmount,
    int InsuranceValue,
    string Content)
{
    public bool IsNewToAddress { get; init; } = true;
    public string RequiredNote { get; init; } = GhnRequiredNote.InspectWithoutTrying;
    public string? Note { get; init; }

    // Địa chỉ lấy hàng hai cấp theo tài liệu GHN `/en/docs/order/create`, để `null` thì GHN dùng địa chỉ của shop.
    // Sandbox xác nhận tạo đơn không gửi các trường này vẫn chạy và GHN dùng địa chỉ của shop (ADR 0003).
    public string? FromName { get; init; }
    public string? FromPhone { get; init; }
    public string? FromAddress { get; init; }
    public string? FromProvinceName { get; init; }
    public string? FromWardName { get; init; }
    public bool? IsNewFromAddress { get; init; }

    // Danh sách kiện hàng. Chỉ gói `service_type_id = 5` bắt buộc, các gói khác để `null` và GHN bỏ qua trường này.
    public IReadOnlyList<GhnOrderItem>? Items { get; init; }
}

// Một kiện hàng của đơn. GHN tính phí gói hàng nặng theo số đo và cân nặng của từng kiện.
internal sealed record GhnOrderItem(string Name, int Quantity, int Weight, int Length, int Width, int Height);

internal sealed record GhnCreatedOrder(string OrderCode, int TotalFee, string? ExpectedDeliveryTime);

internal sealed record GhnOrderDetail(string OrderCode, string? ClientOrderCode, string Status, string? UpdatedDate);

// Kết quả hủy của một mã đơn trong lô.
internal sealed record GhnCancelResult(string OrderCode, bool Result, string? Message);

// Giá trị `required_note` mà GHN nhận.
internal static class GhnRequiredNote
{
    public const string NoInspection = "KHONGCHOXEMHANG";
    public const string InspectWithoutTrying = "CHOXEMHANGKHONGTHU";
    public const string InspectAndTry = "CHOTHUHANG";
}

// Giá trị `reason_code` mà API hủy đơn nhận.
internal static class GhnCancelReason
{
    public const string PickupTooSlow = "GHN-CO001";
    public const string OutOfStock = "GHN-CO002";
    public const string CustomerChangedMind = "GHN-CO003";
    public const string Other = "GHN-CANCEL-OTHER";
}
