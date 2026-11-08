// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, StrictMode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { StaffMessageComposer } from '../../../../../src/features/support-desk/admin/staff-message-composer';
import type { CannedResponseVariables } from '../../../../../src/features/support-desk/admin/render-canned-response';

// Biến mẫu cố định. SD-UI3 nối giá trị thật; hàm test chỉ cần giá trị truyền vào.
const VARIABLES: CannedResponseVariables = { tenKhach: 'Lan', maDon: null };

const CANNED = [
  {
    id: 'ca1',
    shortcut: 'camon',
    body: 'Cảm ơn {ten_khach} đã liên hệ Pickory.',
    shared: true,
    editable: false,
  },
  {
    id: 'ca2',
    shortcut: 'kiemdon',
    body: 'Mình kiểm đơn {ma_don} giúp bạn nhé.',
    shared: false,
    editable: true,
  },
  {
    id: 'ca3',
    shortcut: 'baohanh',
    body: 'Sản phẩm được bảo hành 12 tháng.',
    shared: false,
    editable: true,
  },
];

const MESSAGE_LIMIT_LABEL = (2000).toLocaleString('vi-VN');

let container: HTMLDivElement;
let root: Root;
let client: QueryClient;
let sendMock: (body: string) => Promise<boolean>;
let requestedUrls: string[];
const originalScrollIntoView = Element.prototype.scrollIntoView;

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  requestedUrls = [];
  sendMock = vi.fn(async () => true);
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      requestedUrls.push(url);
      if (url === '/api/admin/support/canned-responses') {
        return new Response(JSON.stringify({ data: CANNED }));
      }
      return new Response(JSON.stringify({ data: [] }));
    }),
  );
  container = document.createElement('div');
  document.body.append(container);
  client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  root = createRoot(container);
});

afterEach(async () => {
  Element.prototype.scrollIntoView = originalScrollIntoView;
  await act(async () => root.unmount());
  container.remove();
  client.clear();
  vi.unstubAllGlobals();
});

interface RenderOptions {
  isHeld?: boolean;
  canReply?: boolean;
  disabledNote?: string | null;
  isSending?: boolean;
}

// Dựng ô soạn một mình, tách khỏi màn CSKH, để test bàn phím và danh sách mẫu.
async function renderComposer(options: RenderOptions = {}) {
  const props = {
    isHeld: options.isHeld ?? true,
    canReply: options.canReply ?? true,
    disabledNote: options.disabledNote ?? null,
    isSending: options.isSending ?? false,
    variables: VARIABLES,
    onSend: sendMock,
  };
  await act(async () => {
    root.render(
      <StrictMode>
        <QueryClientProvider client={client}>
          <StaffMessageComposer {...props} />
        </QueryClientProvider>
      </StrictMode>,
    );
  });
}

function input(): HTMLTextAreaElement {
  const element = container.querySelector('[role="combobox"]');
  if (!element) throw new Error('Thiếu ô soạn combobox.');
  return element as HTMLTextAreaElement;
}

async function typeBody(value: string) {
  await act(async () => {
    const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!;
    setter.call(input(), value);
    input().dispatchEvent(new Event('input', { bubbles: true }));
  });
}

async function pressKey(key: string) {
  await act(async () => {
    input().dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
  });
}

function listbox(): HTMLElement | null {
  return container.querySelector('[role="listbox"]');
}

function options(): HTMLElement[] {
  return [...container.querySelectorAll<HTMLElement>('[role="option"]')];
}

// Mục option tại vị trí cho trước. Thiếu mục là lỗi của test dựng dữ liệu.
function optionAt(index: number): HTMLElement {
  const element = options()[index];
  if (!element) throw new Error('Thiếu mục mẫu tại vị trí ' + index + '.');
  return element;
}

