// @vitest-environment jsdom
import { act } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createRoot, type Root } from 'react-dom/client';
import { SupportPage } from '../../../../../src/features/support-desk/customer/support-page';
import * as supportApi from '../../../../../src/features/support-desk/customer/support-customer-api';
import {
  findSupportDeskExample,
  selectSupportDeskEvents,
  type ContractExample,
} from '../contract-fixtures';
import { FakeEventSource } from '../shared/fake-event-source';

// Phần `data` của response mẫu là dữ liệu ngoài, nên kiểm hình dạng trước khi đưa vào schema.
function readExampleData(example: ContractExample): unknown {
  const { response } = example;
  if (response !== null && typeof response === 'object' && 'data' in response) {
    return response.data;
  }
  throw new Error(`Ví dụ "${example.name}" không có phần "data".`);
}

function conversationData(exampleName: string): unknown {
  return readExampleData(findSupportDeskExample('customer-api.json', exampleName));
}

let container: HTMLDivElement;
let root: Root;

// Trả `data` cho GET và POST. Test cần lỗi HTTP thì trả sẵn `Response`.
function stubFetch(respond: (url: string, init?: RequestInit) => unknown) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const result = await respond(String(input), init);
      if (result instanceof Response) return result;
      return new Response(JSON.stringify({ data: result ?? null }));
    }),
  );
}

async function renderPage() {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  await act(async () => {
    root.render(<SupportPage createEventSource={(url) => new FakeEventSource(url)} />);
  });
}

function activeSource(): FakeEventSource {
  const source = FakeEventSource.instances.at(-1);
  if (!source) throw new Error('Chưa có FakeEventSource nào được tạo.');
  return source;
}

async function waitForText(text: string) {
  for (let attempt = 0; attempt < 40; attempt++) {
    if (container.textContent?.includes(text)) return;
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 50));
    });
  }
  throw new Error(`Hết thời gian chờ chữ "${text}".`);
}

afterEach(async () => {
  FakeEventSource.instances.length = 0;
  if (root) await act(async () => root.unmount());
  container?.remove();
  vi.unstubAllGlobals();
});

