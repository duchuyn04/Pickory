using Microsoft.EntityFrameworkCore;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;

namespace Pickory.Modules.SupportDesk.Sessions;

// Đọc danh sách phiên hỗ trợ theo chế độ hàng chờ hoặc phiên của nhân viên.
internal sealed class SupportSessionQueueService(
    SupportDeskDbContext database,
    TimeProvider timeProvider)
{
    // Lấy danh sách phiên đang chờ hoặc danh sách phiên mà nhân viên hiện tại đang phụ trách.
    public async Task<IReadOnlyList<SupportSessionQueueItemResponse>> ListAsync(
        string view, string staffId, CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        // Lọc phiên theo chế độ xem và sắp xếp theo thứ tự chuyển giao hoặc thời điểm nhận.
        IQueryable<SupportSession> sessionsQuery = database.Sessions.AsNoTracking();
        if (view == "waiting")
        {
            // Chế độ waiting chỉ lấy phiên chưa có người nhận và chưa kết thúc.
            sessionsQuery = sessionsQuery
                .Where(session => session.StaffId == null && session.ClosedAt == null)
                .OrderBy(session => session.HandoffAt)
                .ThenBy(session => session.Id);
        }
        else
        {
            // Chế độ mine chỉ lấy phiên của chính nhân viên gọi và chưa kết thúc.
            sessionsQuery = sessionsQuery
                .Where(session => session.StaffId == staffId && session.ClosedAt == null)
                .OrderBy(session => session.ClaimedAt)
                .ThenBy(session => session.Id);
        }

        var sessions = await sessionsQuery.ToListAsync(cancellationToken);
        if (sessions.Count == 0)
        {
            return [];
        }

        // Đọc toàn bộ tin nhắn khách của các hội thoại liên quan bằng một truy vấn duy nhất.
        var conversationIds = sessions.Select(session => session.ConversationId).Distinct().ToList();
        var earliestHandoff = sessions.Min(session => session.HandoffAt);

        var customerMessages = await database.Messages.AsNoTracking()
            .Where(message => conversationIds.Contains(message.ConversationId)
                && message.Role == SupportMessageRole.Customer
                && message.CreatedAt >= earliestHandoff)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .ToListAsync(cancellationToken);

        var result = new List<SupportSessionQueueItemResponse>(sessions.Count);
        foreach (var session in sessions)
        {
            // Tìm tin nhắn đầu tiên của khách trong phiên kể từ thời điểm handoffAt.
            var firstCustomerMessage = customerMessages
                .FirstOrDefault(message => message.ConversationId == session.ConversationId
                    && message.CreatedAt >= session.HandoffAt);
            // Cắt tối đa 200 phần tử văn bản đầu tiên của tin nhắn để làm đoạn trích.
            var preview = SupportSessionPreview.Truncate(firstCustomerMessage?.Body);

            // Tính số giây chờ làm tròn xuống từ lúc chuyển giao và chặn giá trị âm.
            var waitEnd = session.ClaimedAt ?? nowUtc;
            var elapsedSeconds = (long)Math.Floor((waitEnd - session.HandoffAt).TotalSeconds);
            var waitingSeconds = Math.Max(0, elapsedSeconds);

            // Chuẩn hóa múi giờ UTC để đảm bảo định dạng chuỗi ISO 8601 ở đầu ra.
            var handoffUtc = DateTime.SpecifyKind(session.HandoffAt, DateTimeKind.Utc);
            var claimedUtc = session.ClaimedAt.HasValue
                ? DateTime.SpecifyKind(session.ClaimedAt.Value, DateTimeKind.Utc)
                : (DateTime?)null;

            result.Add(new SupportSessionQueueItemResponse(
                session.Id,
                session.ConversationId,
                preview,
                waitingSeconds,
                handoffUtc,
                claimedUtc,
                session.TransferNote));
        }

        return result;
    }
}
