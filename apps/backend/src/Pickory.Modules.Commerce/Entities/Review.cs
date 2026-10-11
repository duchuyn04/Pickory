namespace Pickory.Modules.Commerce.Entities;

// Đánh giá sản phẩm của người đã nhận hàng. `UserId` là null sau khi tài khoản bị xóa, để giữ nội dung mà bỏ liên kết tới người viết.
public sealed class Review
{
    public string Id { get; set; } = null!;
    public string ProductId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string? Body { get; set; }
    public DateTime? HiddenAt { get; set; }
    public string? HiddenReason { get; set; }
    public int ReportCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Order? Order { get; set; }
}
