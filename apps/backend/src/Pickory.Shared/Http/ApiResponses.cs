namespace Pickory.Shared.Http;

// Cấu trúc phản hồi thành công bọc dữ liệu bên trong thuộc tính data.
public record ApiResponse<T>(T Data);

// Cấu trúc chi tiết lỗi gồm mã máy đọc được và thông điệp tiếng Việt.
public record ErrorDetail(string Code, string Message);

// Cấu trúc phản hồi lỗi chuẩn của toàn bộ hệ thống API.
public record ErrorResponse(ErrorDetail Error);