describe('ô soạn tin nhân viên', () => {
  it('có label và id sinh bằng useId, không dùng id cố định', async () => {
    await renderComposer();

    const label = container.querySelector('label');
    expect(label?.textContent).toBe('Nội dung tin nhắn');
    const id = input().id;
    expect(id.length).toBeGreaterThan(0);
    expect(id).not.toBe('support-message-body');
    expect(label?.htmlFor).toBe(id);
  });

  it('Enter gửi tin, Shift+Enter không gửi', async () => {
    await renderComposer();
    await typeBody('Chào bạn.');
    await pressKey('Enter');

    expect(sendMock).toHaveBeenCalledWith('Chào bạn.');

    await typeBody('Dòng hai');
    await act(async () => {
      input().dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Enter', shiftKey: true, bubbles: true }),
      );
    });
    expect(sendMock).toHaveBeenCalledTimes(1);
  });

  it('gửi thành công mới xóa ô; gửi lỗi giữ nguyên chữ', async () => {
    await renderComposer();
    await typeBody('Tin sẽ gửi');
    await pressKey('Enter');
    await act(async () => {
      await Promise.resolve();
    });
    expect(input().value).toBe('');

    sendMock = vi.fn(async () => false);
    await renderComposerAgainWithFailingSend();
    await typeBody('Tin lỗi');
    await pressKey('Enter');
    await act(async () => {
      await Promise.resolve();
    });
    expect(input().value).toBe('Tin lỗi');
  });

  it('quá 2.000 ký tự sau khi bỏ khoảng trắng thì không gửi và hiện câu lỗi', async () => {
    await renderComposer();
    await typeBody(`${'a'.repeat(2001)} `);

    expect(sendMock).not.toHaveBeenCalled();
    expect(textOfContainer()).toContain(`Tin nhắn tối đa ${MESSAGE_LIMIT_LABEL} ký tự.`);
  });

  it('tắt kèm chú thích khi không giữ phiên', async () => {
    await renderComposer({ isHeld: false, disabledNote: 'Nhận hội thoại để trả lời.' });

    expect(input().disabled).toBe(true);
    expect(textOfContainer()).toContain('Nhận hội thoại để trả lời.');
    // Ô tắt thì không gọi route mẫu.
    expect(requestedUrls.some((url) => url.includes('canned-responses'))).toBe(false);
  });

  it('tắt kèm chú thích khi thiếu quyền trả lời', async () => {
    await renderComposer({ canReply: false, disabledNote: 'Bạn chỉ có quyền xem hội thoại.' });

    expect(input().disabled).toBe(true);
    expect(textOfContainer()).toContain('Bạn chỉ có quyền xem hội thoại.');
    expect(requestedUrls.some((url) => url.includes('canned-responses'))).toBe(false);
  });
});

