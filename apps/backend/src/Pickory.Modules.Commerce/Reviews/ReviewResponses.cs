namespace Pickory.Modules.Commerce.Reviews;

// Tóm tắt các đánh giá đang hiện của một sản phẩm, trong đó `Average` là `null` khi chưa có đánh giá.
// `Distribution` có đủ năm khóa từ 1 đến 5, mỗi khóa là số đánh giá của mức sao đó.
public sealed record ProductReviewSummaryResponse(double? Average, int Count, IReadOnlyDictionary<string, int> Distribution);

// Một dòng đánh giá công khai. Không có `userId`, email, số điện thoại hay `orderId` của người viết.
public sealed record PublicReviewResponse(
    string Id, int Rating, string? Body, string AuthorName, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record ProductReviewListResponse(
    ProductReviewSummaryResponse Summary, IReadOnlyList<PublicReviewResponse> Items, string? NextCursor);

// Đánh giá của chính khách, trong đó `Hidden` báo đánh giá đang bị ẩn và `EditableUntil` là hạn sửa.
public sealed record OwnReviewResponse(
    string Id, int Rating, string? Body, bool Hidden, DateTime CreatedAt, DateTime UpdatedAt, DateTime EditableUntil);

// Trạng thái viết đánh giá của khách cho một sản phẩm. `Reason` là mã ngắn khi `Eligible` sai, và `null` khi đúng.
public sealed record MyReviewStatusResponse(bool Eligible, string? Reason, OwnReviewResponse? Review);

public sealed record ReviewReportResponse(string ReviewId, DateTime CreatedAt);
