import { logger } from './logger';

// Danh mục sự kiện giữa các module. Thêm sự kiện mới tại đây để có kiểu chặt.
export interface DomainEvents {
  UserLinkedEvent: { anonymousId: string; userId: string };
  UserDeletedEvent: { userId: string };
  // Sự kiện phát khi sản phẩm thay đổi trạng thái hoặc thông tin.
  ProductChangedEvent: {
    productId: string;
    change: 'created' | 'updated' | 'status' | 'deleted';
  };
  // Sự kiện phát khi số lượng tồn kho của SKU thay đổi.
  StockChangedEvent: {
    skuId: string;
    stock: number;
    previousStock: number;
  };
}

// Hàm xử lý sự kiện, có thể bất đồng bộ.
type Handler<K extends keyof DomainEvents> = (payload: DomainEvents[K]) => void | Promise<void>;

/**
 * Event bus trong process (MVP chạy 1 instance). Module phát sự kiện không cần biết
 * module nào lắng nghe, nhờ vậy giữ được ranh giới giữa các module.
 */
export class EventBus {
  private readonly handlers = new Map<keyof DomainEvents, Handler<never>[]>();

  // Đăng ký handler cho một sự kiện; payload được kiểm kiểu theo `DomainEvents`.
  on<K extends keyof DomainEvents>(event: K, handler: Handler<K>): void {
    const list = this.handlers.get(event) ?? [];
    list.push(handler as Handler<never>);
    this.handlers.set(event, list);
  }

  // Phát sự kiện tới lần lượt từng handler. Handler lỗi chỉ bị log, không ném ra nơi phát
  // và không chặn handler sau (ví dụ đăng nhập không được thất bại vì handler gộp consent lỗi).
  async emit<K extends keyof DomainEvents>(event: K, payload: DomainEvents[K]): Promise<void> {
    for (const handler of this.handlers.get(event) ?? []) {
      try {
        await (handler as Handler<K>)(payload);
      } catch (err) {
        logger.error({ err, event }, 'event handler failed');
      }
    }
  }
}

// Bus dùng chung cho toàn API.
export const eventBus = new EventBus();
