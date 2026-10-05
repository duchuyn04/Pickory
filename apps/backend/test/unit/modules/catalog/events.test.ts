import { describe, expect, it, vi } from 'vitest';
import {
  emitAfterCommit,
  onProductChanged,
  onStockChanged,
} from '../../../../src/modules/catalog/data/events';

describe('catalog events và emitAfterCommit', () => {
  it('không phát sự kiện khi transaction ném lỗi', async () => {
    const handler = vi.fn();
    onProductChanged(handler);

    await expect(
      emitAfterCommit(async (events) => {
        events.emit('ProductChangedEvent', {
          productId: 'prod-fail',
          change: 'created',
        });
        throw new Error('Lỗi cố ý trong transaction');
      }),
    ).rejects.toThrow('Lỗi cố ý trong transaction');

    expect(handler).not.toHaveBeenCalled();
  });

  it('phát đúng 1 lần sau khi transaction commit thành công', async () => {
    const productHandler = vi.fn();
    const stockHandler = vi.fn();

    onProductChanged(productHandler);
    onStockChanged(stockHandler);

    const result = await emitAfterCommit(async (events) => {
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
    expect(productHandler).toHaveBeenCalledWith({
      productId: 'prod-success',
      change: 'created',
    });
    expect(stockHandler).toHaveBeenCalledTimes(1);
    expect(stockHandler).toHaveBeenCalledWith({
      skuId: 'sku-success',
      stock: 10,
      previousStock: 0,
    });
  });
});
