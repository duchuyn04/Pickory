// @vitest-environment jsdom
import { act } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createRoot, type Root } from 'react-dom/client';
import {
  SUPPORT_MESSAGE_MAX_LENGTH,
  SupportMessageComposer,
} from '../../../../../src/features/support-desk/customer/support-message-composer';

let container: HTMLDivElement;
let root: Root;

async function renderComposer(
  onSend: (body: string) => Promise<boolean>,
  isSending = false,
  isReady = true,
) {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  await act(async () => {
    root.render(<SupportMessageComposer isSending={isSending} isReady={isReady} onSend={onSend} />);
  });
}

function textarea(): HTMLTextAreaElement {
  const element = container.querySelector('textarea');
  if (!element) throw new Error('Không tìm thấy ô soạn tin.');
  return element;
}

function sendButton(): HTMLButtonElement {
  const element = container.querySelector('button');
  if (!element) throw new Error('Không tìm thấy nút gửi.');
  return element;
}

// React theo dõi giá trị ô qua setter gốc. Gán trực tiếp `.value` không kích hoạt `onChange`.
async function typeInto(element: HTMLTextAreaElement, text: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!;
  await act(async () => {
    setter.call(element, text);
    element.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

async function pressEnter(element: HTMLTextAreaElement, shiftKey = false) {
  await act(async () => {
    element.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', shiftKey, bubbles: true }));
  });
}

afterEach(async () => {
  if (root) await act(async () => root.unmount());
  container?.remove();
  vi.unstubAllGlobals();
});

describe('SupportMessageComposer', () => {
  it('có label "Nội dung tin nhắn" liên kết với ô soạn', async () => {
    await renderComposer(async () => true);

    const label = container.querySelector('label');
    expect(label?.textContent).toBe('Nội dung tin nhắn');
    expect(label?.htmlFor).toBe(textarea().id);
    expect(textarea().id).not.toBe('');
  });

  it('Enter gửi tin đã cắt khoảng trắng hai đầu và xóa ô soạn', async () => {
    const onSend = vi.fn(async () => true);
    await renderComposer(onSend);
    await typeInto(textarea(), '  Cảm ơn bạn.  ');

    await pressEnter(textarea());

    expect(onSend).toHaveBeenCalledWith('Cảm ơn bạn.');
    expect(textarea().value).toBe('');
  });

  it('Shift+Enter xuống dòng, không gửi tin', async () => {
    const onSend = vi.fn(async () => true);
    await renderComposer(onSend);
    await typeInto(textarea(), 'dòng một');

    await pressEnter(textarea(), true);

    expect(onSend).not.toHaveBeenCalled();
  });

  it('nút gửi tắt khi nội dung sau khi cắt khoảng trắng rỗng', async () => {
    await renderComposer(async () => true);
    expect(sendButton().disabled).toBe(true);

    await typeInto(textarea(), '   ');
    expect(sendButton().disabled).toBe(true);
  });

  it('placeholder gợi ý nội dung tin nhắn', async () => {
    await renderComposer(async () => true);

    expect(textarea().placeholder).toBe('Nhập tin nhắn...');
  });

  it('Enter trong lúc bộ gõ đang ghép chữ không gửi tin', async () => {
    const onSend = vi.fn(async () => true);
    await renderComposer(onSend);
    await typeInto(textarea(), 'xin chào');

    await act(async () => {
      const event = new KeyboardEvent('keydown', {
        key: 'Enter',
        bubbles: true,
      });
      // Bộ gõ tiếng Việt đánh dấu Enter chốt chữ bằng cờ composition.
      Object.defineProperty(event, 'isComposing', { value: true });
      textarea().dispatchEvent(event);
    });

    expect(onSend).not.toHaveBeenCalled();
  });

  it('đủ 2.000 ký tự sau khi cắt khoảng trắng thì gửi được', async () => {
    const onSend = vi.fn(async () => true);
    await renderComposer(onSend);
    // Hợp đồng tính độ dài sau khi bỏ khoảng trắng hai đầu. Dấu cách cuối không bị phạt.
    await typeInto(textarea(), `${'a'.repeat(SUPPORT_MESSAGE_MAX_LENGTH)}\n`);

    expect(sendButton().disabled).toBe(false);
    await pressEnter(textarea());
    expect(onSend).toHaveBeenCalledWith('a'.repeat(SUPPORT_MESSAGE_MAX_LENGTH));
  });

  it('hơn 2.000 ký tự sau khi cắt khoảng trắng thì báo lỗi', async () => {
    const onSend = vi.fn(async () => true);
    await renderComposer(onSend);
    await typeInto(textarea(), `  ${'a'.repeat(SUPPORT_MESSAGE_MAX_LENGTH + 1)}  `);

    expect(container.textContent).toContain('Tin nhắn tối đa 2.000 ký tự.');
    expect(textarea().getAttribute('aria-invalid')).toBe('true');
    expect(sendButton().disabled).toBe(true);

    await pressEnter(textarea());
    expect(onSend).not.toHaveBeenCalled();
  });

  it('nội dung dài hơn 1.800 ký tự hiện bộ đếm dưới thanh soạn', async () => {
    await renderComposer(async () => true);
    await typeInto(textarea(), 'a'.repeat(1801));

    const counter = container.querySelector('.support-composer-counter');
    expect(counter?.textContent).toBe('1.801/2.000');
  });

  it('hội thoại chưa tải xong thì ô soạn tắt và giữ khung bố cục', async () => {
    await renderComposer(async () => true, false, false);

    expect(textarea().disabled).toBe(true);
    expect(sendButton().disabled).toBe(true);
  });

  it('đang gửi thì nút ghi "Đang gửi..." và tắt', async () => {
    await renderComposer(async () => true, true);

    expect(sendButton().textContent).toContain('Đang gửi...');
    expect(sendButton().disabled).toBe(true);
  });

  it('gửi lỗi giữ nguyên nội dung ô soạn', async () => {
    const onSend = vi.fn(async () => false);
    await renderComposer(onSend);
    await typeInto(textarea(), 'Alo?');

    await pressEnter(textarea());

    expect(onSend).toHaveBeenCalledWith('Alo?');
    expect(textarea().value).toBe('Alo?');
  });

  it('khách gõ tin mới trong lúc gửi thì giữ bản mới, không xóa mất chữ', async () => {
    let resolveSend: (sent: boolean) => void = () => {};
    // Cấu hình `lib` của repo chưa có `Promise.withResolvers`, nên giữ `resolve` ra ngoài bằng biến.
    const onSend = vi.fn(
      () =>
        new Promise<boolean>((resolve) => {
          resolveSend = resolve;
        }),
    );
    await renderComposer(onSend);
    await typeInto(textarea(), 'Tin thứ nhất');

    await pressEnter(textarea());
    // Đang chờ máy chủ. Khách gõ tiếp tin khác.
    await typeInto(textarea(), 'Tin thứ hai');
    await act(async () => {
      resolveSend(true);
      await new Promise((resolve) => setTimeout(resolve, 0));
    });

    expect(textarea().value).toBe('Tin thứ hai');
  });

  it('gửi xong trả focus về ô soạn', async () => {
    const onSend = vi.fn(async () => true);
    await renderComposer(onSend);
    await typeInto(textarea(), 'Tin xong thì focus lại ô soạn');

    await act(async () => {
      sendButton().click();
      await new Promise((resolve) => setTimeout(resolve, 0));
    });

    expect(onSend).toHaveBeenCalled();
    expect(document.activeElement).toBe(textarea());
  });

  it('bật lại sau lần tải lại thì trả focus về ô soạn', async () => {
    const onSend = vi.fn(async () => true);
    await renderComposer(onSend, false, true);
    await act(async () => {
      textarea().focus();
    });

    // Vào trạng thái tải lại: ô soạn bị tắt và mất focus.
    await act(async () => {
      root.render(<SupportMessageComposer isSending={false} isReady={false} onSend={onSend} />);
    });
    // Trình duyệt tự đưa focus về body khi phần tử đang chọn bị disabled. jsdom bỏ qua blur khi disabled,
    // nên tạm bật lại để chuyển focus về body như trình duyệt thật.
    await act(async () => {
      textarea().disabled = false;
      textarea().blur();
      textarea().disabled = true;
    });
    expect(document.activeElement).toBe(document.body);

    await act(async () => {
      root.render(<SupportMessageComposer isSending={false} isReady={true} onSend={onSend} />);
    });

    expect(document.activeElement).toBe(textarea());
  });

  it('nút "Gửi" là button thật, focus được và đứng ngay sau ô soạn trong thứ tự Tab', async () => {
    await renderComposer(async () => true);
    await typeInto(textarea(), 'Xin chào');

    const button = sendButton();
    expect(button.tagName).toBe('BUTTON');
    expect(button.disabled).toBe(false);
    // Trình duyệt thật kích hoạt nút bằng Enter khi đang focus. Luồng bàn phím đầu cuối kiểm ở E2E.
    await act(async () => {
      button.focus();
    });
    expect(document.activeElement).toBe(button);

    const tabbable = [...container.querySelectorAll('textarea, button')];
    expect(tabbable.indexOf(textarea())).toBeLessThan(tabbable.indexOf(button));
  });

  it('đang gửi thì nút tắt', async () => {
    await renderComposer(async () => true, true);
    await typeInto(textarea(), 'Xin chào');

    expect(sendButton().disabled).toBe(true);
  });
});
