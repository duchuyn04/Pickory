namespace Pickory.Modules.Commerce.Contracts.Reading;

// Dữ liệu đánh giá hiển thị công khai không chứa thông tin định danh cá nhân của người viết.
public sealed record VisibleReview(
    string Id,
    int Rating,
    string? Body,
    string AuthorName,
    DateTime CreatedAt,
    DateTime UpdatedAt);
