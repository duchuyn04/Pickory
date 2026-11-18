namespace Pickory.Shared.Exports;

// Kiểu ô của một cột xuất file. Kiểu quyết định cách bộ ghi lưu giá trị và định dạng hiển thị.
public enum ExportCellKind
{
    Text,
    Integer,
    Money,
    DateTime,
    YesNo,
}
