namespace Pickory.Modules.Catalog.Contracts.Events;

// Loại thay đổi của sản phẩm. Module nghe sự kiện chọn việc cần làm theo loại này.
public enum ProductChange
{
    Created,
    Updated,
    Status,
    Deleted,
}
