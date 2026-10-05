import { afterAll, afterEach, describe, expect, it, vi } from 'vitest';
import { createDb, db } from '../../../../src/shared/db';
import { loadConfig } from '../../../../src/shared/config';
import {
  createEventCollector,
  emitAfterCommit,
  onProductChanged,
} from '../../../../src/modules/catalog/data/events';

describe('catalog events integration với transaction thật', () => {
  const cleanupFns: Array<() => void> = [];
  const otherDb = createDb(loadConfig().DATABASE_URL);

  afterEach(() => {
    // Gỡ bỏ toàn bộ handler sau mỗi test để giữ sạch event bus dùng chung.
    while (cleanupFns.length > 0) {
      cleanupFns.pop()?.();
    }
  });

  afterAll(async () => {
    // Đóng kết nối phụ sau khi hoàn tất toàn bộ test trong file.
    await otherDb.$disconnect();
  });

  it('không phát sự kiện và rollback dữ liệu khi transaction ném lỗi', async () => {
    const handler = vi.fn();
    cleanupFns.push(onProductChanged(handler));

    const testSlug = 'brand-rollback-test';

    // Thao tác ghi dữ liệu và đẩy sự kiện nhưng ném lỗi trước khi hoàn tất.
    await expect(
      emitAfterCommit(db, async (tx, events) => {
        await tx.brand.create({
          data: {
            name: 'Thương hiệu lỗi',
            slug: testSlug,
          },
        });

        events.emit('ProductChangedEvent', {
          productId: 'prod-rollback',
          change: 'created',
        });

        throw new Error('Cố ý gây lỗi rollback');
      }),
    ).rejects.toThrow('Cố ý gây lỗi rollback');

    // Dữ liệu trong cơ sở dữ liệu phải không tồn tại do transaction đã rollback.
    const createdBrand = await db.brand.findUnique({
      where: { slug: testSlug },
    });
    expect(createdBrand).toBeNull();

    // Handler sự kiện tuyệt đối không được gọi khi transaction thất bại.
    expect(handler).not.toHaveBeenCalled();
  });

  it('phát đúng 1 lần sau khi dữ liệu đã đọc được từ kết nối khác', async () => {
    let readableFromOtherConnection = false;
    let callCount = 0;
    const testSlug = 'brand-commit-test';

    // Đăng ký handler kiểm tra sự hiện diện của dữ liệu qua kết nối độc lập khác.
    cleanupFns.push(
      onProductChanged(async () => {
        callCount++;
        const record = await otherDb.brand.findUnique({
          where: { slug: testSlug },
        });
        if (record) {
          readableFromOtherConnection = true;
        }
      }),
    );

    // Chạy commit thành công qua emitAfterCommit.
    await emitAfterCommit(db, async (tx, events) => {
      await tx.brand.create({
        data: {
          name: 'Thương hiệu thành công',
          slug: testSlug,
        },
      });

      events.emit('ProductChangedEvent', {
        productId: 'prod-commit',
        change: 'created',
      });
    });

    // Xác nhận handler được gọi đúng 1 lần.
    expect(callCount).toBe(1);

    // Xác nhận dữ liệu đã được commit xong xuôi trước khi handler sự kiện chạy.
    expect(readableFromOtherConnection).toBe(true);
  });

  it('quản lý đúng vòng đời của collector khi bên ngoài tự kiểm soát transaction', async () => {
    const handler = vi.fn();
    cleanupFns.push(onProductChanged(handler));

    const testSlug = 'brand-external-tx-test';

    // 1. Trường hợp rollback transaction bên ngoài: không gọi flush thì không phát.
    const rollbackCollector = createEventCollector();
    await expect(
      db.$transaction(async (tx) => {
        await tx.brand.create({
          data: {
            name: 'Thương hiệu bên ngoài 1',
            slug: `${testSlug}-fail`,
          },
        });

        rollbackCollector.collector.emit('ProductChangedEvent', {
          productId: 'prod-external-fail',
          change: 'created',
        });

        throw new Error('Lỗi rollback bên ngoài');
      }),
    ).rejects.toThrow('Lỗi rollback bên ngoài');

    // Không gọi flush thì sự kiện không bao giờ được phát.
    expect(handler).not.toHaveBeenCalled();

    // 2. Trường hợp commit thành công và gọi flush: phát đúng 1 lần, gọi lại không phát.
    const commitCollector = createEventCollector();
    await db.$transaction(async (tx) => {
      await tx.brand.create({
        data: {
          name: 'Thương hiệu bên ngoài 2',
          slug: `${testSlug}-success`,
        },
      });

      commitCollector.collector.emit('ProductChangedEvent', {
        productId: 'prod-external-success',
        change: 'created',
      });
    });

    // Gọi flush lần thứ nhất: phát sự kiện.
    await commitCollector.flush();
    expect(handler).toHaveBeenCalledTimes(1);

    // Gọi flush lần thứ hai: không phát lại do hàng đợi đã rỗng.
    await commitCollector.flush();
    expect(handler).toHaveBeenCalledTimes(1);
  });
});
