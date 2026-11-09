import { describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../../../src/shared/api';
import {
  claimSupportSession,
  closeSupportSession,
  fetchSessionCustomer,
  sendStaffMessage,
  transferSupportSession,
} from '../../../../../src/features/support-desk/admin/support-desk-api';
import {
  parseCallbackRequests,
  parseCannedResponses,
  parseClosedSession,
  parseMessageCreatedEvent,
  parseQueueSessions,
  parseSessionAssignment,
  parseSessionCustomer,
  parseStaffMessagePage,
  parseStaffPresence,
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

describe('schema bảng thông tin khách đọc ví dụ của admin-customer-panel.json', () => {
  it.each(['khách đã đăng nhập', 'khách vãng lai'])('đọc được ví dụ "%s"', (exampleName) => {
    const example = findSupportDeskExample('admin-customer-panel.json', exampleName);
    const response = example.response as { data: unknown };

    // Kiểm tra cấu trúc dữ liệu khách và danh sách đơn khớp với schema.
    expect(parseSessionCustomer(response.data)).toEqual(response.data);
  });

  it('hàm fetchSessionCustomer gọi đúng endpoint và trả dữ liệu đã parse', async () => {
    const example = findSupportDeskExample('admin-customer-panel.json', 'khách đã đăng nhập');
    const exampleResponse = example.response;
    if (!exampleResponse || typeof exampleResponse !== 'object' || !('data' in exampleResponse)) {
      throw new Error('Ví dụ thiếu trường data.');
    }
    const fetchMock = vi.fn(async () => Response.json(exampleResponse, { status: example.status }));
    vi.stubGlobal('fetch', fetchMock);

    const result = await fetchSessionCustomer('5e550000-0000-4000-8000-000000000001');

    // Xác nhận hàm gọi đúng route và trả dữ liệu khách khớp với file mẫu.
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/admin/support/sessions/5e550000-0000-4000-8000-000000000001/customer',
      expect.objectContaining({ credentials: 'include' }),
    );
    expect(result).toEqual(exampleResponse.data);
  });
});

describe('chuyển phiên đọc ví dụ của admin-sessions.json', () => {
  it('đọc được ví dụ "chuyển phiên cho đồng nghiệp"', () => {
    const example = findSupportDeskExample('admin-sessions.json', 'chuyển phiên cho đồng nghiệp');
    const response = example.response as { data: unknown };

    // Xác nhận response chuyển phiên đọc được bằng parseSessionAssignment.
    expect(parseSessionAssignment(response.data)).toEqual(response.data);
  });

  it('hàm transferSupportSession gửi PUT với staffId và note', async () => {
    const example = findSupportDeskExample('admin-sessions.json', 'chuyển phiên cho đồng nghiệp');
    const exampleResponse = example.response;
    if (!exampleResponse || typeof exampleResponse !== 'object' || !('data' in exampleResponse)) {
      throw new Error('Ví dụ thiếu trường data.');
    }
    const fetchMock = vi.fn(async () => Response.json(exampleResponse, { status: example.status }));
    vi.stubGlobal('fetch', fetchMock);

    const result = await transferSupportSession(
      '5e550000-0000-4000-8000-000000000001',
      'staff-0002',
      'Khách cần đổi size, đơn PK098 đã giao.',
    );

    // Kiểm tra hàm gửi body đúng định dạng JSON lên endpoint chuyển giao.
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/admin/support/sessions/5e550000-0000-4000-8000-000000000001/assignment',
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({
          staffId: 'staff-0002',
          note: 'Khách cần đổi size, đơn PK098 đã giao.',
        }),
      }),
    );
    expect(result).toEqual(exampleResponse.data);
  });

  it('lỗi 409 khi chuyển cho người đang tạm vắng thành ApiError với mã SUPPORT_STAFF_UNAVAILABLE', async () => {
    const example = findSupportDeskExample('admin-sessions.json', 'chuyển cho người đang tạm vắng');
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => Response.json(example.response, { status: example.status })),
    );

    const caught = await transferSupportSession(
      '5e550000-0000-4000-8000-000000000001',
      'staff-0003',
      null,
    ).catch((error: unknown) => error);

    // Xác nhận lỗi 409 chuyển thành ApiError với thông báo máy chủ.
    expect(caught).toBeInstanceOf(ApiError);
    expect(caught).toMatchObject({
      code: 'SUPPORT_STAFF_UNAVAILABLE',
      status: 409,
      message: 'Người nhận không ở trạng thái sẵn sàng.',
    });
  });
});

describe('kết thúc phiên đọc ví dụ của admin-sessions.json', () => {
  it('đọc được ví dụ "kết thúc phiên"', () => {
    const example = findSupportDeskExample('admin-sessions.json', 'kết thúc phiên');
    const response = example.response as { data: unknown };

    // Xác nhận response kết thúc phiên đọc được bằng parseClosedSession.
    expect(parseClosedSession(response.data)).toEqual(response.data);
  });

  it('hàm closeSupportSession gửi PATCH với status closed', async () => {
    const example = findSupportDeskExample('admin-sessions.json', 'kết thúc phiên');
    const exampleResponse = example.response;
    if (!exampleResponse || typeof exampleResponse !== 'object' || !('data' in exampleResponse)) {
      throw new Error('Ví dụ thiếu trường data.');
    }
    const fetchMock = vi.fn(async () => Response.json(exampleResponse, { status: example.status }));
    vi.stubGlobal('fetch', fetchMock);

    const result = await closeSupportSession('5e550000-0000-4000-8000-000000000001');

    // Kiểm tra hàm gửi đúng PATCH status closed lên route phiên.
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/admin/support/sessions/5e550000-0000-4000-8000-000000000001',
      expect.objectContaining({
        method: 'PATCH',
        body: JSON.stringify({ status: 'closed' }),
      }),
    );
    expect(result).toEqual(exampleResponse.data);
  });
});

describe('trường displayName trong staffPresenceSchema', () => {
  it('đọc được khi có chuỗi tên hiển thị', () => {
    const raw = {
      staffId: 'staff-0001',
      status: 'available',
      lastSeenAt: '2026-11-09T08:00:00Z',
      displayName: 'Nguyễn Văn A',
    };

    // Giữ nguyên chuỗi tên hiển thị khi máy chủ cung cấp.
    expect(parseStaffPresence(raw)).toEqual(raw);
  });

  it('đọc được khi displayName là null', () => {
    const raw = {
      staffId: 'staff-0001',
      status: 'available',
      lastSeenAt: '2026-11-09T08:00:00Z',
      displayName: null,
    };

    // Chấp nhận giá trị null khi nhân viên chưa đặt tên hiển thị.
    expect(parseStaffPresence(raw)).toEqual(raw);
  });

  it('đọc được khi thiếu trường displayName', () => {
    const raw = {
      staffId: 'staff-0001',
      status: 'available',
      lastSeenAt: '2026-11-09T08:00:00Z',
    };

    // Chấp nhận phản hồi từ backend hiện tại khi chưa có trường này.
    expect(parseStaffPresence(raw)).toEqual({ ...raw, displayName: undefined });
  });
});
