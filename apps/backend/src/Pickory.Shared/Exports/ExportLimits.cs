using Pickory.Shared.Exceptions;

namespace Pickory.Shared.Exports;

public static class ExportLimits
{
    // Giới hạn dòng dữ liệu của một file. File vẫn tạo trong bộ nhớ nên cần một cận trên.
    public const int MaxRows = 50_000;

    public static AppException TooLarge()
    {
        return new AppException(
            "EXPORT_TOO_LARGE",
            422,
            "Danh sách có hơn 50.000 dòng. Hãy thu hẹp bộ lọc rồi xuất lại.");
    }

    // Route đọc `MaxRows + 1` dòng rồi gọi hàm này. Dòng thứ 50.001 chứng minh danh sách vượt giới hạn mà không cần câu `COUNT`.
    public static void EnsureWithinLimit(int rowCount)
    {
        EnsureWithinLimit(rowCount, MaxRows);
    }

    // Giới hạn đổi được qua cấu hình, để test không phải tạo 50.001 dòng. Thông báo lỗi giữ nguyên chuỗi của contract.
    public static void EnsureWithinLimit(int rowCount, int maxRows)
    {
        // Giới hạn không dương làm mọi file bị từ chối. Chặn sớm để lỗi cấu hình không hiện thành lỗi 422.
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);
        if (rowCount > maxRows)
        {
            throw TooLarge();
        }
    }
}
