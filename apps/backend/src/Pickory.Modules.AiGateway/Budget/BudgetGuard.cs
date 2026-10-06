using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pickory.Modules.AiGateway.Calendar;
using Pickory.Modules.AiGateway.Contracts.Calls;
using Pickory.Modules.AiGateway.Contracts.Errors;
using Pickory.Modules.AiGateway.Persistence;

namespace Pickory.Modules.AiGateway.Budget;

// Kiểm tra chi phí tháng trước mỗi bước gọi provider.
// Ngưỡng chỉ tính usage đã ghi, nên request đang chạy có thể làm chi phí thật vượt ngưỡng.
public sealed class BudgetGuard(
    IDbContextFactory<AiGatewayDbContext> databaseFactory,
    TimeProvider timeProvider,
    ILogger<BudgetGuard> logger)
{
    private const double WarningRatio = 0.8;
    private const double LowPriorityStopRatio = 1.0;
    private const double FullStopRatio = 1.2;

    public async Task<BudgetCheck> AssertWithinBudgetAsync(
        AiFeature feature,
        double monthlyBudgetUsd,
        CancellationToken cancellationToken)
    {
        // Ngân sách bằng 0 chặn ngay, không cần đọc DB. Không lời gọi nào được phát sinh phí.
        if (monthlyBudgetUsd <= 0)
        {
            return Evaluate(feature, monthlyBudgetUsd, 0);
        }

        // Tổng chi phí tính từ bảng log của tháng hiện tại theo giờ Việt Nam.
        var window = VietnamCalendar.MonthWindow(timeProvider.GetUtcNow());
        await using var database = await databaseFactory.CreateDbContextAsync(cancellationToken);
        var totalCostUsd = await database.UsageLogs
            .Where(log => log.CreatedAt >= window.StartUtc && log.CreatedAt < window.NextStartUtc)
            .SumAsync(log => log.CostUsd, cancellationToken);

        var check = Evaluate(feature, monthlyBudgetUsd, totalCostUsd);
        // Cảnh báo sớm để người vận hành tăng ngân sách trước khi tính năng bị chặn.
        if (check.BudgetWarning)
        {
            logger.LogWarning(
                "Chi phí AI trong tháng đã vượt ngưỡng cảnh báo 80%: {Feature}, {TotalCostUsd} trên {MonthlyBudgetUsd} USD.",
                AiFeatureCodes.ToCode(feature),
                totalCostUsd,
                monthlyBudgetUsd);
        }

        return check;
    }

    public static BudgetCheck Evaluate(AiFeature feature, double monthlyBudgetUsd, double totalCostUsd)
    {
        // Ngân sách bằng 0 nghĩa là admin tắt mọi lời gọi có phí.
        if (monthlyBudgetUsd <= 0)
        {
            throw new AiBudgetExceededException(
                "Ngân sách AI tháng này được thiết lập bằng 0. Hệ thống tạm dừng mọi yêu cầu phát sinh chi phí tới provider.");
        }

        var usageRatio = totalCostUsd / monthlyBudgetUsd;
        // Từ 120% thì dừng mọi tính năng để chi phí không tăng thêm.
        if (usageRatio >= FullStopRatio)
        {
            throw new AiBudgetExceededException(
                "Hệ thống đã vượt 120% ngân sách AI của tháng. Toàn bộ tính năng AI tạm dừng để tránh phát sinh chi phí ngoài dự kiến.");
        }

        // Từ 100% thì dừng nhóm ưu tiên thấp. Chat, tìm kiếm và embedding vẫn phục vụ việc mua bán.
        if (usageRatio >= LowPriorityStopRatio && IsLowPriority(feature))
        {
            throw new AiBudgetExceededException(
                "Hệ thống đã đạt 100% ngân sách AI của tháng. Tính năng nội dung gợi ý tạm dừng; các tính năng cốt lõi vẫn khả dụng.");
        }

        return new BudgetCheck(totalCostUsd, usageRatio >= WarningRatio);
    }

    // Nhóm ưu tiên thấp theo Decision 9 của spec. Nhóm này không ảnh hưởng trực tiếp tới việc mua bán.
    public static bool IsLowPriority(AiFeature feature)
    {
        return feature is AiFeature.PersonalizedContentReviewSummary
            or AiFeature.PersonalizedContentDescription
            or AiFeature.PersonalizedContentBanner
            or AiFeature.RecommendationsRerank
            or AiFeature.SupportDeskDraftReply;
    }
}
