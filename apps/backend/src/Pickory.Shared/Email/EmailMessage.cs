namespace Pickory.Shared.Email;

// `Metadata` chứa dữ liệu cho máy đọc, ví dụ token gốc. Test và môi trường dev lấy link mà không phải cắt chuỗi nội dung thư.
public sealed record EmailMessage(
    string To,
    string Subject,
    string Text,
    IReadOnlyDictionary<string, string> Metadata);
