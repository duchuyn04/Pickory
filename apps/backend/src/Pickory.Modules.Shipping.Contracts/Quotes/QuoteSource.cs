namespace Pickory.Modules.Shipping.Contracts.Quotes;

// Nguồn của phí trong `Quote`. Bên gọi dùng giá trị này để biết phí có phải số GHN vừa tính hay không.
public enum QuoteSource
{
    // GHN vừa tính phí cho đúng yêu cầu này.
    Ghn,

    // GHN không phản hồi, nên module dùng phí GHN đã tính trước đó cho cùng điều kiện và còn hạn.
    Cached,

    // GHN không phản hồi và không có phí đã lưu, nên module tính phí theo bảng phí dự phòng của admin.
    Fallback,
}
