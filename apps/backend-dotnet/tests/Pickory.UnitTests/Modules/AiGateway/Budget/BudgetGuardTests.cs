using Pickory.Modules.AiGateway.Budget;
using Pickory.Modules.AiGateway.Calendar;
using Pickory.Modules.AiGateway.Contracts.Calls;
using Pickory.Modules.AiGateway.Contracts.Errors;
using Xunit;

namespace Pickory.UnitTests.Modules.AiGateway.Budget;

// Port từ `budget.test.ts` của bản Node. Bản .NET tách phép so ngưỡng khỏi truy vấn tổng chi phí.
public sealed class BudgetGuardTests
{
    [Fact]
    // Ngân sách bằng 0 chặn mọi lời gọi có phí, kể cả tính năng cốt lõi.
    public void Evaluate_ZeroBudgetBlocksEveryFeature()
    {
        Assert.Throws<AiBudgetExceededException>(() => BudgetGuard.Evaluate(AiFeature.ShoppingAssistantChat, 0, 0));
        Assert.Throws<AiBudgetExceededException>(() => BudgetGuard.Evaluate(AiFeature.PersonalizedContentBanner, 0, 0));
    }

    [Fact]
    // Nhóm ưu tiên thấp bị dừng trước khi chạm 100% ngân sách. Chat, tìm kiếm và embedding vẫn chạy.
    public void IsLowPriority_MatchesNodeFeatureGroups()
    {
        Assert.True(BudgetGuard.IsLowPriority(AiFeature.PersonalizedContentReviewSummary));
        Assert.True(BudgetGuard.IsLowPriority(AiFeature.PersonalizedContentDescription));
        Assert.True(BudgetGuard.IsLowPriority(AiFeature.PersonalizedContentBanner));
        Assert.True(BudgetGuard.IsLowPriority(AiFeature.RecommendationsRerank));
        Assert.True(BudgetGuard.IsLowPriority(AiFeature.SupportDeskDraftReply));

        Assert.False(BudgetGuard.IsLowPriority(AiFeature.ShoppingAssistantChat));
        Assert.False(BudgetGuard.IsLowPriority(AiFeature.ShoppingAssistantSummarize));
        Assert.False(BudgetGuard.IsLowPriority(AiFeature.SmartSearchParseQuery));
        Assert.False(BudgetGuard.IsLowPriority(AiFeature.KnowledgeBaseQuery));
    }

    [Fact]
    // Tháng tính theo giờ Việt Nam (UTC+7). Ngày 1/10 giờ Việt Nam bắt đầu lúc 17:00 UTC ngày 30/9.
    public void MonthWindow_UsesVietnamTime()
    {
        var window = VietnamCalendar.MonthWindow(new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTime(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc), window.StartUtc);
        Assert.Equal(new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc), window.NextStartUtc);
    }

    [Fact]
    // 17:00 UTC ngày 31/10 đã là ngày 1/11 giờ Việt Nam. Chi phí lúc đó thuộc tháng 11.
    public void MonthWindow_MovesToNextMonthAtVietnamMidnight()
    {
        var window = VietnamCalendar.MonthWindow(new DateTimeOffset(2026, 10, 31, 17, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc), window.StartUtc);
        Assert.Equal(new DateTime(2026, 11, 30, 17, 0, 0, DateTimeKind.Utc), window.NextStartUtc);
    }

    [Fact]
    // Dưới 80% ngân sách thì chạy bình thường, không bật cờ cảnh báo.
    public void Evaluate_BelowEightyPercentRunsNormally()
    {
        var check = BudgetGuard.Evaluate(AiFeature.PersonalizedContentReviewSummary, 30, 15);

        Assert.Equal(15, check.TotalCostUsd);
        Assert.False(check.BudgetWarning);
    }

    [Fact]
    // Từ 80% đến dưới 100% ngân sách thì bật cờ cảnh báo, chưa chặn tính năng nào.
    public void Evaluate_FromEightyPercentRaisesWarning()
    {
        var check = BudgetGuard.Evaluate(AiFeature.PersonalizedContentReviewSummary, 30, 25);

        Assert.True(check.BudgetWarning);
    }

    [Fact]
    // Từ 100% ngân sách thì chặn tính năng ưu tiên thấp. Chat vẫn chạy.
    public void Evaluate_AtHundredPercentBlocksOnlyLowPriority()
    {
        Assert.Throws<AiBudgetExceededException>(
            () => BudgetGuard.Evaluate(AiFeature.PersonalizedContentReviewSummary, 30, 30.5));

        var check = BudgetGuard.Evaluate(AiFeature.ShoppingAssistantChat, 30, 30.5);

        Assert.True(check.BudgetWarning);
    }

    [Fact]
    // Từ 120% ngân sách thì dừng mọi tính năng AI.
    public void Evaluate_AtHundredTwentyPercentBlocksEverything()
    {
        Assert.Throws<AiBudgetExceededException>(() => BudgetGuard.Evaluate(AiFeature.ShoppingAssistantChat, 30, 36.5));
        Assert.Throws<AiBudgetExceededException>(() => BudgetGuard.Evaluate(AiFeature.SmartSearchParseQuery, 30, 36.5));
    }

    [Fact]
    // Ngày theo giờ Việt Nam. 17:00 UTC đã sang ngày hôm sau.
    public void DayKey_UsesVietnamTime()
    {
        Assert.Equal("2026-10-05", VietnamCalendar.DayKey(new DateTimeOffset(2026, 10, 5, 16, 59, 0, TimeSpan.Zero)));
        Assert.Equal("2026-10-06", VietnamCalendar.DayKey(new DateTimeOffset(2026, 10, 5, 17, 0, 0, TimeSpan.Zero)));
    }
}
