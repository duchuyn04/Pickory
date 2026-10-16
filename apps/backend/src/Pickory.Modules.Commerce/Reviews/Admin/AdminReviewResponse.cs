namespace Pickory.Modules.Commerce.Reviews.Admin;

// Một dòng của hàng chờ kiểm duyệt, không có `userId`, email, số điện thoại hay `orderId` của người viết.
// `ProductName` là `null` khi catalog không còn sản phẩm đó.
public sealed record AdminReviewResponse(
    string Id,
    string ProductId,
    string? ProductName,
    int Rating,
    string? Body,
    string AuthorName,
    int ReportCount,
    bool Hidden,
    string? HiddenReason,
    DateTime? HiddenAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);
