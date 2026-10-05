import type { PrismaClient } from '../../generated/prisma/client';
import { db as defaultDb } from '../../shared/db';
import { getVietnamMonthWindow } from './budget';
import { getAiSettings } from './settings';
import type { DateRange, UsageSummary } from './types';

// Định dạng ngày theo múi giờ Việt Nam (Asia/Ho_Chi_Minh) dạng YYYY-MM-DD.
// Chuẩn en-CA sắp xếp năm-tháng-ngày tự nhiên, phù hợp để phân nhóm và sắp xếp theo ngày.
const VIETNAM_DAY_FORMATTER = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'Asia/Ho_Chi_Minh',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
});

/**
 * Tính phân vị theo thuật toán nearest-rank (thứ hạng gần nhất) tất định.
 * Với N phần tử đã sắp xếp tăng dần và phân vị P (0 < P <= 100):
 * Thứ hạng k = ceil((P / 100) * N), chỉ số mảng là k - 1.
 * Thuật toán này không dùng nội suy, cho kết quả tất định và bảo toàn giá trị đo thực tế.
 */
export function calculateNearestRankPercentile(values: number[], percentile: number): number {
  // Trả về 0 khi tập mẫu rỗng để tránh lỗi truy cập ngoài biên mảng.
  if (values.length === 0) {
    return 0;
  }

  // Sao chép và sắp xếp tăng dần để xác định đúng thứ bậc phân vị.
  const sorted = [...values].sort((a, b) => a - b);

  // Tính thứ tự xếp hạng k theo công thức trần (ceil) của phương pháp nearest-rank.
  const rank = Math.ceil((percentile / 100) * sorted.length);

  // Giới hạn chỉ số trong phạm vi mảng hợp lệ từ 0 đến N - 1.
  const index = Math.max(0, Math.min(sorted.length - 1, rank - 1));

  // Tập mẫu không rỗng và index đã giới hạn trong mảng nên phần tử luôn tồn tại.
  return sorted[index]!;
}

// Tổng hợp báo cáo sử dụng AI theo khoảng thời gian chỉ định cho giao diện quản trị.
export async function getUsageSummary(
  range: DateRange,
  store: PrismaClient = defaultDb,
): Promise<UsageSummary> {
  // Truy vấn toàn bộ log trong phạm vi thời gian từ from đến to (bao gồm cả hai mốc biên).
  const logs = await store.aiUsageLog.findMany({
    where: {
      createdAt: {
        gte: range.from,
        lte: range.to,
      },
    },
    select: {
      costUsd: true,
      latencyMs: true,
      status: true,
      feature: true,
      createdAt: true,
    },
    orderBy: {
      createdAt: 'asc',
    },
  });

  // Sử dụng Map để nhóm dữ liệu động theo ngày và theo tính năng trong thời gian chạy.
  const dayBuckets = new Map<string, { costUsd: number; calls: number }>();
  const featureBuckets = new Map<string, { costUsd: number; calls: number }>();

  let accumulatedCostUsd = 0;
  let errorCallsCount = 0;
  const latencies: number[] = [];

  // Duyệt qua từng dòng log để tích lũy số liệu thống kê.
  for (const log of logs) {
    // Tích lũy chi phí gốc không làm tròn để giữ nguyên giá trị siêu nhỏ của embedding.
    accumulatedCostUsd += log.costUsd;
    latencies.push(log.latencyMs);

    // Chỉ đếm các lời gọi thất bại do lỗi nhà cung cấp hoặc hệ thống.
    if (log.status === 'error') {
      errorCallsCount += 1;
    }

    // Nhóm chi phí và số lượt gọi theo ngày tại múi giờ Việt Nam.
    const dayKey = VIETNAM_DAY_FORMATTER.format(log.createdAt);
    const existingDay = dayBuckets.get(dayKey);
    if (existingDay) {
      existingDay.costUsd += log.costUsd;
      existingDay.calls += 1;
    } else {
      dayBuckets.set(dayKey, { costUsd: log.costUsd, calls: 1 });
    }

    // Nhóm chi phí và số lượt gọi theo tên tính năng AI.
    const featureKey = log.feature;
    const existingFeature = featureBuckets.get(featureKey);
    if (existingFeature) {
      existingFeature.costUsd += log.costUsd;
      existingFeature.calls += 1;
    } else {
      featureBuckets.set(featureKey, { costUsd: log.costUsd, calls: 1 });
    }
  }

  const totalCalls = logs.length;

  // Tính tỉ lệ lỗi trên tổng số lượt gọi, trả về 0 nếu chưa có cuộc gọi nào.
  const errorRate = totalCalls > 0 ? errorCallsCount / totalCalls : 0;

  // Tính độ trễ phân vị p95 theo thuật toán nearest-rank tất định.
  const p95LatencyMs = calculateNearestRankPercentile(latencies, 95);

  // Sắp xếp danh sách theo ngày tăng dần theo thứ tự thời gian.
  const byDay = Array.from(dayBuckets.entries())
    .map(([day, data]) => ({
      day,
      costUsd: data.costUsd,
      calls: data.calls,
    }))
    .sort((a, b) => a.day.localeCompare(b.day));

  // Sắp xếp danh sách theo tính năng giảm dần theo chi phí sử dụng.
  const byFeature = Array.from(featureBuckets.entries())
    .map(([feature, data]) => ({
      feature,
      costUsd: data.costUsd,
      calls: data.calls,
    }))
    .sort((a, b) => b.costUsd - a.costUsd || a.feature.localeCompare(b.feature));

  // Đọc cài đặt ngân sách hiện hành để kiểm tra ngưỡng ngân sách tháng.
  const settings = await getAiSettings(store);

  // Cảnh báo ngân sách phải so sánh với tổng chi tiêu của cả tháng hiện tại theo giờ Việt Nam,
  // chứ không chỉ tính riêng trên khoảng thời gian lọc của báo cáo.
  const { startOfMonthUtc, nextMonthStartUtc } = getVietnamMonthWindow();
  const currentMonthAggregate = await store.aiUsageLog.aggregate({
    _sum: { costUsd: true },
    where: {
      createdAt: {
        gte: startOfMonthUtc,
        lt: nextMonthStartUtc,
      },
    },
  });

  const currentMonthCostUsd = currentMonthAggregate._sum.costUsd ?? 0;

  // Bật cờ cảnh báo khi tổng chi tiêu tháng hiện tại đạt hoặc vượt 80% ngân sách tháng.
  const budgetWarning =
    settings.monthlyBudgetUsd > 0
      ? currentMonthCostUsd >= settings.monthlyBudgetUsd * 0.8
      : currentMonthCostUsd > 0;

  return {
    costUsd: accumulatedCostUsd,
    calls: totalCalls,
    errorRate,
    p95LatencyMs,
    byDay,
    byFeature,
    budgetWarning,
  };
}
