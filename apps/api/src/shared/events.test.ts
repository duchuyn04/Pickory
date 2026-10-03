import { describe, expect, it, vi } from 'vitest';
import { EventBus } from './events';

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
});
