// @vitest-environment jsdom
import { act, StrictMode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createRoot, type Root } from 'react-dom/client';
import {
  useSupportConversation,
  type SupportConversationController,
} from '../../../../../src/features/support-desk/customer/use-support-conversation';
import { findSupportDeskExample } from '../contract-fixtures';
import { FakeEventSource } from '../shared/fake-event-source';

const CONVERSATION_ID = 'c0a80000-0000-4000-8000-000000000001';

// Dữ liệu mẫu đọc từ file hợp đồng, để test không tự viết dữ liệu lệch với backend.
function conversationData(exampleName: string): unknown {
  const example = findSupportDeskExample('customer-api.json', exampleName);
  const { response } = example;
  if (response !== null && typeof response === 'object' && 'data' in response) {
    return response.data;
  }
  throw new Error(`Ví dụ "${exampleName}" không có phần "data".`);
}

function messageData(): Record<string, unknown> {
  const data = conversationData('khách gửi tin');
  if (data === null || typeof data !== 'object') {
    throw new Error('Ví dụ "khách gửi tin" không phải một object.');
  }
  return data as Record<string, unknown>;
}

let container: HTMLDivElement;
let root: Root;
let controller: SupportConversationController | null = null;

function Probe() {
  // Test thay `createEventSource` bằng `FakeEventSource` để quyết định thời điểm luồng mở hay nhận sự kiện.
  controller = useSupportConversation({
    createEventSource: (url) => new FakeEventSource(url),
  });
  return null;
}

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

function activeSource(): FakeEventSource {
  const source = FakeEventSource.instances.at(-1);
  if (!source) throw new Error('Chưa có FakeEventSource nào được tạo.');
  return source;
}

// Chờ tối đa 2 giây cho tới khi điều kiện đúng. Hook nạp dữ liệu qua nhiều lượt bất đồng bộ.
async function waitFor(condition: () => boolean, description: string) {
  for (let attempt = 0; attempt < 40; attempt++) {
    if (condition()) return;
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 50));
    });
  }
  throw new Error(`Hết thời gian chờ: ${description}.`);
}

async function renderProbe() {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  await act(async () => {
    root.render(<Probe />);
  });
}

afterEach(async () => {
  controller = null;
  FakeEventSource.instances.length = 0;
  if (root) await act(async () => root.unmount());
  container?.remove();
  vi.unstubAllGlobals();
});

