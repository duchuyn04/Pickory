using Microsoft.EntityFrameworkCore;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;

namespace Pickory.Modules.SupportDesk.Conversations;

// Kết quả một vòng bảo trì hội thoại CSKH: số hội thoại quá hạn đã xóa và số hội thoại im lặng đã đóng.
internal sealed record SupportConversationMaintenanceResult(int ExpiredDeleted, int InactiveClosed);

// Cổng bảo trì hội thoại CSKH để worker nền gọi định kỳ và test giả lập được.
internal interface ISupportConversationMaintenanceProcessor
{
    Task<SupportConversationMaintenanceResult> MaintainConversationsAsync(CancellationToken cancellationToken);
}

// Bảo trì hội thoại CSKH hằng ngày: xóa hội thoại cũ hơn 90 ngày và đóng hội thoại mở im lặng quá 24 giờ.
internal sealed class SupportConversationMaintenanceProcessor(
    SupportDeskDbContext database,
    TimeProvider timeProvider) : ISupportConversationMaintenanceProcessor
{
    public const int InactiveHours = 24;
    public const int RetentionDays = 90;

    // Mốc biên 24 giờ tính từ hiện tại. Chỉ hội thoại có tin nhắn cuối trước mốc này mới bị đóng.
    public static DateTime GetInactiveCutoffUtc(DateTime nowUtc) => nowUtc.AddHours(-InactiveHours);

    // Mốc biên 90 ngày tính từ hiện tại. Chỉ hội thoại có tin nhắn cuối trước mốc này mới bị xóa.
    public static DateTime GetRetentionCutoffUtc(DateTime nowUtc) => nowUtc.AddDays(-RetentionDays);

    public async Task<SupportConversationMaintenanceResult> MaintainConversationsAsync(CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var retentionCutoff = GetRetentionCutoffUtc(nowUtc);
        var inactiveCutoff = GetInactiveCutoffUtc(nowUtc);

        // Xóa hội thoại quá hạn trước để không cập nhật thừa trạng thái cho dòng sắp bị xóa.
        // Khóa ngoại cascade ở DB sẽ tự động xóa tin nhắn và phiên, đồng thời gỡ liên kết yêu cầu gọi lại.
        var expiredDeleted = await database.Conversations
            .Where(conversation => conversation.LastMessageAt < retentionCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        // Đóng các hội thoại đang mở im lặng quá 24 giờ. Cập nhật thời điểm đóng là mốc hiện tại.
        var inactiveClosed = await database.Conversations
            .Where(conversation => conversation.Status == SupportConversationStatus.Open
                && conversation.LastMessageAt < inactiveCutoff)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(conversation => conversation.Status, SupportConversationStatus.Closed)
                    .SetProperty(conversation => conversation.ClosedAt, nowUtc),
                cancellationToken);

        return new SupportConversationMaintenanceResult(expiredDeleted, inactiveClosed);
    }
}
