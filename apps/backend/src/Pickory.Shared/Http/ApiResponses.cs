using System.Text.Json.Serialization;

namespace Pickory.Shared.Http;

// Cấu trúc phản hồi thành công bọc dữ liệu bên trong thuộc tính data.
public record ApiResponse<T>(T Data);

// Một lỗi của một trường trong body. `Key` là đường dẫn camelCase của trường, lỗi cả body có `Key` là `body`.
public record ErrorFieldDetail(string Key, string Message);

// Chi tiết lỗi gồm mã máy đọc được, thông điệp tiếng Việt và `Details` của lỗi kiểm tra dữ liệu.
// Giá trị `null` của `Details` bị bỏ khi ghi JSON, nên lỗi khác giữ hình dạng cũ.
public record ErrorDetail(
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ErrorFieldDetail>? Details = null);

// Cấu trúc phản hồi lỗi chuẩn của toàn bộ hệ thống API.
public record ErrorResponse(ErrorDetail Error);
