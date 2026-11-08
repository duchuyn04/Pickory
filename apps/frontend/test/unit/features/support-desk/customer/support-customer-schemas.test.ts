import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../../../src/shared/api';
import {
  parseSupportCallbackRequest,
  parseSupportConversation,
  parseSupportCsatResponse,
  parseSupportCustomerEvent,
  parseSupportMessage,
  parseSupportMessagePage,
} from '../../../../../src/features/support-desk/customer/support-customer-schemas';
import {
  sendCallbackRequest,
  sendSupportMessage,
  submitSupportCsat,
} from '../../../../../src/features/support-desk/customer/support-customer-api';
import {
  findSupportDeskExample,
  selectSupportDeskEvents,
  type ContractExample,
} from '../contract-fixtures';

const CUSTOMER_API_FIXTURE = 'customer-api.json';
const CUSTOMER_EVENTS_FIXTURE = 'customer-events.json';

afterEach(() => {
  vi.unstubAllGlobals();
});

// Phần `data` của response mẫu là dữ liệu ngoài, nên kiểm hình dạng trước khi đưa vào schema.
function readExampleData(example: ContractExample): unknown {
  const { response } = example;
  if (response !== null && typeof response === 'object' && 'data' in response) {
    return response.data;
  }
  throw new Error(`Ví dụ "${example.name}" không có phần "data".`);
}

describe('schema phía khách của support desk', () => {
  it('đọc ví dụ "tạo hội thoại lần đầu" thành hội thoại với câu báo 90 ngày', () => {
    const example = findSupportDeskExample(CUSTOMER_API_FIXTURE, 'tạo hội thoại lần đầu');

    const conversation = parseSupportConversation(readExampleData(example));

    expect(conversation.id).toBe('c0a80000-0000-4000-8000-000000000001');
    expect(conversation.showRetentionNotice).toBe(true);
    expect(conversation.session).toBeNull();
  });

  it('đọc ví dụ "mở lại hội thoại đang có phiên chờ" thành phiên chờ', () => {
    const example = findSupportDeskExample(
      CUSTOMER_API_FIXTURE,
      'mở lại hội thoại đang có phiên chờ',
    );

    const conversation = parseSupportConversation(readExampleData(example));

    expect(conversation.showRetentionNotice).toBe(false);
    expect(conversation.session).toEqual({
      id: '5e550000-0000-4000-8000-000000000001',
      status: 'waiting',
      csatScore: null,
    });
  });

  it('thiếu khóa `session` thì đọc thành `null`', () => {
    const example = findSupportDeskExample(CUSTOMER_API_FIXTURE, 'tạo hội thoại lần đầu');
    const data = readExampleData(example);
    if (data === null || typeof data !== 'object') {
      throw new Error('Ví dụ "tạo hội thoại lần đầu" không phải một object.');
    }
    // Backend nhánh này chưa trả khóa `session`. Bỏ khóa để giả lùi đúng phản hồi thật hiện nay.
    const withoutSession = Object.fromEntries(
      Object.entries(data).filter(([key]) => key !== 'session'),
    );

    const conversation = parseSupportConversation(withoutSession);

    expect(conversation.session).toBeNull();
  });

  it('thiếu khóa `staffDisplayName` thì đọc thành `null`', () => {
    const example = findSupportDeskExample(CUSTOMER_API_FIXTURE, 'khách gửi tin');
    const data = readExampleData(example);
    if (data === null || typeof data !== 'object') {
      throw new Error('Ví dụ "khách gửi tin" không phải một object.');
    }
    // Backend nhánh này chưa trả khóa `staffDisplayName`. Bỏ khóa để giả lùi đúng phản hồi thật hiện nay.
    const withoutDisplayName = Object.fromEntries(
      Object.entries(data).filter(([key]) => key !== 'staffDisplayName'),
    );

    const message = parseSupportMessage(withoutDisplayName);

    expect(message.staffDisplayName).toBeNull();
  });

  it('đọc ví dụ "lịch sử tin, trang cuối" thành trang tin', () => {
    const example = findSupportDeskExample(CUSTOMER_API_FIXTURE, 'lịch sử tin, trang cuối');

    const page = parseSupportMessagePage(readExampleData(example));

    expect(page.items).toHaveLength(3);
    expect(page.nextCursor).toBeNull();
    expect(page.items.map((item) => item.role)).toEqual(['customer', 'system', 'staff']);
    // Tin nhân viên có tên ngắn theo hợp đồng. Đọc qua `at` để không lệch khi đổi thứ tự mẫu.
    const staffMessage = page.items.at(-1);
    expect(staffMessage?.staffDisplayName).toBe('Lan - CSKH');
  });

  it('đọc ví dụ "khách gửi tin" thành một tin', () => {
    const example = findSupportDeskExample(CUSTOMER_API_FIXTURE, 'khách gửi tin');

    const message = parseSupportMessage(readExampleData(example));

    expect(message.role).toBe('customer');
    expect(message.body).toBe('Cảm ơn bạn.');
    expect(message.action).toBeNull();
  });

  it('đọc mọi sự kiện của customer-events.json', () => {
    const events = selectSupportDeskEvents(CUSTOMER_EVENTS_FIXTURE, [
      'message.created',
      'session.waiting',
      'session.assigned',
      'session.closed',
    ]);

    for (const event of events) {
      const parsed = parseSupportCustomerEvent(event.name, event.data);
      expect(parsed).not.toBeNull();
    }
  });

  it('sự kiện `message.created` trả tin có tên nhân viên', () => {
    const [event] = selectSupportDeskEvents(CUSTOMER_EVENTS_FIXTURE, ['message.created']);
    if (event === undefined) throw new Error('Thiếu sự kiện message.created trong file mẫu.');

    const parsed = parseSupportCustomerEvent(event.name, event.data);

    expect(parsed).toEqual({
      kind: 'message',
      message: expect.objectContaining({ role: 'staff', staffDisplayName: 'Lan - CSKH' }),
    });
  });

  it('sự kiện có tên lạ bị bỏ qua', () => {
    expect(parseSupportCustomerEvent('message.deleted', { id: 'x' })).toBeNull();
    expect(parseSupportCustomerEvent('heartbeat', { at: '2026-11-03T00:00:00Z' })).toBeNull();
  });

  it('dữ liệu sự kiện hỏng bị bỏ qua thay vì làm vỡ trang', () => {
    expect(parseSupportCustomerEvent('session.waiting', { wrong: true })).toBeNull();
    expect(parseSupportCustomerEvent('message.created', 'not-an-object')).toBeNull();
  });

  it('đọc ví dụ "khách để lại số điện thoại" thành yêu cầu gọi lại chờ', () => {
    const example = findSupportDeskExample(CUSTOMER_API_FIXTURE, 'khách để lại số điện thoại');
    // Schema bảo đảm yêu cầu gọi lại có mã, trạng thái chờ và thời điểm tạo.
    const callbackRequest = parseSupportCallbackRequest(readExampleData(example));
    expect(callbackRequest).toEqual({
      id: 'cb000000-0000-4000-8000-000000000001',
      status: 'pending',
      createdAt: '2026-11-03T14:05:00Z',
    });
  });

  it('đọc ví dụ "khách chấm điểm phiên vừa kết thúc" thành điểm CSAT hợp lệ', () => {
    const example = findSupportDeskExample(
      CUSTOMER_API_FIXTURE,
      'khách chấm điểm phiên vừa kết thúc',
    );
    // Schema bảo đảm điểm số nằm trong khoảng từ 1 đến 5.
    const csat = parseSupportCsatResponse(readExampleData(example));
    expect(csat).toEqual({
      sessionId: '5e550000-0000-4000-8000-000000000001',
      score: 5,
    });
  });
});

