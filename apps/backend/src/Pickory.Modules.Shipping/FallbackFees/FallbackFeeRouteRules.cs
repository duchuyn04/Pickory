namespace Pickory.Modules.Shipping.FallbackFees;

// Ba tuyến có giá riêng. `Unknown` nghĩa là không đủ dữ liệu để chọn tuyến.
internal enum FallbackFeeRoute
{
    Unknown,
    SameProvince,
    SameRegion,
    CrossRegion,
}

// Luật thuần chọn tuyến từ mã tỉnh lấy hàng và mã tỉnh nhận. Không đọc DB và không gọi HTTP.
internal static class FallbackFeeRouteRules
{
    public static FallbackFeeRoute Determine(string fromProvinceCode, string toProvinceCode)
    {
        // Cùng mã thì chắc chắn nội tỉnh, kể cả khi mã không có trong bảng vùng. Không cần biết vùng để kết luận.
        if (string.Equals(fromProvinceCode, toProvinceCode, StringComparison.Ordinal))
        {
            return FallbackFeeRoute.SameProvince;
        }

        var fromRegion = FallbackFeeRegions.Find(fromProvinceCode);
        var toRegion = FallbackFeeRegions.Find(toProvinceCode);
        // Thiếu vùng của một đầu thì trả `Unknown`. SHIP-14 coi đây là không tính được phí dự phòng, và không đoán tuyến.
        if (fromRegion is null || toRegion is null)
        {
            return FallbackFeeRoute.Unknown;
        }

        return fromRegion == toRegion ? FallbackFeeRoute.SameRegion : FallbackFeeRoute.CrossRegion;
    }
}
