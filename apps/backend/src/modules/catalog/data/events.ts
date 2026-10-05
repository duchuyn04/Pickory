import type { Prisma, PrismaClient } from '../../../generated/prisma/client';
import { eventBus, type DomainEvents } from '../../../shared/events';

// Đối tượng gom sự kiện trong lúc transaction đang chạy.
export interface TransactionEventCollector {
  emit<K extends keyof DomainEvents>(event: K, payload: DomainEvents[K]): void;
}
// Tạo collector gom sự kiện và hàm flush phát sự kiện cho bên ngoài tự quản lý transaction.
export function createEventCollector(): {
  collector: TransactionEventCollector;
  flush: () => Promise<void>;
  discard: () => void;
} {
  let queuedEvents: Array<() => Promise<void>> = [];

  const collector: TransactionEventCollector = {
    emit(event, payload) {
      queuedEvents.push(() => eventBus.emit(event, payload));
    },
  };

  const discard = (): void => {
    // Xóa bỏ toàn bộ sự kiện đã gom khi bên ngoài rollback transaction.
    queuedEvents = [];
  };

  const flush = async (): Promise<void> => {
    // Chuyển toàn bộ sự kiện sang danh sách tạm và làm rỗng hàng đợi ngay lập tức.
    const eventsToEmit = queuedEvents;
    queuedEvents = [];

    // Phát toàn bộ sự kiện đã gom; gọi lần hai sẽ không phát lại do hàng đợi đã rỗng.
    for (const emitEvent of eventsToEmit) {
      await emitEvent();
    }
  };

  return { collector, flush, discard };
}

// Tự mở transaction để chỉ phát sự kiện sau khi commit. Lỗi trong transaction bỏ toàn bộ sự kiện.
export async function emitAfterCommit<T>(
  dbClient: PrismaClient,
  action: (tx: Prisma.TransactionClient, collector: TransactionEventCollector) => Promise<T>,
): Promise<T> {
  const { collector, flush } = createEventCollector();

  // Thực hiện toàn bộ thao tác ghi trong transaction của cơ sở dữ liệu.
  const result = await dbClient.$transaction(async (tx) => {
    return await action(tx, collector);
  });

  // Chỉ phát sự kiện ra event bus sau khi transaction đã hoàn tất lưu trữ.
  await flush();

  return result;
}

// Đăng ký lắng nghe sự kiện thay đổi dữ liệu sản phẩm và trả về hàm gỡ bỏ.
export function onProductChanged(
  handler: (payload: DomainEvents['ProductChangedEvent']) => void | Promise<void>,
): () => void {
  return eventBus.on('ProductChangedEvent', handler);
}

// Đăng ký lắng nghe sự kiện thay đổi tồn kho biến thể SKU và trả về hàm gỡ bỏ.
export function onStockChanged(
  handler: (payload: DomainEvents['StockChangedEvent']) => void | Promise<void>,
): () => void {
  return eventBus.on('StockChangedEvent', handler);
}
