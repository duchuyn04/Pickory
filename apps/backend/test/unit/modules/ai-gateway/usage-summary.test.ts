import { describe, expect, it } from 'vitest';
import {
  calculateNearestRankPercentile,
  getUsageSummary,
} from '../../../../src/modules/ai-gateway/usage-summary';
import type { PrismaClient } from '../../../../src/generated/prisma/client';

describe('AI-09: Kiểm thử thuật toán phân vị và tổng hợp dữ liệu sử dụng AI', () => {
  describe('calculateNearestRankPercentile', () => {
    it('trả về 0 khi danh sách độ trễ rỗng', () => {
      // Bảo đảm an toàn khi chưa có dữ liệu cuộc gọi nào phát sinh.
      expect(calculateNearestRankPercentile([], 95)).toBe(0);
    });

    it('trả về giá trị của phần tử duy nhất trong danh sách một phần tử', () => {
      // Phân vị bất kỳ của tập một phần tử luôn là chính phần tử đó.
      expect(calculateNearestRankPercentile([250], 95)).toBe(250);
      expect(calculateNearestRankPercentile([250], 50)).toBe(250);
    });

    it('tính đúng phân vị p95 cho tập 10 phần tử theo công thức nearest-rank tất định', () => {
      // Với N = 10, phân vị P = 95%: rank k = ceil(0.95 * 10) = 10 -> phần tử thứ 10 (chỉ số 9).
      const latencies = [100, 200, 300, 400, 500, 600, 700, 800, 900, 1000];
      expect(calculateNearestRankPercentile(latencies, 95)).toBe(1000);

      // Phân vị p50%: rank k = ceil(0.50 * 10) = 5 -> phần tử thứ 5 (chỉ số 4) là 500.
      expect(calculateNearestRankPercentile(latencies, 50)).toBe(500);

      // Phân vị p90%: rank k = ceil(0.90 * 10) = 9 -> phần tử thứ 9 (chỉ số 8) là 900.
      expect(calculateNearestRankPercentile(latencies, 90)).toBe(900);
    });

    it('tính đúng phân vị p95 cho tập 20 phần tử đều nhau', () => {
      // Với N = 20, rank k = ceil(0.95 * 20) = 19 -> phần tử thứ 19 có giá trị 190.
      const latencies = Array.from({ length: 20 }, (_, i) => (i + 1) * 10);
      expect(calculateNearestRankPercentile(latencies, 95)).toBe(190);
    });

    it('tự động sắp xếp mảng đầu vào chưa được sắp xếp trước khi tính', () => {
      // Mảng xáo trộn không được làm sai lệch thứ bậc phân vị.
      const unsorted = [500, 100, 1000, 200, 900, 300, 800, 400, 700, 600];
      expect(calculateNearestRankPercentile(unsorted, 95)).toBe(1000);
    });
  });

  describe('getUsageSummary với dữ liệu mẫu và phân định biên ngày/tháng', () => {
    interface MockUsageLogItem {
      costUsd: number;
      latencyMs: number;
      status: string;
      feature: string;
      createdAt: Date;
    }

    interface MockWhereQuery {
      createdAt?: {
        gte?: Date;
        lte?: Date;
        lt?: Date;
      };
    }

    // Tạo mock store để kiểm thử logic xử lý nghiệp vụ độc lập mà không cần kết nối DB vật lý.
    function createMockStore(logs: MockUsageLogItem[], monthlyBudgetUsd = 30): PrismaClient {
      function filterLogs(where?: MockWhereQuery): MockUsageLogItem[] {
        return logs.filter((log) => {
          if (where?.createdAt?.gte && log.createdAt < where.createdAt.gte) {
            return false;
          }
          if (where?.createdAt?.lt && log.createdAt >= where.createdAt.lt) {
            return false;
          }
          if (where?.createdAt?.lte && log.createdAt > where.createdAt.lte) {
            return false;
          }
          return true;
        });
      }

      return {
        aiUsageLog: {
          findMany: async (args?: { where?: MockWhereQuery }) => {
            return filterLogs(args?.where);
          },
          aggregate: async (args?: { where?: MockWhereQuery }) => {
            const matched = filterLogs(args?.where);
            const sum = matched.reduce((acc, item) => acc + item.costUsd, 0);
            return { _sum: { costUsd: sum } };
          },
        },
        aiSetting: {
          findUnique: async () => ({
            id: 'singleton',
            monthlyBudgetUsd,
            chatDailyLimitUser: 50,
            chatDailyLimitAnon: 15,
            createdAt: new Date(),
            updatedAt: new Date(),
          }),
        },
      } as unknown as PrismaClient;
    }

    it('phân bổ chính xác biên ngày theo múi giờ Việt Nam Asia/Ho_Chi_Minh', async () => {
      // Mốc 16:59:59 UTC ngày 04/10 tương ứng 23:59:59 tại Việt Nam (thuộc ngày 2026-10-04).
      const logDay4 = {
        costUsd: 0.05,
        latencyMs: 120,
        status: 'ok',
        feature: 'shopping-assistant.chat',
        createdAt: new Date('2026-10-04T16:59:59.000Z'),
      };

      // Mốc 17:00:00 UTC ngày 04/10 tương ứng 00:00:00 tại Việt Nam (bắt đầu ngày mới 2026-10-05).
      const logDay5Midnight = {
        costUsd: 0.1,
        latencyMs: 250,
        status: 'ok',
        feature: 'shopping-assistant.chat',
        createdAt: new Date('2026-10-04T17:00:00.000Z'),
      };

      // Mốc trưa ngày 05/10 tại Việt Nam.
      const logDay5Noon = {
        costUsd: 0.15,
        latencyMs: 300,
        status: 'ok',
        feature: 'smart-search.parse-query',
        createdAt: new Date('2026-10-05T05:00:00.000Z'),
      };

      const mockStore = createMockStore([logDay4, logDay5Midnight, logDay5Noon]);

      const summary = await getUsageSummary(
        {
          from: new Date('2026-10-04T00:00:00.000Z'),
          to: new Date('2026-10-05T23:59:59.000Z'),
        },
        mockStore,
      );

      // Xác nhận kết quả phân rã theo ngày chính xác theo giờ Việt Nam.
      expect(summary.byDay).toHaveLength(2);
      expect(summary.byDay[0]?.day).toBe('2026-10-04');
      expect(summary.byDay[0]?.calls).toBe(1);
      expect(summary.byDay[0]?.costUsd).toBe(0.05);

      expect(summary.byDay[1]?.day).toBe('2026-10-05');
      expect(summary.byDay[1]?.calls).toBe(2);
      expect(summary.byDay[1]?.costUsd).toBe(0.25);
    });

    it('loại trừ các bản ghi nằm ngoài phạm vi khoảng ngày chỉ định', async () => {
      const logInside = {
        costUsd: 0.02,
        latencyMs: 150,
        status: 'ok',
        feature: 'shopping-assistant.chat',
        createdAt: new Date('2026-10-05T10:00:00.000Z'),
      };
      const logBefore = {
        costUsd: 0.08,
        latencyMs: 180,
        status: 'ok',
        feature: 'shopping-assistant.chat',
        createdAt: new Date('2026-10-01T00:00:00.000Z'),
      };
      const logAfter = {
        costUsd: 0.09,
        latencyMs: 200,
        status: 'ok',
        feature: 'shopping-assistant.chat',
        createdAt: new Date('2026-10-10T00:00:00.000Z'),
      };

      const mockStore = createMockStore([logBefore, logInside, logAfter]);

      const summary = await getUsageSummary(
        {
          from: new Date('2026-10-05T00:00:00.000Z'),
          to: new Date('2026-10-05T23:59:59.000Z'),
        },
        mockStore,
      );

      expect(summary.calls).toBe(1);
      expect(summary.costUsd).toBe(0.02);
      expect(summary.byDay).toEqual([{ day: '2026-10-05', costUsd: 0.02, calls: 1 }]);
    });

    it('giữ nguyên chi phí siêu nhỏ của embedding mà không làm tròn thành 0', async () => {
      // Chi phí 1 token embedding khoảng 0.00000002 USD (0.02 USD / 1M token).
      const tinyEmbeddingCost = 0.00000002;
      const tinyLog = {
        costUsd: tinyEmbeddingCost,
        latencyMs: 40,
        status: 'ok',
        feature: 'smart-search.index',
        createdAt: new Date('2026-10-05T10:00:00.000Z'),
      };

      const mockStore = createMockStore([tinyLog]);

      const summary = await getUsageSummary(
        {
          from: new Date('2026-10-05T00:00:00.000Z'),
          to: new Date('2026-10-05T23:59:59.000Z'),
        },
        mockStore,
      );

      // Chi phí phải được bảo toàn nguyên vẹn, không bị ép về 0 bởi phép làm tròn 6 chữ số thập phân.
      expect(summary.costUsd).toBe(tinyEmbeddingCost);
      expect(summary.byDay[0]?.costUsd).toBe(tinyEmbeddingCost);
      expect(summary.byFeature[0]?.costUsd).toBe(tinyEmbeddingCost);
    });

    it('tính đúng tỉ lệ lỗi errorRate (chỉ tính status=error)', async () => {
      const logs = [
        {
          costUsd: 0.01,
          latencyMs: 100,
          status: 'ok',
          feature: 'chat',
          createdAt: new Date('2026-10-05T01:00:00.000Z'),
        },
        {
          costUsd: 0.01,
          latencyMs: 110,
          status: 'ok',
          feature: 'chat',
          createdAt: new Date('2026-10-05T02:00:00.000Z'),
        },
        {
          costUsd: 0.0,
          latencyMs: 5,
          status: 'blocked', // Bị chặn không tính là lỗi provider
          feature: 'chat',
          createdAt: new Date('2026-10-05T03:00:00.000Z'),
        },
        {
          costUsd: 0.0,
          latencyMs: 80,
          status: 'error', // Lỗi thực sự từ nhà cung cấp
          feature: 'chat',
          createdAt: new Date('2026-10-05T04:00:00.000Z'),
        },
      ];

      const mockStore = createMockStore(logs);
      const summary = await getUsageSummary(
        {
          from: new Date('2026-10-05T00:00:00.000Z'),
          to: new Date('2026-10-05T23:59:59.000Z'),
        },
        mockStore,
      );

      // Tổng 4 cuộc gọi, có 1 cuộc gọi lỗi -> tỉ lệ lỗi 1 / 4 = 0.25 (25%).
      expect(summary.calls).toBe(4);
      expect(summary.errorRate).toBe(0.25);
    });

    it('bật cảnh báo budgetWarning khi tổng chi phí tháng hiện tại đạt hoặc vượt 80% ngân sách', async () => {
      // Ngân sách tháng cấu hình là 10 USD, ngưỡng cảnh báo 80% là 8 USD.
      // Dùng thời điểm hiện tại Date() để rơi trúng vào tháng hiện hành của getVietnamMonthWindow().
      const highCostLogs = [
        {
          costUsd: 8.5,
          latencyMs: 200,
          status: 'ok',
          feature: 'chat',
          createdAt: new Date(),
        },
      ];

      const mockStoreWarning = createMockStore(highCostLogs, 10);
      const summaryWarning = await getUsageSummary(
        {
          from: new Date(Date.now() - 24 * 60 * 60 * 1000),
          to: new Date(Date.now() + 24 * 60 * 60 * 1000),
        },
        mockStoreWarning,
      );

      expect(summaryWarning.budgetWarning).toBe(true);

      // Trường hợp chi phí dưới 80% (7.9 USD < 8.0 USD) thì cờ cảnh báo phải là false.
      const lowCostLogs = [
        {
          costUsd: 7.9,
          latencyMs: 200,
          status: 'ok',
          feature: 'chat',
          createdAt: new Date(),
        },
      ];

      const mockStoreNormal = createMockStore(lowCostLogs, 10);
      const summaryNormal = await getUsageSummary(
        {
          from: new Date(Date.now() - 24 * 60 * 60 * 1000),
          to: new Date(Date.now() + 24 * 60 * 60 * 1000),
        },
        mockStoreNormal,
      );

      expect(summaryNormal.budgetWarning).toBe(false);
    });
  });
});
