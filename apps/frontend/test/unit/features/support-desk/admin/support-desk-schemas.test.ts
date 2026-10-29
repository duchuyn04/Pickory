import { describe, expect, it } from 'vitest';
import { ApiError } from '../../../../../src/shared/api';
import {
  parseCallbackRequests,
  parsePresenceChange,
  parseStaffPresence,
  parseStaffPresenceList,
  parseQueueSessions,
} from '../../../../../src/features/support-desk/admin/support-desk-schemas';

const validSession = {
  id: 'session-1',
  conversationId: 'conversation-1',
  preview: 'Tôi cần đổi địa chỉ giao hàng',
  waitingSeconds: 125,
  handoffAt: '2026-10-22T08:00:00Z',
};

describe('parseQueueSessions', () => {
  it('nhận danh sách đúng hợp đồng', () => {
    expect(parseQueueSessions([validSession])).toEqual([validSession]);
  });

  it('ném ApiError khi thiếu trường bắt buộc, để giao diện hiện lỗi thay vì vỡ', () => {
    const incomplete = {
      id: validSession.id,
      conversationId: validSession.conversationId,
      waitingSeconds: validSession.waitingSeconds,
      handoffAt: validSession.handoffAt,
    };

    expect(() => parseQueueSessions([incomplete])).toThrow(ApiError);
  });

  it('ném ApiError khi dữ liệu không phải mảng', () => {
    expect(() => parseQueueSessions({ data: [] })).toThrow(ApiError);
  });
});

describe('parseCallbackRequests', () => {
  it('nhận danh sách đúng hợp đồng', () => {
    const row = {
      id: 'cb-1',
      phone: '0900000001',
      status: 'pending',
      createdAt: '2026-10-22T08:00:00Z',
    };

    expect(parseCallbackRequests([row])).toEqual([row]);
  });

  it('ném ApiError khi status không phải chuỗi', () => {
    const row = { id: 'cb-1', phone: '0900000001', status: 1, createdAt: '2026-10-22T08:00:00Z' };

    expect(() => parseCallbackRequests([row])).toThrow(ApiError);
  });
});

describe('trạng thái sẵn sàng', () => {
  it('nhận một dòng và một danh sách trạng thái', () => {
    const row = { staffId: 'staff-1', status: 'available', lastSeenAt: '2026-10-22T08:00:00Z' };

    expect(parseStaffPresence(row)).toEqual(row);
    expect(parseStaffPresenceList([row])).toEqual([row]);
  });

  it('từ chối giá trị status ngoài available và away', () => {
    const row = { staffId: 'staff-1', status: 'busy', lastSeenAt: '2026-10-22T08:00:00Z' };

    expect(() => parseStaffPresence(row)).toThrow(ApiError);
  });
});

describe('parsePresenceChange', () => {
  it('đọc dữ liệu của sự kiện presence.changed', () => {
    expect(parsePresenceChange({ staffId: 'staff-1', status: 'away' })).toEqual({
      staffId: 'staff-1',
      status: 'away',
    });
  });

  it('trả null khi dữ liệu sự kiện sai dạng, vì sự kiện không được làm vỡ trang', () => {
    expect(parsePresenceChange(null)).toBeNull();
    expect(parsePresenceChange({ staffId: 'staff-1', status: 'busy' })).toBeNull();
  });
});
