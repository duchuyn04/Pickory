namespace Pickory.Modules.Catalog.Attributes;

// Lỗi của một định nghĩa thuộc tính. `Key` cho biết thuộc tính nào sai để form admin đánh dấu đúng ô.
public sealed record AttributeError(string? Key, string Message);