describe('useSupportConversation', () => {
  it('StrictMode chạy effect hai lần nhưng chỉ gửi một POST và chỉ mở một luồng', async () => {
    let postCount = 0;
    stubFetch((url) => {
      if (url === '/api/support/conversations') {
        postCount += 1;
        return conversationData('tạo hội thoại lần đầu');
      }
      return { items: [], nextCursor: null };
    });

    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    container = document.createElement('div');
    document.body.append(container);
    root = createRoot(container);
    // StrictMode mô phỏng dev: mount, chạy effect, cleanup, chạy lại effect trên cùng instance.
    await act(async () => {
      root.render(
        <StrictMode>
          <Probe />
        </StrictMode>,
      );
    });

    await waitFor(() => controller?.conversation !== null, 'hội thoại được tải');
    await waitFor(() => postCount === 1, 'chỉ một POST tạo hội thoại');
    expect(FakeEventSource.instances).toHaveLength(1);
  });

  it('tải hội thoại và lịch sử, rồi mở luồng sự kiện của đúng hội thoại', async () => {
    const calls: string[] = [];
    stubFetch((url, init) => {
      calls.push(`${init?.method ?? 'GET'} ${url}`);
      if (url === '/api/support/conversations') return conversationData('tạo hội thoại lần đầu');
      if (url.includes('/messages')) return { items: [], nextCursor: null };
      return null;
    });

    await renderProbe();

    await waitFor(() => controller?.conversation !== null, 'hội thoại được tải');

    expect(controller?.conversation?.showRetentionNotice).toBe(true);
    expect(controller?.messages).toEqual([]);
    expect(FakeEventSource.instances).toHaveLength(1);
    expect(activeSource().url).toBe(`/api/support/conversations/${CONVERSATION_ID}/events`);
  });

  it('mỗi lần luồng mở đọc lại hội thoại và lịch sử', async () => {
    let postCount = 0;
    stubFetch((url) => {
      if (url === '/api/support/conversations') {
        postCount += 1;
        return conversationData('tạo hội thoại lần đầu');
      }
      return { items: [], nextCursor: null };
    });

    await renderProbe();
    await waitFor(() => controller?.conversation !== null, 'hội thoại được tải');
    expect(postCount).toBe(1);

    await act(async () => {
      activeSource().open();
    });

    await waitFor(() => postCount === 2, 'luồng mở đọc lại hội thoại');
  });

  it('sự kiện `message.created` thêm tin, tin có `id` đã hiện không bị nhân đôi', async () => {
    stubFetch((url) => {
      if (url === '/api/support/conversations') return conversationData('tạo hội thoại lần đầu');
      return { items: [], nextCursor: null };
    });

    await renderProbe();
    await waitFor(() => controller?.conversation !== null, 'hội thoại được tải');

    await act(async () => {
      activeSource().emit('message.created', messageData());
    });
    expect(controller?.messages).toHaveLength(1);

    // Sự kiện lặp lại sau khi nối lại luồng. Gộp theo `id` giữ một tin duy nhất.
    await act(async () => {
      activeSource().emit('message.created', messageData());
    });
    expect(controller?.messages).toHaveLength(1);
  });

  it('luồng mở lại đọc lại lịch sử và không sinh tin trùng', async () => {
    let historyItems: unknown[] = [];
    stubFetch((url) => {
      if (url === '/api/support/conversations') return conversationData('tạo hội thoại lần đầu');
      return { items: historyItems, nextCursor: null };
    });

    await renderProbe();
    await waitFor(() => controller?.conversation !== null, 'hội thoại được tải');

    // Tin đến qua sự kiện trước khi luồng đứt. Máy chủ trả lại cùng tin trong lịch sử sau khi nối lại.
    await act(async () => {
      activeSource().emit('message.created', messageData());
    });
    expect(controller?.messages).toHaveLength(1);
    historyItems = [messageData()];

    await act(async () => {
      activeSource().dropAndReconnectByBrowser();
    });
    await act(async () => {
      activeSource().open();
    });

    await waitFor(
      () =>
        controller?.messages.length === 1 && controller.messages[0]?.body === messageData().body,
      'lịch sử đọc lại mà không nhân đôi tin',
    );
  });

  it('sự kiện `session.waiting` hiện trạng thái chờ, `session.closed` hiện phiên đã đóng', async () => {
    stubFetch((url) => {
      if (url === '/api/support/conversations') return conversationData('tạo hội thoại lần đầu');
      return { items: [], nextCursor: null };
    });

    await renderProbe();
    await waitFor(() => controller?.conversation !== null, 'hội thoại được tải');
    expect(controller?.sessionStatus).toBeNull();

    await act(async () => {
      activeSource().emit('session.waiting', { sessionId: 's1' });
    });
    expect(controller?.sessionStatus).toBe('waiting');

    await act(async () => {
      activeSource().emit('session.closed', { sessionId: 's1' });
    });
    expect(controller?.sessionStatus).toBe('closed');
  });

  it('sự kiện có tên lạ và dữ liệu hỏng bị bỏ qua', async () => {
    stubFetch((url) => {
      if (url === '/api/support/conversations') return conversationData('tạo hội thoại lần đầu');
      return { items: [], nextCursor: null };
    });

    await renderProbe();
    await waitFor(() => controller?.conversation !== null, 'hội thoại được tải');

    await act(async () => {
      activeSource().emit('heartbeat', { at: '2026-11-03T00:00:00Z' });
      activeSource().emit('message.created', { wrong: true });
      activeSource().emitRaw('message.created', 'not-json');
    });

    expect(controller?.messages).toEqual([]);
  });

  it('gửi tin thành công thêm tin vào danh sách và trả `true`', async () => {
    stubFetch((url, init) => {
      if (url === '/api/support/conversations') return conversationData('tạo hội thoại lần đầu');
      if (url.includes('/messages') && init?.method === 'POST') return messageData();
      return { items: [], nextCursor: null };
    });

    await renderProbe();
    await waitFor(() => controller?.conversation !== null, 'hội thoại được tải');

    let sent = false;
    await act(async () => {
      sent = await controller!.send('Cảm ơn bạn.');
    });

    expect(sent).toBe(true);
    expect(controller?.messages).toHaveLength(1);
    expect(controller?.sendError).toBeNull();
  });

  it('gửi tin lỗi 429 trả `false` và giữ câu lỗi của API', async () => {
    const example = findSupportDeskExample('customer-api.json', 'khách gửi quá giới hạn');
    stubFetch((url, init) => {
      if (url === '/api/support/conversations') return conversationData('tạo hội thoại lần đầu');
      if (url.includes('/messages') && init?.method === 'POST') {
        return Response.json(example.response, { status: example.status });
      }
      return { items: [], nextCursor: null };
    });

    await renderProbe();
    await waitFor(() => controller?.conversation !== null, 'hội thoại được tải');

    let sent = true;
    await act(async () => {
      sent = await controller!.send('Alo?');
    });

    expect(sent).toBe(false);
    expect(controller?.sendError).toBe('Bạn gửi tin quá nhanh. Hãy chờ một chút rồi gửi lại.');
    expect(controller?.messages).toEqual([]);
  });

  it('lỗi tải hội thoại hiện câu lỗi và nút thử lại tải lại', async () => {
    let fail = true;
    stubFetch((url) => {
      if (fail) {
        return Response.json(
          { error: { code: 'INTERNAL', message: 'Máy chủ gặp lỗi.' } },
          { status: 500 },
        );
      }
      if (url === '/api/support/conversations') return conversationData('tạo hội thoại lần đầu');
      return { items: [], nextCursor: null };
    });

    await renderProbe();
    await waitFor(() => controller?.loadError !== null, 'lỗi tải hội thoại hiện ra');
    expect(controller?.isLoading).toBe(false);

    fail = false;
    await act(async () => {
      controller!.retryLoad();
    });
    await waitFor(() => controller?.conversation !== null, 'thử lại tải được hội thoại');
  });

  it('giữ trạng thái chờ khi luồng mở lại mà hội thoại vẫn thiếu `session`', async () => {
    stubFetch((url) => {
      if (url === '/api/support/conversations') return conversationData('tạo hội thoại lần đầu');
      return { items: [], nextCursor: null };
    });

    await renderProbe();
    await waitFor(() => controller?.conversation !== null, 'hội thoại được tải');

    await act(async () => {
      activeSource().emit('session.waiting', { sessionId: 's1' });
    });
    expect(controller?.sessionStatus).toBe('waiting');

    await act(async () => {
      activeSource().dropAndReconnectByBrowser();
    });
    await act(async () => {
      activeSource().open();
    });
    await new Promise((resolve) => setTimeout(resolve, 150));

    expect(controller?.sessionStatus).toBe('waiting');
  });

  it('luồng mở lại đọc `session.assigned` thì đổi trạng thái sang đã nhận', async () => {
    let postCount = 0;
    stubFetch((url) => {
      if (url === '/api/support/conversations') {
        postCount += 1;
        // Lần đầu chưa có phiên. Lần đọc lại phiên đã nhận, như máy chủ trả sau khi nhân viên nhận.
        if (postCount === 1) return conversationData('tạo hội thoại lần đầu');
        const data = conversationData('mở lại hội thoại đang có phiên chờ');
        if (data === null || typeof data !== 'object') return data;
        return { ...data, session: { id: 's1', status: 'assigned', csatScore: null } };
      }
      return { items: [], nextCursor: null };
    });

    await renderProbe();
    await waitFor(() => controller?.conversation !== null, 'hội thoại được tải');
    expect(controller?.sessionStatus).toBeNull();

    await act(async () => {
      activeSource().emit('session.waiting', { sessionId: 's1' });
    });
    expect(controller?.sessionStatus).toBe('waiting');

    await act(async () => {
      activeSource().open();
    });
    await waitFor(() => controller?.sessionStatus === 'assigned', 'trạng thái đổi sang assigned');
  });

  it('luồng mở lại đọc lại lịch sử và hiện tin phát lúc mất kết nối', async () => {
    const tinA = messageData();
    const tinB = { ...messageData(), id: 'b0000000-0000-4000-8000-0000000000b1', body: 'Tin mới' };
    let historyItems: unknown[] = [tinA];
    stubFetch((url) => {
      if (url === '/api/support/conversations') return conversationData('tạo hội thoại lần đầu');
      return { items: historyItems, nextCursor: null };
    });

    await renderProbe();
    await waitFor(() => controller?.messages.length === 1, 'lịch sử đầu có một tin');

    // Tin B phát lúc luồng đứt nên không thành sự kiện. Chỉ lần đọc lại lịch sử mới mang nó về.
    historyItems = [tinA, tinB];
    await act(async () => {
      activeSource().dropAndReconnectByBrowser();
    });
    await act(async () => {
      activeSource().open();
    });

    await waitFor(
      () => controller?.messages.length === 2 && controller.messages[1]?.body === 'Tin mới',
      'tin phát lúc mất kết nối hiện sau khi luồng mở lại',
    );
  });

  it('lần đọc lại trả hội thoại khác mã thì boot lại và dừng luồng cũ', async () => {
    let postCount = 0;
    stubFetch((url) => {
      if (url === '/api/support/conversations') {
        postCount += 1;
        const data = conversationData('tạo hội thoại lần đầu');
        if (postCount === 1 || data === null || typeof data !== 'object') return data;
        // Hội thoại cũ đã đóng nên máy chủ trả hội thoại mới ở lần đọc lại.
        return { ...data, id: 'c0a80000-0000-4000-8000-000000000099' };
      }
      return { items: [], nextCursor: null };
    });

    await renderProbe();
    await waitFor(() => controller?.conversation !== null, 'hội thoại đầu được tải');
    const firstSource = activeSource();

    await act(async () => {
      activeSource().open();
    });

    await waitFor(() => FakeEventSource.instances.length === 2, 'boot lại tạo luồng mới');
    expect(activeSource().url).toBe(
      '/api/support/conversations/c0a80000-0000-4000-8000-000000000099/events',
    );
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(firstSource.closeCalls).toBeGreaterThan(0);
    expect(controller?.conversation?.id).toBe('c0a80000-0000-4000-8000-000000000099');
    // Tin của hội thoại cũ không được mang sang khung chat của hội thoại mới.
    expect(controller?.messages).toEqual([]);
  });

  it('gửi tin nhận 409 thì boot lại với hội thoại mới, dừng luồng cũ và xóa tin cũ', async () => {
    let postCount = 0;
    const oldMessage = messageData();
    stubFetch((url, init) => {
      if (url === '/api/support/conversations') {
        postCount += 1;
        const data = conversationData('tạo hội thoại lần đầu');
        if (postCount === 1 || data === null || typeof data !== 'object') return data;
        // Hội thoại cũ đã đóng nên lần boot lại nhận hội thoại mới của cùng khách.
        return { ...data, id: 'c0a80000-0000-4000-8000-0000000000aa' };
      }
      if (url.includes('/messages') && init?.method === 'POST') {
        return Response.json(
          { error: { code: 'SUPPORT_CONVERSATION_CLOSED', message: 'Hội thoại đã đóng.' } },
          { status: 409 },
        );
      }
      // Lịch sử của hội thoại cũ có một tin, để thấy hook xóa dữ liệu cũ khi boot lại.
      if (postCount === 1) return { items: [oldMessage], nextCursor: null };
      return { items: [], nextCursor: null };
    });

    await renderProbe();
    await waitFor(() => controller?.messages.length === 1, 'lịch sử cũ có một tin');
    const firstSource = activeSource();

    let sent = true;
    await act(async () => {
      sent = await controller!.send('Alo?');
    });
    expect(sent).toBe(false);
    expect(controller?.sendError).toBe('Hội thoại đã đóng.');

    await waitFor(() => FakeEventSource.instances.length === 2, 'boot lại tạo luồng mới');
    await waitFor(
      () => controller?.conversation?.id === 'c0a80000-0000-4000-8000-0000000000aa',
      'hội thoại mới được tải',
    );
    expect(activeSource().url).toBe(
      '/api/support/conversations/c0a80000-0000-4000-8000-0000000000aa/events',
    );
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(firstSource.closeCalls).toBeGreaterThan(0);
    // Tin của hội thoại đã đóng không còn trong khung chat mới.
    expect(controller?.messages).toEqual([]);
  });

  it('lần tải đầu nhận 404 chủ thì đọc lại POST và GET một lần và hiện hội thoại', async () => {
    let getCount = 0;
    stubFetch((url, init) => {
      if (url === '/api/support/conversations') return conversationData('tạo hội thoại lần đầu');
      if (url.includes('/messages') && (init?.method ?? 'GET') === 'GET') {
        getCount += 1;
        // Lần đầu thiếu chủ do cookie đầu trang chưa ổn định. Lần đọc lại thành công.
        if (getCount === 1) {
          return Response.json(
            {
              error: {
                code: 'SUPPORT_CONVERSATION_NOT_FOUND',
                message: 'Không tìm thấy hội thoại.',
              },
            },
            { status: 404 },
          );
        }
        return { items: [], nextCursor: null };
      }
      return null;
    });

    await renderProbe();

    await waitFor(() => controller?.conversation !== null, 'hội thoại hiện sau lần đọc lại');
    expect(controller?.loadError).toBeNull();
    expect(getCount).toBe(2);
  });

  it('hai lần đọc đều 404 thì trang hiện màn lỗi', async () => {
    stubFetch((url) => {
      if (url === '/api/support/conversations') return conversationData('tạo hội thoại lần đầu');
      return Response.json(
        { error: { code: 'SUPPORT_CONVERSATION_NOT_FOUND', message: 'Không tìm thấy hội thoại.' } },
        { status: 404 },
      );
    });

    await renderProbe();

    await waitFor(() => controller?.loadError !== null, 'màn lỗi hiện sau hai lần 404');
    expect(controller?.loadError).toBe('Không tìm thấy hội thoại.');
  });

  it('sự kiện assigned đến trong lúc POST đọc lại thì không bị dữ liệu waiting ghi đè', async () => {
    let postCount = 0;
    let resolveReloadPost: (value: unknown) => void = () => {};
    stubFetch((url) => {
      if (url === '/api/support/conversations') {
        postCount += 1;
        if (postCount === 1) return conversationData('tạo hội thoại lần đầu');
        // Lần đọc lại giữ POST đang chờ, để sự kiện chen vào giữa lúc chờ.
        return new Promise((resolve) => {
          resolveReloadPost = resolve;
        });
      }
      return { items: [], nextCursor: null };
    });

    await renderProbe();
    await waitFor(() => controller?.conversation !== null, 'hội thoại được tải');

    await act(async () => {
      activeSource().open();
    });
    await act(async () => {
      activeSource().emit('session.assigned', { sessionId: 's1' });
    });
    expect(controller?.sessionStatus).toBe('assigned');

    // POST trả phiên `waiting` đã cũ, tính trước lúc nhân viên nhận phiên.
    await act(async () => {
      resolveReloadPost(conversationData('mở lại hội thoại đang có phiên chờ'));
      await new Promise((resolve) => setTimeout(resolve, 20));
    });

    expect(controller?.sessionStatus).toBe('assigned');
  });

  it('lần đọc lại cũ không ghi dữ liệu sau khi boot lại do 409', async () => {
    let postCount = 0;
    let getCount = 0;
    let resolveStaleHistory: (value: unknown) => void = () => {};
    const staleMessage = messageData();
    stubFetch((url, init) => {
      if (url === '/api/support/conversations') {
        postCount += 1;
        const data = conversationData('tạo hội thoại lần đầu');
        if (postCount < 3 || data === null || typeof data !== 'object') return data;
        // Lần boot lại sau 409 nhận hội thoại mới.
        return { ...data, id: 'c0a80000-0000-4000-8000-0000000000bb' };
      }
      if (url.includes('/messages') && init?.method === 'POST') {
        return Response.json(
          { error: { code: 'SUPPORT_CONVERSATION_CLOSED', message: 'Hội thoại đã đóng.' } },
          { status: 409 },
        );
      }
      getCount += 1;
      // Lần đọc lại của hội thoại cũ giữ GET đang chờ, để boot lại chen vào giữa.
      if (getCount === 2) {
        return new Promise((resolve) => {
          resolveStaleHistory = resolve;
        });
      }
      return { items: [], nextCursor: null };
    });

    await renderProbe();
    await waitFor(() => controller?.conversation !== null, 'hội thoại được tải');

    await act(async () => {
      activeSource().open();
    });
    let sent = true;
    await act(async () => {
      sent = await controller!.send('Alo?');
    });
    expect(sent).toBe(false);

    await waitFor(
      () => controller?.conversation?.id === 'c0a80000-0000-4000-8000-0000000000bb',
      'boot lại xong với hội thoại mới',
    );

    // Lịch sử của hội thoại cũ trả về muộn. Hook không được trộn tin này vào khung chat mới.
    await act(async () => {
      resolveStaleHistory({ items: [staleMessage], nextCursor: null });
      await new Promise((resolve) => setTimeout(resolve, 20));
    });

    expect(controller?.messages).toEqual([]);
  });

  it('unmount trang thì đóng luồng sự kiện', async () => {
    stubFetch((url) => {
      if (url === '/api/support/conversations') return conversationData('tạo hội thoại lần đầu');
      return { items: [], nextCursor: null };
    });

    await renderProbe();
    await waitFor(() => controller?.conversation !== null, 'hội thoại được tải');
    const source = activeSource();

    await act(async () => {
      await root.unmount();
    });
    // Việc dừng luồng nằm sau một lượt hẹn giờ của cleanup, nên nhường event loop một nhịp.
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 20));
    });

    expect(source.closeCalls).toBeGreaterThan(0);
  });
});
