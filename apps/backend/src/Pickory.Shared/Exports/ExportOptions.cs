using Microsoft.Extensions.Configuration;
using Pickory.Shared.Configuration;

namespace Pickory.Shared.Exports;

// Cấu hình của các route xuất file, đọc một lần khi khởi động từ mục `Exports`.
// `MaxRows` chỉ dành cho test và môi trường đặc biệt, vì thông báo lỗi 422 luôn ghi "50.000" theo contract.
public sealed record ExportOptions
{
    // Khóa cấu hình của giới hạn dòng. Biến môi trường tương ứng là `Exports__MaxRows`.
    public const string MaxRowsKey = "Exports:MaxRows";

    // Cận trên nhỏ hơn `int.MaxValue` một đơn vị. Route đọc `MaxRows + 1` dòng nên phép cộng không được tràn `int`.
    private const int MaxRowsUpperBound = int.MaxValue - 1;

    // Số dòng dữ liệu tối đa của một file, mặc định là giới hạn của spec.
    // Chỉ `Load` đảm bảo khoảng 1 tới `int.MaxValue - 1`, còn code gán trực tiếp `MaxRows` tự chịu trách nhiệm.
    public int MaxRows { get; init; } = ExportLimits.MaxRows;

    public static ExportOptions Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Dùng lại hàm đọc số của `AppConfig`. Khóa không đặt thì dùng giới hạn của spec, giá trị sai làm API dừng khi khởi động.
        var maxRows = AppConfig.ReadOptionalIntegerInRange(configuration, MaxRowsKey, 1, MaxRowsUpperBound);
        return new ExportOptions { MaxRows = maxRows ?? ExportLimits.MaxRows };
    }
}
