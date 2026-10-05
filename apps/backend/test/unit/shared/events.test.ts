import { describe, expect, it, vi } from 'vitest';
import { EventBus } from '../../../src/shared/events';

describe('EventBus', () => {
  it('gọi handler với payload', async () => {
    const bus = new EventBus();
    const handler = vi.fn();
    bus.on('UserDeletedEvent', handler);
    await bus.emit('UserDeletedEvent', { userId: 'u1' });
    expect(handler).toHaveBeenCalledWith({ userId: 'u1' });
  });

  it('handler ném lỗi thì không ném ra nơi phát và handler sau vẫn chạy', async () => {
    const bus = new EventBus();
    const after = vi.fn();
    bus.on('UserDeletedEvent', () => {
      throw new Error('boom');
    });
    bus.on('UserDeletedEvent', after);
    await expect(bus.emit('UserDeletedEvent', { userId: 'u1' })).resolves.toBeUndefined();
    expect(after).toHaveBeenCalled();
  });

  it('gỡ bỏ handler bằng hàm off mà không ảnh hưởng handler khác', async () => {
    const bus = new EventBus();
    const handlerA = vi.fn();
    const handlerB = vi.fn();

    bus.on('UserDeletedEvent', handlerA);
    bus.on('UserDeletedEvent', handlerB);

    // Gỡ bỏ handlerA
    bus.off('UserDeletedEvent', handlerA);

    await bus.emit('UserDeletedEvent', { userId: 'u2' });
    expect(handlerA).not.toHaveBeenCalled();
    expect(handlerB).toHaveBeenCalledWith({ userId: 'u2' });
  });

  it('gỡ bỏ handler bằng hàm hủy đăng ký trả về từ hàm on', async () => {
    const bus = new EventBus();
    const handler = vi.fn();
    const unsubscribe = bus.on('UserDeletedEvent', handler);

    // Phát sự kiện lần đầu: handler nhận được
    await bus.emit('UserDeletedEvent', { userId: 'u3' });
    expect(handler).toHaveBeenCalledTimes(1);

    // Gọi hàm hủy đăng ký
    unsubscribe();

    // Phát sự kiện lần hai: handler không nhận thêm
    await bus.emit('UserDeletedEvent', { userId: 'u3' });
    expect(handler).toHaveBeenCalledTimes(1);
  });
});
