using Microsoft.EntityFrameworkCore;
using Pickory.Modules.SupportDesk.Persistence;

namespace Pickory.Modules.SupportDesk.Callbacks;

// Cổng xóa số điện thoại quá hạn lưu trữ để worker nền gọi và unit test giả lập được.
internal interface ICallbackPhoneRetentionProcessor
{
    Task<int> RedactExpiredPhonesAsync(CancellationToken cancellationToken);
}

// Xóa số điện thoại và ghi chú của yêu cầu gọi lại cũ hơn 30 ngày, giữ lại dòng cho báo cáo.
internal sealed class CallbackPhoneRetentionProcessor(
    SupportDeskDbContext database,
    TimeProvider timeProvider) : ICallbackPhoneRetentionProcessor
{
    public const int RetentionDays = 30;

    // Mốc biên 30 ngày tính từ hiện tại. Chỉ dòng tạo trước mốc này mới hết hạn lưu dữ liệu cá nhân.
    public static DateTime GetCutoffUtc(DateTime nowUtc) => nowUtc.AddDays(-RetentionDays);

    public async Task<int> RedactExpiredPhonesAsync(CancellationToken cancellationToken)
    {
        var cutoff = GetCutoffUtc(timeProvider.GetUtcNow().UtcDateTime);
        // Ghi chú là chữ tự do nên có thể chứa dữ liệu cá nhân, phải xóa cùng số điện thoại.
        return await database.CallbackRequests
            .Where(request => request.CreatedAt < cutoff && request.Phone != null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(request => request.Phone, (string?)null)
                    .SetProperty(request => request.Note, (string?)null),
                cancellationToken);
    }
}
