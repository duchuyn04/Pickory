import { describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../../../src/shared/api';
import {
  claimSupportSession,
  sendStaffMessage,
} from '../../../../../src/features/support-desk/admin/support-desk-api';
import {
  parseCallbackRequests,
  parseCannedResponses,
  parseMessageCreatedEvent,
  parseQueueSessions,
  parseSessionAssignment,
  parseStaffMessagePage,
  parseStaffViewMessage,
} from '../../../../../src/features/support-desk/admin/support-desk-schemas';
import { findSupportDeskExample, selectSupportDeskEvents } from '../contract-fixtures';

// Ví dụ trong file mẫu là hợp đồng dùng chung với backend. Schema phải đọc được mọi ví dụ màn CSKH dùng.
describe('schema hàng chờ đọc ví dụ của admin-sessions.json', () => {
  it.each(['hàng chờ', 'phiên của tôi sau khi đồng nghiệp chuyển'])(
    'đọc được ví dụ %s',
    (exampleName) => {
      const example = findSupportDeskExample('admin-sessions.json', exampleName);
      const response = example.response as { data: unknown };

      // Đọc rồi trả lại đúng dữ liệu mẫu. Schema thiếu trường hay sai kiểu thì case này đỏ.
      expect(parseQueueSessions(response.data)).toEqual(response.data);
    },
  );
});

describe('schema yêu cầu gọi lại đọc ví dụ của admin-callbacks.json', () => {
  it('đọc được yêu cầu đã xóa số sau 30 ngày', () => {
    const example = findSupportDeskExample('admin-callbacks.json', 'danh sách yêu cầu gọi lại');
    const response = example.response as { data: unknown };

    expect(parseCallbackRequests(response.data)).toEqual(response.data);
  });
});

describe('nhận phiên đọc ví dụ của admin-sessions.json', () => {
  it('đọc được ví dụ "nhận phiên"', () => {
    const example = findSupportDeskExample('admin-sessions.json', 'nhận phiên');
    const response = example.response as { data: unknown };

    expect(parseSessionAssignment(response.data)).toEqual(response.data);
  });

  it('lỗi 409 khi nhận phiên đã được nhận trước thành ApiError với mã SUPPORT_SESSION_ALREADY_CLAIMED', async () => {
    const example = findSupportDeskExample('admin-sessions.json', 'nhận phiên đã có người nhận');
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => Response.json(example.response, { status: example.status })),
    );

    const caught = await claimSupportSession('5e550000-0000-4000-8000-000000000001').catch(
      (error: unknown) => error,
    );

    expect(caught).toBeInstanceOf(ApiError);
    expect(caught).toMatchObject({
      code: 'SUPPORT_SESSION_ALREADY_CLAIMED',
      status: 409,
      message: 'Đã có người nhận hội thoại này.',
    });
  });
});

describe('gửi tin phía nhân viên đọc ví dụ của admin-sessions.json', () => {
  it('đọc được ví dụ "nhân viên gửi tin"', () => {
    const example = findSupportDeskExample('admin-sessions.json', 'nhân viên gửi tin');
    const response = example.response as { data: unknown };

    expect(parseStaffViewMessage(response.data)).toEqual(response.data);
  });

  it('lỗi 403 khi gửi tin mà không giữ phiên thành ApiError với mã SUPPORT_SESSION_NOT_ASSIGNED', async () => {
    const example = findSupportDeskExample('admin-sessions.json', 'người không giữ phiên gửi tin');
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => Response.json(example.response, { status: example.status })),
    );

    const caught = await sendStaffMessage(
      '5e550000-0000-4000-8000-000000000001',
      'Chào bạn.',
    ).catch((error: unknown) => error);

    expect(caught).toBeInstanceOf(ApiError);
    expect(caught).toMatchObject({
      code: 'SUPPORT_SESSION_NOT_ASSIGNED',
      status: 403,
      message: 'Bạn chưa nhận hội thoại này nên không gửi tin được.',
    });
  });
});

describe('lịch sử tin cho nhân viên đọc ví dụ của admin-sessions.json', () => {
  it('đọc được ví dụ "lịch sử tin của hội thoại cho nhân viên"', () => {
    const example = findSupportDeskExample(
      'admin-sessions.json',
      'lịch sử tin của hội thoại cho nhân viên',
    );
    const response = example.response as { data: unknown };

    expect(parseStaffMessagePage(response.data)).toEqual(response.data);
  });
});

describe('mẫu trả lời nhanh đọc ví dụ của admin-canned-responses.json', () => {
  it('đọc được ví dụ "danh sách mẫu"', () => {
    const example = findSupportDeskExample('admin-canned-responses.json', 'danh sách mẫu');
    const response = example.response as { data: unknown };

    expect(parseCannedResponses(response.data)).toEqual(response.data);
  });
});

describe('sự kiện message.created đọc ví dụ của admin-events.json', () => {
  it('đọc được dữ liệu sự kiện mẫu', () => {
    const [event] = selectSupportDeskEvents('admin-events.json', ['message.created']);
    if (event === undefined) throw new Error('Thiếu sự kiện message.created trong file mẫu.');

    expect(parseMessageCreatedEvent(event.data)).toEqual(event.data);
  });
});
