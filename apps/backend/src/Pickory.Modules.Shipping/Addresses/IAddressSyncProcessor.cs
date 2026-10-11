namespace Pickory.Modules.Shipping.Addresses;

// Một lần đồng bộ danh mục địa chỉ từ GHN. Worker nền và lệnh dòng lệnh cùng gọi cổng này, và test thay được bằng bản giả.
internal interface IAddressSyncProcessor
{
    // Lỗi từ GHN và danh sách tỉnh/thành rỗng trả kết quả thất bại. Lỗi DB và lỗi khác ném ra cho bên gọi.
    Task<AddressSyncResult> SyncAsync(CancellationToken cancellationToken);
}