describe('gửi tin phía khách', () => {
  it('lỗi 429 thành ApiError với mã và câu tiếng Việt của API', async () => {
    const example = findSupportDeskExample(CUSTOMER_API_FIXTURE, 'khách gửi quá giới hạn');
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => Response.json(example.response, { status: example.status })),
    );

    const caught = await sendSupportMessage('c0a80000-0000-4000-8000-000000000001', 'Alo?').catch(
      (error: unknown) => error,
    );

    expect(caught).toBeInstanceOf(ApiError);
    expect(caught).toMatchObject({
      code: 'SUPPORT_MESSAGE_RATE_LIMITED',
      status: 429,
      message: 'Bạn gửi tin quá nhanh. Hãy chờ một chút rồi gửi lại.',
    });
  });
});

describe('yêu cầu gọi lại phía khách', () => {
  it('lỗi 429 "yêu cầu gọi lại thứ 4 trong ngày" thành ApiError với mã CALLBACK_LIMIT_REACHED', async () => {
    const example = findSupportDeskExample(
      CUSTOMER_API_FIXTURE,
      'yêu cầu gọi lại thứ 4 trong ngày',
    );
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => Response.json(example.response, { status: example.status })),
    );

    // Bắt lỗi khi gọi quá giới hạn trong ngày để giao diện hiện thông báo.
    const caught = await sendCallbackRequest({
      conversationId: 'c0a80000-0000-4000-8000-000000000001',
      phone: '0901234567',
      note: null,
    }).catch((error: unknown) => error);

    expect(caught).toBeInstanceOf(ApiError);
    expect(caught).toMatchObject({
      code: 'CALLBACK_LIMIT_REACHED',
      status: 429,
      message: 'Bạn đã gửi 3 yêu cầu gọi lại hôm nay. CSKH sẽ liên hệ sớm.',
    });
  });
});

describe('chấm điểm CSAT phía khách', () => {
  it('lỗi 409 "chấm điểm khi phiên chưa kết thúc" thành ApiError với mã SUPPORT_SESSION_OPEN', async () => {
    const example = findSupportDeskExample(
      CUSTOMER_API_FIXTURE,
      'chấm điểm khi phiên chưa kết thúc',
    );
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => Response.json(example.response, { status: example.status })),
    );

    // Bắt lỗi khi chấm điểm phiên chưa kết thúc để giữ lựa chọn của khách.
    const caught = await submitSupportCsat('c0a80000-0000-4000-8000-000000000001', 4).catch(
      (error: unknown) => error,
    );

    expect(caught).toBeInstanceOf(ApiError);
    expect(caught).toMatchObject({
      code: 'SUPPORT_SESSION_OPEN',
      status: 409,
      message: 'Phiên hỗ trợ chưa kết thúc nên chưa chấm điểm được.',
    });
  });
});
