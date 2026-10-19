namespace Pickory.Modules.Shipping.FallbackFees;

// Ba vùng của bảng giá GHN. Số của enum là số vùng GHN dùng, để người đọc đối chiếu với bảng giá.
internal enum FallbackFeeRegion
{
    South = 1,
    Central = 2,
    North = 3,
}

internal sealed record FallbackFeeRegionEntry(string ProvinceCode, string ProvinceName, FallbackFeeRegion Region);

// Bảng 34 tỉnh theo đơn vị hành chính từ ngày 1/7/2025. Mã tỉnh lấy từ `province/all` của GHN API v3 trên sandbox ngày 2026-10-19.
internal static class FallbackFeeRegions
{
    public static readonly IReadOnlyList<FallbackFeeRegionEntry> All =
    [
        new("1000000", "Hà Nội", FallbackFeeRegion.North),
        new("1000001", "Hồ Chí Minh", FallbackFeeRegion.South),
        new("1000002", "Huế", FallbackFeeRegion.Central),
        new("1000003", "Đà Nẵng", FallbackFeeRegion.Central),
        new("1000004", "Hải Phòng", FallbackFeeRegion.North),
        new("1000005", "Cần Thơ", FallbackFeeRegion.South),
        new("1000006", "Đồng Nai", FallbackFeeRegion.South),
        new("1000007", "Gia Lai", FallbackFeeRegion.South),
        new("1000008", "Khánh Hòa", FallbackFeeRegion.South),
        new("1000009", "Lâm Đồng", FallbackFeeRegion.South),
        new("1000010", "Đắk Lắk", FallbackFeeRegion.South),
        new("1000011", "Vĩnh Long", FallbackFeeRegion.South),
        new("1000012", "Đồng Tháp", FallbackFeeRegion.South),
        new("1000013", "An Giang", FallbackFeeRegion.South),
        new("1000014", "Tuyên Quang", FallbackFeeRegion.North),
        new("1000015", "Phú Thọ", FallbackFeeRegion.North),
        new("1000016", "Ninh Bình", FallbackFeeRegion.North),
        new("1000017", "Quảng Trị", FallbackFeeRegion.Central),
        new("1000018", "Tây Ninh", FallbackFeeRegion.South),
        new("1000019", "Quảng Ngãi", FallbackFeeRegion.Central),
        new("1000020", "Thái Nguyên", FallbackFeeRegion.North),
        new("1000021", "Bắc Ninh", FallbackFeeRegion.North),
        new("1000022", "Cà Mau", FallbackFeeRegion.South),
        new("1000023", "Hưng Yên", FallbackFeeRegion.North),
        new("1000024", "Lào Cai", FallbackFeeRegion.North),
        new("1000025", "Lai Châu", FallbackFeeRegion.North),
        new("1000026", "Điện Biên", FallbackFeeRegion.North),
        new("1000027", "Sơn La", FallbackFeeRegion.North),
        new("1000028", "Quảng Ninh", FallbackFeeRegion.North),
        new("1000029", "Hà Tĩnh", FallbackFeeRegion.North),
        new("1000030", "Thanh Hóa", FallbackFeeRegion.North),
        new("1000031", "Nghệ An", FallbackFeeRegion.North),
        new("1000032", "Cao Bằng", FallbackFeeRegion.North),
        new("1000033", "Lạng Sơn", FallbackFeeRegion.North),
    ];

    private static readonly Dictionary<string, FallbackFeeRegion> RegionByProvinceCode =
        All.ToDictionary(entry => entry.ProvinceCode, entry => entry.Region, StringComparer.Ordinal);

    // Trả `null` khi mã không có trong bảng. Nơi gọi không đoán vùng, vì đoán sai làm báo phí sai tuyến.
    public static FallbackFeeRegion? Find(string provinceCode) =>
        RegionByProvinceCode.TryGetValue(provinceCode, out var region) ? region : null;
}
