namespace Pickory.Modules.Commerce.Contracts.Reading;

// Trang danh sách đánh giá kèm con trỏ tiếp theo để phân trang ổn định.
public sealed record ReviewPage(
    IReadOnlyList<VisibleReview> Items,
    string? NextCursor);
