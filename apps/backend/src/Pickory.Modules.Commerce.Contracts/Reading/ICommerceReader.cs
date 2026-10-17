namespace Pickory.Modules.Commerce.Contracts.Reading;

// Cổng đọc dữ liệu commerce cho module khác, thay cho việc đọc bảng trực tiếp.
public interface ICommerceReader
{
    // Lấy tóm tắt đánh giá cho danh sách mã sản phẩm gồm điểm trung bình và phân bố.
    // Sản phẩm chưa có đánh giá vẫn có mục tóm tắt với số lượng bằng 0.
    Task<IReadOnlyDictionary<string, RatingSummary>> GetRatingSummaryAsync(
        IReadOnlyList<string> productIds, CancellationToken cancellationToken);

    // Đọc danh sách đánh giá công khai của sản phẩm theo trang có phân trang con trỏ.
    // Kết quả bỏ qua các đánh giá đã bị ẩn bởi quản trị viên.
    Task<ReviewPage> GetVisibleReviewsAsync(
        string productId, int limit, string? cursor, CancellationToken cancellationToken);

    // Lấy danh sách mã sản phẩm khách hàng đã mua thành công và không bị hoàn trả.
    // Danh sách trả về không chứa phần tử trùng lặp và được sắp xếp ổn định.
    Task<IReadOnlyList<string>> GetPurchasedProductIdsAsync(
        string userId, CancellationToken cancellationToken);

    // Lấy danh sách đơn hàng gần nhất của khách hàng theo thứ tự thời gian tạo giảm dần.
    // Giới hạn số lượng được kéo về khoảng cho phép từ 1 đến 50 đơn.
    Task<IReadOnlyList<OrderSummary>> GetOrdersForCustomerAsync(
        string userId, int limit, CancellationToken cancellationToken);

    // Tra cứu chi tiết đơn hàng theo mã đơn đã chuẩn hóa không phân biệt chữ hoa thường.
    // Trả về giá trị null khi không tìm thấy đơn hàng tương ứng trong hệ thống.
    Task<OrderDetail?> GetOrderByCodeAsync(
        string code, CancellationToken cancellationToken);

    // Lấy danh sách đơn hàng đặt bởi khách vãng lai theo mã định danh ẩn danh.
    // Trả về danh sách rỗng khi mã ẩn danh truyền vào là chuỗi trống.
    Task<IReadOnlyList<OrderSummary>> GetOrdersForAnonymousAsync(
        string anonymousId, int limit, CancellationToken cancellationToken);
}
