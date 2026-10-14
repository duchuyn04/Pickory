namespace Pickory.Modules.Commerce.Entities;

// Một báo cáo vi phạm của khách về một đánh giá. Mỗi khách báo cáo một đánh giá tối đa một lần.
public sealed class ReviewReport
{
    public string Id { get; set; } = null!;
    public string ReviewId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public Review? Review { get; set; }
}
