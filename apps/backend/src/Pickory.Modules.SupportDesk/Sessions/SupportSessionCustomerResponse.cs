namespace Pickory.Modules.SupportDesk.Sessions;

// Thông tin khách hàng phục vụ bảng thông tin khách trên màn hình hỗ trợ CSKH.
public sealed record SupportSessionCustomerInfo(
    bool IsGuest,
    string? Name,
    string? Email);

// Tóm tắt thông tin đơn hàng của khách hàng trong bảng thông tin khách.
public sealed record SupportSessionCustomerOrder(
    string Code,
    string Status,
    int Total,
    int ItemCount,
    DateTime CreatedAt);

// Dữ liệu phản hồi của bảng thông tin khách hàng cho phiên hỗ trợ.
public sealed record SupportSessionCustomerPanelResponse(
    SupportSessionCustomerInfo Customer,
    IReadOnlyList<SupportSessionCustomerOrder> Orders);
