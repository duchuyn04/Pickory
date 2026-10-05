import { eventBus, type DomainEvents } from '../../../shared/events';

// Đối tượng gom sự kiện trong lúc transaction đang chạy.
export interface TransactionEventCollector {
  emit<K extends keyof DomainEvents>(event: K, payload: DomainEvents[K]): void;
}

// Gom sự kiện trong transaction và chỉ phát sau khi transaction commit thành công.
export async function emitAfterCommit<T>(
  action: (collector: TransactionEventCollector) => Promise<T>,
): Promise<T> {
  const queuedEvents: Array<() => Promise<void>> = [];

  const collector: TransactionEventCollector = {
    emit(event, payload) {
      queuedEvents.push(() => eventBus.emit(event, payload));
    },
  };

  // Chạy hành động chính. Nếu xảy ra lỗi thì dừng ngay và bỏ qua toàn bộ sự kiện.
  const result = await action(collector);

  // Phát toàn bộ sự kiện đã gom sau khi thao tác cơ sở dữ liệu đã commit.
  for (const emitEvent of queuedEvents) {
    await emitEvent();
  }

  return result;
}

// Đăng ký lắng nghe sự kiện thay đổi dữ liệu sản phẩm.
export function onProductChanged(
  handler: (payload: DomainEvents['ProductChangedEvent']) => void | Promise<void>,
): void {
  eventBus.on('ProductChangedEvent', handler);
}

// Đăng ký lắng nghe sự kiện thay đổi tồn kho biến thể SKU.
export function onStockChanged(
  handler: (payload: DomainEvents['StockChangedEvent']) => void | Promise<void>,
): void {
  eventBus.on('StockChangedEvent', handler);
}
