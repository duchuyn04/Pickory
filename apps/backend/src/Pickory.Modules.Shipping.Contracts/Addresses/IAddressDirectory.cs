namespace Pickory.Modules.Shipping.Contracts.Addresses;

// Tra danh mục địa chỉ cho module khác. Module khác không đọc bảng địa chỉ của shipping trực tiếp.
public interface IAddressDirectory
{
    // Trả `null` khi tỉnh hoặc phường không có, phường không thuộc tỉnh, hoặc một trong hai đang tắt.
    Task<AddressNames?> FindAsync(string provinceCode, string wardCode, CancellationToken cancellationToken);
}
