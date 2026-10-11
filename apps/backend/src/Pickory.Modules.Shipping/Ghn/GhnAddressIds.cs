using System.Globalization;

namespace Pickory.Modules.Shipping.Ghn;

// Đổi mã tỉnh và mã phường đã lưu (chuỗi) sang số mà GHN yêu cầu ở `to_province_id_v2` và `to_ward_id_v2`.
internal static class GhnAddressIds
{
    public static bool TryParse(string provinceCode, string wardCode, out long provinceId, out long wardId)
    {
        provinceId = 0;
        wardId = 0;

        // Chỉ nhận chữ số, không dấu và không khoảng trắng. Chuỗi như "-5" hay " 12" không phải mã GHN hợp lệ.
        var provinceParsed = long.TryParse(provinceCode, NumberStyles.None, CultureInfo.InvariantCulture, out provinceId);
        var wardParsed = long.TryParse(wardCode, NumberStyles.None, CultureInfo.InvariantCulture, out wardId);

        // Mã 0 là giá trị mặc định của GHN cho "chưa có". Gửi mã này GHN trả `mã phường mới không tồn tại: 0`.
        return provinceParsed && wardParsed && provinceId > 0 && wardId > 0;
    }
}
