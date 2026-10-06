namespace Pickory.Modules.Catalog.Specs;

// Lỗi của một giá trị thông số. `Key` là khóa thuộc tính để form admin đánh dấu đúng ô.
public sealed record SpecError(string Key, string Message);
