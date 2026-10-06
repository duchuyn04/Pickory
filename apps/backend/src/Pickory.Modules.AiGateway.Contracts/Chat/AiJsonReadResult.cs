namespace Pickory.Modules.AiGateway.Contracts.Chat;

// Kết quả đọc JSON của model thành kiểu C#. `Details` có dạng `đường dẫn: lỗi` khi đọc thất bại.
public sealed record AiJsonReadResult<T>(bool Succeeded, T? Value, string? Details);