describe('danh sách mẫu trả lời nhanh', () => {
  it('gõ / mở danh sách theo mẫu combobox', async () => {
    await renderComposer();
    await typeBody('/');

    expect(input().getAttribute('aria-expanded')).toBe('true');
    expect(listbox()).not.toBeNull();
    expect(options()).toHaveLength(3);
    // Mục đang chọn đầu tiên, mỗi mục là một option với aria-selected.
    expect(optionAt(0).getAttribute('aria-selected')).toBe('true');
    // Mẫu dùng chung có nhãn riêng.
    expect(textOfContainer()).toContain('Dùng chung');
  });

  it('từ sau / lọc mẫu theo shortcut không phân biệt hoa thường', async () => {
    await renderComposer();
    await typeBody('/KI');

    expect(options()).toHaveLength(1);
    expect(textOfContainer()).toContain('/kiemdon');
  });

  it('chỉ lọc mẫu có shortcut bắt đầu bằng từ sau dấu /', async () => {
    await renderComposer();
    // 'camon' chứa 'mon' ở giữa nhưng không bắt đầu bằng 'mon', nên không được chọn.
    await typeBody('/mon');

    expect(options()).toHaveLength(0);
    expect(textOfContainer()).toContain('Không có mẫu nào khớp.');
  });

  it('không có mẫu khớp thì hiện câu thông báo, không vỡ ô soạn', async () => {
    await renderComposer();
    await typeBody('/xyz');

    expect(options()).toHaveLength(0);
    expect(textOfContainer()).toContain('Không có mẫu nào khớp.');
    expect(input().getAttribute('aria-expanded')).toBe('true');
  });

  it('mẫu tải lỗi thì hiện câu thông báo riêng', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response('lỗi', { status: 500 })),
    );
    await renderComposer();
    await typeBody('/');

    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 50));
    });
    expect(textOfContainer()).toContain('Không tải được mẫu.');
  });

  it('mũi tên xuống quá cuối dừng ở cuối, mũi tên lên lùi về mục kế cuối (R1-11)', async () => {
    await renderComposer();
    await typeBody('/');

    // Bấm ArrowDown 4 lần (danh sách có 3 mục: 0, 1, 2) thì dừng ở mục 2.
    await pressKey('ArrowDown');
    await pressKey('ArrowDown');
    await pressKey('ArrowDown');
    await pressKey('ArrowDown');
    expect(optionAt(2).getAttribute('aria-selected')).toBe('true');

    // Bấm ArrowUp 1 lần thì phải lùi về mục 1 (kế cuối), chứng minh chỉ số không bị tràn quá 2.
    await pressKey('ArrowUp');
    expect(optionAt(1).getAttribute('aria-selected')).toBe('true');
    expect(input().getAttribute('aria-activedescendant')).toBe(optionAt(1).id);
  });

  it('Enter chèn mẫu đã thay biến từ danh sách đầy đủ 3 mục, đóng danh sách và không gửi tin (R1-11)', async () => {
    await renderComposer();
    // Gõ / có đủ 3 mục: 0=/camon, 1=/kiemdon, 2=/baohanh.
    await typeBody('/');
    await pressKey('ArrowDown');
    // Đang ở mục 1 (/kiemdon).
    expect(optionAt(1).getAttribute('aria-selected')).toBe('true');
    await pressKey('Enter');

    expect(input().value).toBe('Mình kiểm đơn {ma_don} giúp bạn nhé.');
    expect(listbox()).toBeNull();
    expect(sendMock).not.toHaveBeenCalled();
    // Con trỏ đặt ở cuối nội dung mẫu.
    expect(input().selectionStart).toBe(input().value.length);
  });

  it('Enter không gửi tin khi danh sách đang mở', async () => {
    await renderComposer();
    await typeBody('/kie');
    await pressKey('Enter');

    expect(sendMock).not.toHaveBeenCalled();
    expect(input().value).toBe('Mình kiểm đơn {ma_don} giúp bạn nhé.');
  });

  it('Esc đóng danh sách và gõ tiếp thì danh sách mở lại (R1-11)', async () => {
    await renderComposer();
    await typeBody('/');
    expect(listbox()).not.toBeNull();

    await pressKey('Escape');
    expect(listbox()).toBeNull();
    expect(input().getAttribute('aria-expanded')).toBe('false');

    // Gõ tiếp ký tự mới thì danh sách mở lại theo nội dung mới.
    await typeBody('/c');
    expect(listbox()).not.toBeNull();
    expect(input().getAttribute('aria-expanded')).toBe('true');
  });

  it('bấm chuột trái vào mục mẫu chèn mẫu đã thay biến (R1-07, R3-02)', async () => {
    await renderComposer();
    await typeBody('/');

    const firstOption = optionAt(0);
    const event = new MouseEvent('mousedown', {
      bubbles: true,
      cancelable: true,
      button: 0,
    });
    await act(async () => {
      firstOption.dispatchEvent(event);
    });

    expect(event.defaultPrevented).toBe(true);
    expect(input().value).toBe('Cảm ơn Lan đã liên hệ Pickory.');
    expect(listbox()).toBeNull();
  });

  it('bấm chuột phải vào mục mẫu không chèn mẫu nhưng vẫn chặn mất focus (R2-07, R3-02)', async () => {
    await renderComposer();
    await typeBody('/');

    const firstOption = optionAt(0);
    const event = new MouseEvent('mousedown', {
      bubbles: true,
      cancelable: true,
      button: 2,
    });
    await act(async () => {
      firstOption.dispatchEvent(event);
    });

    // Vẫn gọi preventDefault để ô soạn không mất focus, nhưng không chèn mẫu.
    expect(event.defaultPrevented).toBe(true);
    expect(listbox()).not.toBeNull();
    expect(input().value).toBe('/');
  });

  it('mục mẫu được cuộn vào tầm nhìn khi di chuyển bằng phím mũi tên (R2-07, R3-01)', async () => {
    const scrollMock = vi.fn();
    Element.prototype.scrollIntoView = scrollMock;

    await renderComposer();
    await typeBody('/');
    // Danh sách vừa mở đã cuộn mục 0; xóa lượt gọi ban đầu để kiểm tra đúng lượt do phím ArrowDown gây ra.
    scrollMock.mockClear();

    await pressKey('ArrowDown');
    expect(scrollMock).toHaveBeenCalledTimes(1);
    expect(scrollMock).toHaveBeenCalledWith({ block: 'nearest' });
    // Khẳng định hàm được gọi từ phần tử của mục thứ 2 (chỉ số 1).
    expect(scrollMock.mock.contexts[0]).toBe(optionAt(1));
  });

  it('ô soạn tắt thì không mở danh sách mẫu dù nội dung bắt đầu bằng / (R1-06)', async () => {
    await renderComposer({ isHeld: false });
    await typeBody('/camon');

    expect(listbox()).toBeNull();
    expect(input().getAttribute('aria-expanded')).toBe('false');
  });

  it('Enter khi danh sách mở nhưng không có mẫu khớp thì không gửi tin và không xóa nội dung (R1-11)', async () => {
    await renderComposer();
    await typeBody('/xyz');
    expect(listbox()).not.toBeNull();

    await pressKey('Enter');
    expect(sendMock).not.toHaveBeenCalled();
    expect(input().value).toBe('/xyz');
  });

  it('IME tiếng Việt: phím Enter khi isComposing hoặc keyCode 229 không gửi tin (R1-11)', async () => {
    await renderComposer();
    await typeBody('Chào bạn');

    // Giả lập phím Enter khi đang gõ IME (isComposing = true).
    await act(async () => {
      const event = new KeyboardEvent('keydown', {
        key: 'Enter',
        bubbles: true,
        cancelable: true,
      });
      Object.defineProperty(event, 'isComposing', { value: true });
      input().dispatchEvent(event);
    });
    // Trình duyệt gửi keyCode 229 trong quá trình composition.
    await act(async () => {
      const event = new KeyboardEvent('keydown', {
        key: 'Enter',
        keyCode: 229,
        bubbles: true,
        cancelable: true,
      });
      input().dispatchEvent(event);
    });

    expect(sendMock).not.toHaveBeenCalled();
  });

  it('danh sách đóng khi nội dung ô không còn là / cộng một từ', async () => {
    await renderComposer();
    await typeBody('/');
    expect(listbox()).not.toBeNull();

    await typeBody('/ca ');
    expect(listbox()).toBeNull();

    await typeBody('/ca');
    expect(listbox()).not.toBeNull();
  });

  it('biến null giữ nguyên dạng {ma_don} khi chèn mẫu', async () => {
    await renderComposer();
    await typeBody('/kiemdon');
    await pressKey('Enter');

    expect(input().value).toBe('Mình kiểm đơn {ma_don} giúp bạn nhé.');
  });
});

function textOfContainer(): string {
  return container.textContent ?? '';
}

// Gửi lỗi cần render lại với onSend trả false. Hàm nhỏ giữ test đọc được.
async function renderComposerAgainWithFailingSend() {
  await act(async () => {
    root.render(
      <StrictMode>
        <QueryClientProvider client={client}>
          <StaffMessageComposer
            isHeld
            canReply
            disabledNote={null}
            isSending={false}
            variables={VARIABLES}
            onSend={sendMock}
          />
        </QueryClientProvider>
      </StrictMode>,
    );
  });
}
