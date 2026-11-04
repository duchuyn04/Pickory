import { describe, expect, it } from 'vitest';
import { mergeMessagesById } from '../../../../../src/features/support-desk/customer/support-conversation-state';
import type { SupportMessage } from '../../../../../src/features/support-desk/customer/support-customer-schemas';

function message(partial: Partial<SupportMessage> & { id: string }): SupportMessage {
  return {
    role: 'customer',
    body: 'Xin chào',
    action: null,
    staffDisplayName: null,
    createdAt: '2026-11-03T02:00:00Z',
    ...partial,
  };
}

describe('mergeMessagesById', () => {
  it('gộp tin theo `id` và sắp xếp theo `createdAt`, tin mới ở cuối', () => {
    const older = message({ id: 'm1', createdAt: '2026-11-03T02:00:00Z' });
    const newer = message({ id: 'm2', createdAt: '2026-11-03T02:05:00Z' });

    const merged = mergeMessagesById([newer], [older]);

    expect(merged.map((item) => item.id)).toEqual(['m1', 'm2']);
  });

  it('tin có `id` đã hiện chỉ hiện một lần', () => {
    const fromHistory = message({ id: 'm1', body: 'bản cũ' });
    const fromEvent = message({ id: 'm1', body: 'bản mới' });

    const merged = mergeMessagesById([fromHistory], [fromEvent]);

    expect(merged).toHaveLength(1);
    // Bản đến sau ghi đè, để nội dung luôn khớp dữ liệu mới nhất của máy chủ.
    expect(merged[0]?.body).toBe('bản mới');
  });

  it('sắp xếp theo thời điểm, sai khi so chuỗi với phần lẻ giây dài khác nhau', () => {
    // Hai mốc cùng giây nhưng phần lẻ khác độ dài. So chuỗi sẽ xếp `.12Z` sau `.123Z`.
    const earlier = message({ id: 'm1', createdAt: '2026-11-03T02:00:00.12Z' });
    const later = message({ id: 'm2', createdAt: '2026-11-03T02:00:00.123Z' });

    const merged = mergeMessagesById([later], [earlier]);

    expect(merged.map((item) => item.id)).toEqual(['m1', 'm2']);
  });

  it('cùng thời điểm thì so `id` để thứ tự ổn định', () => {
    const b = message({ id: 'm-b', createdAt: '2026-11-03T02:00:00Z' });
    const a = message({ id: 'm-a', createdAt: '2026-11-03T02:00:00Z' });

    const merged = mergeMessagesById([b], [a]);

    expect(merged.map((item) => item.id)).toEqual(['m-a', 'm-b']);
  });
});
