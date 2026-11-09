import { describe, expect, it } from 'vitest';
import { ApiError } from '../../../../../src/shared/api';
import {
  parseCallbackRequest,
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
  claimedAt: null,
  transferNote: null,
};

const validCallback = {
  id: 'cb-1',
  conversationId: 'conversation-1',
  phone: '0900000001',
  note: null,
  status: 'pending',
  createdAt: '2026-10-22T08:00:00Z',
  handledAt: null,
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
      conversationId: 'conversation-1',
      phone: '0900000001',
      note: null,
      status: 'pending',
      createdAt: '2026-10-22T08:00:00Z',
      handledAt: null,
    };

    expect(parseCallbackRequests([row])).toEqual([row]);
  });

  it('ném ApiError khi status không phải chuỗi', () => {
    const row = {
      id: 'cb-1',
      conversationId: 'conversation-1',
      phone: '0900000001',
      note: null,
      status: 1,
      createdAt: '2026-10-22T08:00:00Z',
      handledAt: null,
    };

    expect(() => parseCallbackRequests([row])).toThrow(ApiError);
  });

  it('ném ApiError khi status ngoài pending, done, unreachable', () => {
    const row = { ...validCallback, status: 'cancelled' };

    expect(() => parseCallbackRequests([row])).toThrow(ApiError);
  });

  it('đọc được dòng có phone bằng null', () => {
    const row = { ...validCallback, phone: null };

    expect(parseCallbackRequests([row])).toEqual([row]);
  });

  it('ném ApiError khi createdAt không có múi giờ', () => {
    const row = { ...validCallback, createdAt: '2026-10-22 08:00' };

    expect(() => parseCallbackRequests([row])).toThrow(ApiError);
  });

  it('ném ApiError khi handledAt là chuỗi giờ hỏng', () => {
    const row = { ...validCallback, status: 'done', handledAt: 'hôm qua' };

    expect(() => parseCallbackRequests([row])).toThrow(ApiError);
  });
});

describe('parseCallbackRequest', () => {
  it('đọc một dòng đã xử lý', () => {
    const row = { ...validCallback, status: 'unreachable', handledAt: '2026-10-22T09:00:00Z' };

    expect(parseCallbackRequest(row)).toEqual(row);
  });

  it('ném ApiError khi nhận mảng thay cho một dòng', () => {
    expect(() => parseCallbackRequest([validCallback])).toThrow(ApiError);
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
