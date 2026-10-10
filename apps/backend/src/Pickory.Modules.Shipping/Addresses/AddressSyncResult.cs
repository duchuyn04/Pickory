namespace Pickory.Modules.Shipping.Addresses;

// Kết quả một lần đồng bộ danh mục địa chỉ. Số phường/xã không gồm phường/xã của tỉnh/thành đã tắt, vì bộ đồng bộ không đọc chúng.
internal sealed record AddressSyncResult(bool Succeeded, int ProvinceCount, int WardCount, string? FailureReason)
{
    public static AddressSyncResult Success(int provinceCount, int wardCount) => new(true, provinceCount, wardCount, null);

    public static AddressSyncResult Failure(string reason) => new(false, 0, 0, reason);
}
