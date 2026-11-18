using System.Globalization;
using Pickory.Shared.Calendar;

namespace Pickory.Shared.Exports;

public static class ExportFileName
{
    // Tạo tên `<prefix>-yyyyMMdd-HHmm.xlsx` theo giờ Việt Nam. Bên gọi truyền thời điểm vào để test không phụ thuộc giờ máy.
    public static string Create(string prefix, DateTimeOffset utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        // Đi qua `VietnamCalendar` để cả hệ thống dùng một định nghĩa giờ Việt Nam.
        var vietnamTime = VietnamCalendar.ToVietnamDateTime(utcNow.UtcDateTime);
        // Dùng `InvariantCulture` để tên file không đổi theo ngôn ngữ của máy chủ.
        var stamp = vietnamTime.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        return $"{prefix}-{stamp}.xlsx";
    }
}
