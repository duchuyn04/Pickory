namespace Pickory.Modules.Shipping.Entities;

// Phí GHN đã tính thành công, giữ để dùng lại khi GHN không phản hồi. Sáu cột đầu là khóa: cùng bộ khóa thì chỉ có một dòng.
public sealed class ShippingQuoteCache
{
    // Mã phường nhận và mã phường lấy hàng của danh mục địa chỉ. Đổi địa chỉ lấy hàng thì khóa không còn khớp.
    public string ReceiverWardCode { get; set; } = string.Empty;
    public string PickupWardCode { get; set; } = string.Empty;
    public int ServiceTypeId { get; set; }
    // Khối lượng tính cước theo gam, đã làm tròn lên bội số của 500. Dùng `long` vì kiện lớn có cân quy đổi vượt `int`.
    public long ChargeableWeightGrams { get; set; }
    // Giá trị khai giá đã kẹp trần và tiền thu hộ. GHN tính phí bảo hiểm và phí thu hộ theo hai số này.
    public int InsuredValue { get; set; }
    public int CodAmount { get; set; }
    // Phí GHN trả về, tính bằng đồng.
    public int Fee { get; set; }
    // Thời điểm lưu. Phí chỉ còn hạn dùng khi chưa đủ 24 giờ tính từ mốc này.
    public DateTime SavedAt { get; set; }
}
