using Pickory.Shared.Http;

namespace Pickory.Shared.Exceptions;

// Định nghĩa lớp ngoại lệ nghiệp vụ để mang mã lỗi máy đọc được và thông điệp tiếng Việt.
public class AppException : Exception
{
    // Lưu mã lỗi định danh để client phân loại và hiển thị tương ứng.
    public string Code { get; }

    // Lưu mã trạng thái HTTP để middleware trả đúng mã phản hồi mong muốn.
    public int StatusCode { get; }

    // Lưu danh sách lỗi theo mục, để lỗi như thiếu hàng chỉ ra từng mục sai. Phần lớn lỗi không có danh sách này.
    public IReadOnlyList<ErrorFieldDetail>? Details { get; }

    public AppException(string code, int statusCode, string message, IReadOnlyList<ErrorFieldDetail>? details = null)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        Details = details;
    }
}
