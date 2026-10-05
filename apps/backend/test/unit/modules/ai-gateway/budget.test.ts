import { describe, expect, it } from 'vitest';
import type { PrismaClient } from '../../../../src/generated/prisma/client';
import {
  assertWithinBudget,
  getVietnamMonthWindow,
  isLowPriorityFeature,
} from '../../../../src/modules/ai-gateway/budget';
import { AiBudgetExceededError } from '../../../../src/modules/ai-gateway/errors';

describe('Kiểm soát ngân sách tháng và phân cấp tính năng', () => {
  it('chặn mọi lời gọi phát sinh chi phí khi ngân sách được thiết lập bằng 0', async () => {
    const mockStore = {
      aiUsageLog: {
        aggregate: async () => ({ _sum: { costUsd: 0 } }),
      },
    } as unknown as PrismaClient;

    // Khi ngân sách bằng 0, không được phép gọi provider ngoài dù chi phí hiện tại là 0.
    await expect(
      assertWithinBudget({
        feature: 'shopping-assistant.chat',
        monthlyBudgetUsd: 0,
        store: mockStore,
      }),
    ).rejects.toBeInstanceOf(AiBudgetExceededError);

    await expect(
      assertWithinBudget({
        feature: 'personalized-content.banner',
        monthlyBudgetUsd: 0,
        store: mockStore,
      }),
    ).rejects.toBeInstanceOf(AiBudgetExceededError);
  });

  it('xác định chính xác tính năng thuộc nhóm ưu tiên thấp', () => {
    // Nhóm tính năng tiếp thị và nội dung gợi ý thuộc nhóm ưu tiên thấp.
    expect(isLowPriorityFeature('personalized-content.review-summary')).toBe(true);
    expect(isLowPriorityFeature('personalized-content.description')).toBe(true);
    expect(isLowPriorityFeature('personalized-content.banner')).toBe(true);
    expect(isLowPriorityFeature('recommendations.rerank')).toBe(true);
    expect(isLowPriorityFeature('support-desk.draft-reply')).toBe(true);

    // Tính năng tìm kiếm và trợ lý mua sắm là nghiệp vụ cốt lõi, thuộc nhóm ưu tiên cao.
    expect(isLowPriorityFeature('shopping-assistant.chat')).toBe(false);
    expect(isLowPriorityFeature('shopping-assistant.summarize')).toBe(false);
    expect(isLowPriorityFeature('smart-search.parse-query')).toBe(false);
    expect(isLowPriorityFeature('knowledge-base.query')).toBe(false);
  });

  it('tính đúng cửa sổ tháng theo múi giờ Việt Nam UTC+7', () => {
    // Thời điểm: 2026-10-05 10:00:00 UTC (17:00:00 Việt Nam).
    const fixedTime = new Date('2026-10-05T10:00:00.000Z').getTime();
    const { startOfMonthUtc, nextMonthStartUtc } = getVietnamMonthWindow(fixedTime);

    // Đầu tháng 10 Việt Nam là 2026-10-01 00:00:00 UTC+7 tương đương 2026-09-30 17:00:00 UTC.
    expect(startOfMonthUtc.toISOString()).toBe('2026-09-30T17:00:00.000Z');
    // Đầu tháng 11 Việt Nam tương đương 2026-10-31 17:00:00 UTC.
    expect(nextMonthStartUtc.toISOString()).toBe('2026-10-31T17:00:00.000Z');
  });

  it('hoạt động bình thường dưới 80% ngân sách', async () => {
    const mockStore = {
      aiUsageLog: {
        aggregate: async () => ({ _sum: { costUsd: 15 } }),
      },
    } as unknown as PrismaClient;

    const result = await assertWithinBudget({
      feature: 'personalized-content.review-summary',
      monthlyBudgetUsd: 30,
      store: mockStore,
    });

    expect(result.totalCostUsd).toBe(15);
    expect(result.budgetWarning).toBe(false);
  });

  it('bật cờ cảnh báo khi chạm từ 80% đến dưới 100% ngân sách', async () => {
    const mockStore = {
      aiUsageLog: {
        aggregate: async () => ({ _sum: { costUsd: 25 } }), // 25 / 30 = 83.3%
      },
    } as unknown as PrismaClient;

    const result = await assertWithinBudget({
      feature: 'personalized-content.review-summary',
      monthlyBudgetUsd: 30,
      store: mockStore,
    });

    expect(result.budgetWarning).toBe(true);
  });

  it('đạt 100% ngân sách: chặn tính năng ưu tiên thấp, cho phép tính năng cốt lõi', async () => {
    const mockStore = {
      aiUsageLog: {
        aggregate: async () => ({ _sum: { costUsd: 30.5 } }), // 101.6% ngân sách
      },
    } as unknown as PrismaClient;

    // Tính năng ưu tiên thấp bị chặn lập tức.
    await expect(
      assertWithinBudget({
        feature: 'personalized-content.review-summary',
        monthlyBudgetUsd: 30,
        store: mockStore,
      }),
    ).rejects.toBeInstanceOf(AiBudgetExceededError);

    // Trợ lý mua sắm vẫn được phép hoạt động tiếp tục.
    await expect(
      assertWithinBudget({
        feature: 'shopping-assistant.chat',
        monthlyBudgetUsd: 30,
        store: mockStore,
      }),
    ).resolves.not.toThrow();
  });

  it('đạt 120% ngân sách: chặn toàn bộ mọi tính năng AI', async () => {
    const mockStore = {
      aiUsageLog: {
        aggregate: async () => ({ _sum: { costUsd: 36.5 } }), // 36.5 / 30 = 121.6%
      },
    } as unknown as PrismaClient;

    // Chat mua sắm cũng bị dừng khi đạt ngưỡng 120%.
    await expect(
      assertWithinBudget({
        feature: 'shopping-assistant.chat',
        monthlyBudgetUsd: 30,
        store: mockStore,
      }),
    ).rejects.toBeInstanceOf(AiBudgetExceededError);

    // Tìm kiếm thông minh cũng bị dừng an toàn.
    await expect(
      assertWithinBudget({
        feature: 'smart-search.parse-query',
        monthlyBudgetUsd: 30,
        store: mockStore,
      }),
    ).rejects.toBeInstanceOf(AiBudgetExceededError);
  });
});
