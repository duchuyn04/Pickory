import type { PrismaClient } from '../../generated/prisma/client';
import { logger } from '../../shared/logger';
import { AiBudgetExceededError } from './errors';
import type { AiFeature } from './types';

// Chênh lệch múi giờ Việt Nam cố định là +7 giờ và không có giờ mùa hè.
const VIETNAM_OFFSET_HOURS = 7;
const VIETNAM_OFFSET_MILLISECONDS = VIETNAM_OFFSET_HOURS * 60 * 60 * 1000;

// Tính toán mốc thời gian đầu tháng và đầu tháng tiếp theo theo giờ Việt Nam.
export function getVietnamMonthWindow(nowMs: number = Date.now()): {
  startOfMonthUtc: Date;
  nextMonthStartUtc: Date;
} {
  // Dịch thời gian sang múi giờ Việt Nam để xác định chính xác ranh giới tháng địa phương.
  const vietnamTime = new Date(nowMs + VIETNAM_OFFSET_MILLISECONDS);
  const year = vietnamTime.getUTCFullYear();
  const month = vietnamTime.getUTCMonth();

  // Chuyển mốc 00:00:00 ngày đầu tháng tại Việt Nam ngược lại mốc chuẩn UTC.
  const startOfMonthUtc = new Date(Date.UTC(year, month, 1) - VIETNAM_OFFSET_MILLISECONDS);
  // Chuyển mốc 00:00:00 ngày đầu tháng sau tại Việt Nam ngược lại mốc chuẩn UTC.
  const nextMonthStartUtc = new Date(Date.UTC(year, month + 1, 1) - VIETNAM_OFFSET_MILLISECONDS);

  return { startOfMonthUtc, nextMonthStartUtc };
}

// Kiểm tra tính năng có thuộc nhóm ưu tiên thấp theo quyết định kiến trúc Decision 9.
export function isLowPriorityFeature(feature: AiFeature): boolean {
  return (
    feature.startsWith('personalized-content.') ||
    feature === 'recommendations.rerank' ||
    feature === 'support-desk.draft-reply'
  );
}

// Kiểm tra chi phí tháng hiện tại so với hạn mức ngân sách và ném lỗi nếu vượt ngưỡng.
export async function assertWithinBudget(params: {
  feature: AiFeature;
  monthlyBudgetUsd: number;
  store: PrismaClient;
  now?: () => number;
}): Promise<{ totalCostUsd: number; budgetWarning: boolean }> {
  // Ngân sách bằng 0 hoặc âm: chặn toàn bộ yêu cầu gọi provider để bảo đảm không phát sinh bất kỳ khoản phí nào ngoài dự kiến.
  if (params.monthlyBudgetUsd <= 0) {
    throw new AiBudgetExceededError(
      'Ngân sách AI tháng này được thiết lập bằng 0. Hệ thống tạm dừng mọi yêu cầu phát sinh chi phí tới provider.',
    );
  }

  const currentNow = params.now ? params.now() : Date.now();
  const { startOfMonthUtc, nextMonthStartUtc } = getVietnamMonthWindow(currentNow);

  // Tính tổng chi phí đã phát sinh trong tháng hiện tại từ bảng nhật ký.
  const aggregateResult = await params.store.aiUsageLog.aggregate({
    _sum: { costUsd: true },
    where: {
      createdAt: {
        gte: startOfMonthUtc,
        lt: nextMonthStartUtc,
      },
    },
  });

  const totalCostUsd = aggregateResult._sum.costUsd ?? 0;
  const usageRatio = totalCostUsd / params.monthlyBudgetUsd;

  // Ngưỡng 120% chặn bước mới dựa trên chi phí đã ghi, không phải trần hóa đơn của các request đang chạy.
  if (usageRatio >= 1.2) {
    throw new AiBudgetExceededError(
      'Hệ thống đã vượt 120% ngân sách AI của tháng. Toàn bộ tính năng AI tạm dừng để tránh phát sinh chi phí ngoài dự kiến.',
    );
  }

  // Ngưỡng 100%: chặn các tính năng ưu tiên thấp nhưng vẫn cho phép các tính năng chuyển đổi cốt lõi chạy.
  if (usageRatio >= 1.0 && isLowPriorityFeature(params.feature)) {
    throw new AiBudgetExceededError(
      'Hệ thống đã đạt 100% ngân sách AI của tháng. Tính năng nội dung gợi ý tạm dừng; các tính năng cốt lõi vẫn khả dụng.',
    );
  }

  // Ngưỡng 80%: ghi log cảnh báo để người vận hành chủ động theo dõi hoặc tăng ngân sách trước khi bị chặn.
  const budgetWarning = usageRatio >= 0.8;
  if (budgetWarning) {
    // Truyền đối tượng dữ liệu trước và chuỗi thông báo sau theo đúng chuẩn giao thức Pino logger.
    logger.warn(
      {
        feature: params.feature,
        totalCostUsd,
        monthlyBudgetUsd: params.monthlyBudgetUsd,
        usageRatio,
      },
      'Chi phí AI trong tháng đã vượt ngưỡng cảnh báo 80%',
    );
  }

  return { totalCostUsd, budgetWarning };
}
