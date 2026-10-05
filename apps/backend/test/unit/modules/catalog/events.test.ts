import { afterEach, describe, expect, it, vi } from 'vitest';
import type { PrismaClient } from '../../../../src/generated/prisma/client';
import {
  createEventCollector,
  emitAfterCommit,
  onProductChanged,
  onStockChanged,
} from '../../../../src/modules/catalog/data/events';

describe('catalog events và emitAfterCommit (unit)', () => {
  const cleanupFns: Array<() => void> = [];

  afterEach(() => {
    // Dọn sạch toàn bộ handler đã đăng ký sau mỗi test để tránh ảnh hưởng test sau.
    while (cleanupFns.length > 0) {
      cleanupFns.pop()?.();
    }
  });

  describe('createEventCollector', () => {
    it('gom sự kiện và chỉ phát khi gọi flush, gọi flush lần hai không phát lại', async () => {
      const handler = vi.fn();
      cleanupFns.push(onProductChanged(handler));

      const { collector, flush } = createEventCollector();
      collector.emit('ProductChangedEvent', {
        productId: 'prod-collector',
        change: 'created',
      });

      // Chưa gọi flush thì sự kiện chưa được phát.
      expect(handler).not.toHaveBeenCalled();

      // Gọi flush lần 1 thì sự kiện được phát đi.
      await flush();
      expect(handler).toHaveBeenCalledTimes(1);

      // Gọi flush lần 2 thì không phát lại do hàng đợi đã dọn sạch.
      await flush();
      expect(handler).toHaveBeenCalledTimes(1);
    });
  });

  describe('emitAfterCommit với dbClient giả lập', () => {
    it('không phát sự kiện khi $transaction ném lỗi', async () => {
      const handler = vi.fn();
      cleanupFns.push(onProductChanged(handler));

      const fakeDb = {
        $transaction: vi.fn().mockImplementation(async (callback) => {
          return await callback({});
        }),
      } as unknown as PrismaClient;

      await expect(
        emitAfterCommit(fakeDb, async (_tx, events) => {
          events.emit('ProductChangedEvent', {
            productId: 'prod-fail',
            change: 'created',
          });
          throw new Error('Lỗi cố ý trong transaction');
        }),
      ).rejects.toThrow('Lỗi cố ý trong transaction');

      expect(handler).not.toHaveBeenCalled();
    });

    it('phát đúng 1 lần sau khi $transaction hoàn tất thành công', async () => {
      const productHandler = vi.fn();
      const stockHandler = vi.fn();

      cleanupFns.push(onProductChanged(productHandler));
      cleanupFns.push(onStockChanged(stockHandler));

      const fakeDb = {
        $transaction: vi.fn().mockImplementation(async (callback) => {
          return await callback({});
        }),
      } as unknown as PrismaClient;

      const result = await emitAfterCommit(fakeDb, async (_tx, events) => {
        events.emit('ProductChangedEvent', {
          productId: 'prod-success',
          change: 'created',
        });
        events.emit('StockChangedEvent', {
          skuId: 'sku-success',
          stock: 10,
          previousStock: 0,
        });
        return { ok: true };
      });

      expect(result).toEqual({ ok: true });
      expect(productHandler).toHaveBeenCalledTimes(1);
      expect(stockHandler).toHaveBeenCalledTimes(1);
    });
  });
});