describe('trang /support', () => {
  it('hội thoại mới hiện câu báo hội thoại được lưu 90 ngày', async () => {
    stubFetch((url) => {
      if (url === '/api/support/conversations') return conversationData('tạo hội thoại lần đầu');
      return { items: [], nextCursor: null };
    });

    await renderPage();

    await waitForText('Hội thoại này được lưu 90 ngày.');
  });

  it('hội thoại có phiên chờ hiện "Đang chờ nhân viên" và không hiện câu báo 90 ngày', async () => {
    stubFetch((url) => {
      if (url === '/api/support/conversations') {
        return conversationData('mở lại hội thoại đang có phiên chờ');
      }
      return { items: [], nextCursor: null };
    });

    await renderPage();

    await waitForText('Đang chờ nhân viên');
    expect(container.textContent).not.toContain('Hội thoại này được lưu 90 ngày.');
  });

  it('lịch sử tin hiện tên nhân viên, tin khách hiện nhãn "Bạn"', async () => {
    stubFetch((url) => {
      if (url === '/api/support/conversations') return conversationData('tạo hội thoại lần đầu');
      return readExampleData(
        findSupportDeskExample('customer-api.json', 'lịch sử tin, trang cuối'),
      );
    });

    await renderPage();

    await waitForText('Lan - CSKH');
    const senders = [...container.querySelectorAll('.support-message-sender')].map(
      (element) => element.textContent,
    );
    expect(senders).toContain('Bạn');
    expect(senders).toContain('Lan - CSKH');
  });

  it('tin nhân viên có `staffDisplayName` bằng `null` hiện "Nhân viên CSKH"', async () => {
    stubFetch((url) => {
      if (url === '/api/support/conversations') return conversationData('tạo hội thoại lần đầu');
      return { items: [], nextCursor: null };
    });

    await renderPage();
    await waitForText('Nội dung tin nhắn');

    const [staffEvent] = selectSupportDeskEvents('customer-events.json', ['message.created']);
    if (staffEvent === undefined) throw new Error('Thiếu sự kiện message.created trong file mẫu.');
    if (staffEvent.data === null || typeof staffEvent.data !== 'object') {
      throw new Error('Sự kiện message.created trong file mẫu không phải một object.');
    }
    const staffMessage = { ...staffEvent.data, staffDisplayName: null };

    await act(async () => {
      activeSource().emit('message.created', staffMessage);
    });

    await waitForText('Nhân viên CSKH');
    const staffSender = [...container.querySelectorAll('.support-message-sender')].find(
      (element) => element.textContent === 'Nhân viên CSKH',
    );
    expect(staffSender).not.toBeUndefined();
  });

  it('tin hệ thống có `action` hiện chỗ đặt form với `data-action` tương ứng', async () => {
    stubFetch((url) => {
      if (url === '/api/support/conversations') return conversationData('tạo hội thoại lần đầu');
      return { items: [], nextCursor: null };
    });

    await renderPage();
    await waitForText('Nội dung tin nhắn');

    const systemEvents = selectSupportDeskEvents('customer-events.json', ['message.created']);
    const withAction = systemEvents.filter(
      (event) =>
        event.data !== null &&
        typeof event.data === 'object' &&
        'action' in event.data &&
        event.data.action !== null,
    );

    for (const event of withAction) {
      await act(async () => {
        activeSource().emit('message.created', event.data);
      });
    }

    expect(container.querySelector('[data-action="callback_form"]')).not.toBeNull();
    expect(container.querySelector('[data-action="csat_form"]')).not.toBeNull();
  });

  it('tin hệ thống có `action` callback_form nhận đúng `conversationId` thật khi gửi', async () => {
    const sendSpy = vi.spyOn(supportApi, 'sendCallbackRequest').mockResolvedValueOnce({
      id: 'cb-page-1',
      status: 'pending',
      createdAt: '2026-11-03T14:05:00Z',
    });

    stubFetch((url) => {
      if (url === '/api/support/conversations') return conversationData('tạo hội thoại lần đầu');
      return { items: [], nextCursor: null };
    });

    await renderPage();
    await waitForText('Nội dung tin nhắn');

    // Phát sự kiện tin hệ thống chứa hành động gọi lại.
    await act(async () => {
      activeSource().emit('message.created', {
        id: 'msg-sys-1',
        conversationId: 'c0a80000-0000-4000-8000-000000000001',
        role: 'system',
        senderRole: 'system',
        staffDisplayName: null,
        body: 'Nhân viên đang bận. Bạn hãy để lại số điện thoại.',
        action: 'callback_form',
        createdAt: '2026-11-03T14:05:00Z',
      });
    });

    const phoneInput = container.querySelector<HTMLInputElement>('input[name="phone"]')!;
    expect(phoneInput).not.toBeNull();

    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!;
    await act(async () => {
      setter.call(phoneInput, '0901234567');
      phoneInput.dispatchEvent(new Event('input', { bubbles: true }));
    });

    const form = container.querySelector('form')!;
    await act(async () => {
      form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    });

    // Form phải nhận mã hội thoại từ trang chính và gửi lên API.
    expect(sendSpy).toHaveBeenCalledWith({
      conversationId: 'c0a80000-0000-4000-8000-000000000001',
      phone: '0901234567',
      note: null,
    });
  });

  it('tin hệ thống có `action` csat_form nhận đúng `conversationId` thật khi chấm điểm', async () => {
    const submitSpy = vi.spyOn(supportApi, 'submitSupportCsat').mockResolvedValueOnce({
      sessionId: 'sess-page-1',
      score: 5,
    });

    stubFetch((url) => {
      if (url === '/api/support/conversations') return conversationData('tạo hội thoại lần đầu');
      return { items: [], nextCursor: null };
    });

    await renderPage();
    await waitForText('Nội dung tin nhắn');

    // Phát sự kiện tin hệ thống chứa hành động chấm điểm CSAT.
    await act(async () => {
      activeSource().emit('message.created', {
        id: 'msg-sys-2',
        conversationId: 'c0a80000-0000-4000-8000-000000000001',
        role: 'system',
        senderRole: 'system',
        staffDisplayName: null,
        body: 'Phiên hỗ trợ đã kết thúc. Hãy đánh giá mức độ hài lòng.',
        action: 'csat_form',
        createdAt: '2026-11-03T14:10:00Z',
      });
    });

    const scoreButtons = container.querySelectorAll<HTMLButtonElement>('.csat-score-btn');
    expect(scoreButtons).toHaveLength(5);

    await act(async () => {
      scoreButtons[4]!.click();
    });

    const submitBtn = container.querySelector<HTMLButtonElement>('button[type="submit"]')!;
    await act(async () => {
      submitBtn.click();
    });

    // CSAT form phải gửi đúng mã hội thoại của trang lên API.
    expect(submitSpy).toHaveBeenCalledWith('c0a80000-0000-4000-8000-000000000001', 5);
  });

  it('sự kiện `session.assigned` hiện "Nhân viên đang hỗ trợ"', async () => {
    stubFetch((url) => {
      if (url === '/api/support/conversations') return conversationData('tạo hội thoại lần đầu');
      return { items: [], nextCursor: null };
    });

    await renderPage();
    await waitForText('Nội dung tin nhắn');

    await act(async () => {
      activeSource().emit('session.assigned', { sessionId: 's1' });
    });

    await waitForText('Nhân viên đang hỗ trợ');
  });

  it('hội thoại chưa có tin hiện trạng thái rỗng trong vùng tin', async () => {
    stubFetch((url) => {
      if (url === '/api/support/conversations') return conversationData('tạo hội thoại lần đầu');
      return { items: [], nextCursor: null };
    });

    await renderPage();

    await waitForText('Chào bạn, hãy mô tả vấn đề cần hỗ trợ. Kèm mã đơn hàng nếu có.');
  });

  it('hội thoại có phiên chờ hiện badge cảnh báo trong vùng trạng thái', async () => {
    stubFetch((url) => {
      if (url === '/api/support/conversations') {
        return conversationData('mở lại hội thoại đang có phiên chờ');
      }
      return { items: [], nextCursor: null };
    });

    await renderPage();

    await waitForText('Đang chờ nhân viên');
    const statusRegion = container.querySelector('[role="status"].support-chat-status');
    expect(statusRegion?.querySelector('.badge-warn')?.textContent).toBe('Đang chờ nhân viên');
    expect(container.textContent).not.toContain('Hội thoại này được lưu 90 ngày.');
  });

  it('mỗi tin có giờ gửi trong phần tử `time`', async () => {
    stubFetch((url) => {
      if (url === '/api/support/conversations') return conversationData('tạo hội thoại lần đầu');
      return readExampleData(
        findSupportDeskExample('customer-api.json', 'lịch sử tin, trang cuối'),
      );
    });

    await renderPage();

    await waitForText('Lan - CSKH');
    const times = [...container.querySelectorAll('time')];
    expect(times.length).toBeGreaterThan(0);
    // Giờ theo giờ Việt Nam, tin mẫu 02:15 UTC là 09:15 giờ Việt Nam.
    expect(times[0]?.dateTime).toBe('2026-11-03T02:15:05Z');
    expect(times[0]?.textContent).toBe('09:15');
  });

  it('mất kết nối hiện cảnh báo, luồng mở lại thì cảnh báo ẩn', async () => {
    stubFetch((url) => {
      if (url === '/api/support/conversations') return conversationData('tạo hội thoại lần đầu');
      return { items: [], nextCursor: null };
    });

    await renderPage();
    await waitForText('Hội thoại này được lưu 90 ngày.');

    vi.useFakeTimers();
    try {
      const source = activeSource();
      await act(async () => {
        source.open();
      });
      await act(async () => {
        source.failPermanently();
      });
      // Trình duyệt đóng hẳn nên luồng tự nối lại. Chờ qua nhịp báo mất kết nối 5 giây.
      await act(async () => {
        vi.advanceTimersByTime(5100);
      });
      expect(container.textContent).toContain('Mất kết nối. Đang thử kết nối lại...');

      await act(async () => {
        activeSource().open();
      });
      expect(container.textContent).not.toContain('Mất kết nối');
    } finally {
      vi.useRealTimers();
    }
  });

  it('đang tải giữ khung trang với chữ trạng thái trong vùng tin', async () => {
    let resolveFetch: (value: unknown) => void = () => {};
    stubFetch(
      () =>
        new Promise((resolve) => {
          resolveFetch = resolve;
        }),
    );

    await renderPage();

    const loading = container.querySelector('[role="status"].empty-state');
    expect(loading?.textContent).toBe('Đang tải hội thoại...');
    await act(async () => {
      resolveFetch(conversationData('tạo hội thoại lần đầu'));
    });
  });

  it('lỗi tải hội thoại hiện thông báo lỗi và nút "Thử lại"', async () => {
    stubFetch(() =>
      Response.json({ error: { code: 'INTERNAL', message: 'Máy chủ gặp lỗi.' } }, { status: 500 }),
    );

    await renderPage();

    await waitForText('Máy chủ gặp lỗi.');
    const retryButton = container.querySelector('button');
    expect(retryButton?.textContent).toBe('Thử lại');
  });
});
