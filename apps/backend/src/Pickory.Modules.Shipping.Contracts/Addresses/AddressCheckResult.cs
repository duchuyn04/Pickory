namespace Pickory.Modules.Shipping.Contracts.Addresses;

// Ba kết quả kiểm địa chỉ nhận với đơn vị vận chuyển.
public enum AddressCheckOutcome
{
    // Đơn vị vận chuyển nhận địa chỉ này.
    Valid,

    // Đơn vị vận chuyển từ chối địa chỉ. `Reason` cho biết lý do.
    Rejected,

    // Không biết đúng hay sai vì đơn vị vận chuyển lỗi, hết thời gian chờ, hoặc shipping chưa cấu hình. Bên gọi nên cho đi tiếp.
    Unverifiable,
}

// Lý do địa chỉ nhận bị từ chối. Bên gọi tự viết câu cho khách theo từng lý do.
public enum AddressRejectionReason
{
    // Tỉnh hoặc phường đã chọn không còn trong danh mục đang dùng của shipping.
    NotInCatalog,

    // Tên đường không thuộc phường/xã đã chọn.
    StreetNotInWard,
}

// Kết quả kiểm địa chỉ nhận. `Reason` chỉ có giá trị khi `Outcome` là `Rejected`.
public sealed record AddressCheckResult(AddressCheckOutcome Outcome, AddressRejectionReason? Reason)
{
    public static AddressCheckResult Valid { get; } = new(AddressCheckOutcome.Valid, null);

    public static AddressCheckResult Unverifiable { get; } = new(AddressCheckOutcome.Unverifiable, null);

    public static AddressCheckResult Rejected(AddressRejectionReason reason) => new(AddressCheckOutcome.Rejected, reason);
}
