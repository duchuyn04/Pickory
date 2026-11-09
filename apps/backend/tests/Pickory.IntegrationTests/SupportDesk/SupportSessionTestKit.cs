using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence.Configurations;

namespace Pickory.IntegrationTests.SupportDesk;

// Dụng cụ chung cho test phiên chờ của SD-04. Dụng cụ này gieo phiên, sửa cấu hình và đọc lại bảng phiên ở mốc giờ cố định.
internal static class SupportSessionTestKit
{
    // 10:00 sáng ngày 2026-11-03 theo giờ Việt Nam. Khung giờ mặc định 8:00 đến 21:00 bao trùm mốc này.
    public static readonly DateTimeOffset InHoursMoment = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);

    // 21:00 tối cùng ngày theo giờ Việt Nam. Khung giờ là nửa mở nên mốc này nằm ngoài giờ.
    public static readonly DateTimeOffset ClosingMoment = new(2026, 11, 3, 14, 0, 0, TimeSpan.Zero);

    // Giá trị của dòng cấu hình do migration tạo. Test sửa cấu hình phải trả về đúng các giá trị này.
    private static readonly TimeOnly DefaultOpensAt = new(8, 0);
    private static readonly TimeOnly DefaultClosesAt = new(21, 0);
    private const int DefaultWaitNudgeMinutes = 10;
    private const int DefaultAutoCloseMinutes = 30;

    // Ngày lịch Việt Nam của một mốc UTC. Ngày nghỉ trong cấu hình là ngày Việt Nam, không phải ngày UTC.
    public static DateOnly VietnamDate(DateTimeOffset moment) =>
        DateOnly.FromDateTime(moment.UtcDateTime.AddHours(7));

    // Gieo một phiên cho hội thoại. Hai cột định danh lấy từ hội thoại, giống mã nguồn thật lúc mở phiên.
    // Tham số có tên để test đọc rõ trạng thái phiên mà nó dựng.
    public static async Task<SupportSession> SeedSessionAsync(
        IntegrationDatabaseFixture database,
        SupportConversation conversation,
        DateTime handoffAt,
        bool handoffInHours = true,
        string? staffId = null,
        DateTime? claimedAt = null,
        DateTime? closedAt = null,
        int? csat = null,
        DateTime? nudgedAt = null)
    {
        var session = new SupportSession
        {
            ConversationId = conversation.Id,
            HandoffAt = handoffAt,
            HandoffInHours = handoffInHours,
            CustomerUserId = conversation.CustomerUserId,
            AnonymousId = conversation.AnonymousId,
            StaffId = staffId,
            ClaimedAt = claimedAt,
            ClosedAt = closedAt,
            Csat = csat,
            NudgedAt = nudgedAt,
        };
        await using var context = database.CreateSupportDeskContext();
        context.Sessions.Add(session);
        await context.SaveChangesAsync();
        return session;
    }

    // Mọi phiên của một hội thoại. Thứ tự theo lúc mở phiên rồi mã phiên, giống cách route chọn phiên mới nhất.
    public static async Task<IReadOnlyList<SupportSession>> ReadSessionsAsync(
        IntegrationDatabaseFixture database, string conversationId)
    {
        await using var context = database.CreateSupportDeskContext();
        return await context.Sessions.AsNoTracking()
            .Where(session => session.ConversationId == conversationId)
            .OrderBy(session => session.HandoffAt)
            .ThenBy(session => session.Id)
            .ToListAsync();
    }

    // Sửa dòng cấu hình duy nhất. Trường bỏ trống giữ giá trị đang có, nên test chỉ đổi đúng trường nó cần.
    public static async Task UpdateSettingsAsync(
        IntegrationDatabaseFixture database,
        TimeOnly? opensAt = null,
        TimeOnly? closesAt = null,
        DateOnly[]? holidays = null,
        int? waitNudgeMinutes = null)
    {
        await using var context = database.CreateSupportDeskContext();
        var settings = await context.Settings.SingleAsync(row => row.Id == SupportSettingsConfiguration.SingletonId);
        settings.OpensAt = opensAt ?? settings.OpensAt;
        settings.ClosesAt = closesAt ?? settings.ClosesAt;
        settings.Holidays = holidays ?? settings.Holidays;
        settings.WaitNudgeMinutes = waitNudgeMinutes ?? settings.WaitNudgeMinutes;
        await context.SaveChangesAsync();
    }

    // Trả dòng cấu hình về giá trị mặc định. Bảng cấu hình dùng chung cho cả bộ test, nên test nào sửa phải gọi hàm này.
    public static async Task ResetSettingsAsync(IntegrationDatabaseFixture database)
    {
        await using var context = database.CreateSupportDeskContext();
        var settings = await context.Settings.SingleAsync(row => row.Id == SupportSettingsConfiguration.SingletonId);
        settings.OpensAt = DefaultOpensAt;
        settings.ClosesAt = DefaultClosesAt;
        settings.Holidays = [];
        settings.WaitNudgeMinutes = DefaultWaitNudgeMinutes;
        settings.AutoCloseMinutes = DefaultAutoCloseMinutes;
        await context.SaveChangesAsync();
    }

    public static async Task<SupportSettings> ReadSettingsAsync(IntegrationDatabaseFixture database)
    {
        await using var context = database.CreateSupportDeskContext();
        return await context.Settings.AsNoTracking()
            .SingleAsync(row => row.Id == SupportSettingsConfiguration.SingletonId);
    }
}
