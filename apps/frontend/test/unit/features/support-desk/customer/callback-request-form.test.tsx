// @vitest-environment jsdom
import { act } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createRoot, type Root } from 'react-dom/client';
import { ApiError } from '../../../../../src/shared/api';
import {
  CallbackRequestForm,
  normalizePhoneNumber,
} from '../../../../../src/features/support-desk/customer/callback-request-form';
import * as supportApi from '../../../../../src/features/support-desk/customer/support-customer-api';

let container: HTMLDivElement;
let root: Root;

async function renderForm(conversationId: string | null = 'conv-123') {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  await act(async () => {
    root.render(<CallbackRequestForm conversationId={conversationId} />);
  });
}

function phoneInput(): HTMLInputElement {
  const element = container.querySelector<HTMLInputElement>('input[name="phone"]');
  if (!element) throw new Error('Không tìm thấy ô nhập số điện thoại.');
  return element;
}

function noteTextarea(): HTMLTextAreaElement {
  const element = container.querySelector<HTMLTextAreaElement>('textarea[name="note"]');
  if (!element) throw new Error('Không tìm thấy ô nhập ghi chú.');
  return element;
}

function fieldErrorFor(input: HTMLElement): HTMLElement | null {
  const errorId = input.getAttribute('aria-describedby');
  if (!errorId) return null;
  return container.querySelector(`[id="${errorId}"]`);
}

function submitButton(): HTMLButtonElement {
  const element = container.querySelector<HTMLButtonElement>('button[type="submit"]');
  if (!element) throw new Error('Không tìm thấy nút gửi.');
  return element;
}

// Gán giá trị vào ô nhập và phát sự kiện input theo dõi bởi React.
async function typeIntoInput(element: HTMLInputElement | HTMLTextAreaElement, text: string) {
  const proto =
    element instanceof HTMLInputElement
      ? HTMLInputElement.prototype
      : HTMLTextAreaElement.prototype;
  const setter = Object.getOwnPropertyDescriptor(proto, 'value')!.set!;
  await act(async () => {
    setter.call(element, text);
    element.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

async function submitForm() {
  const form = container.querySelector('form');
  if (!form) throw new Error('Không tìm thấy thẻ form.');
  await act(async () => {
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  });
}

beforeEach(() => {
  vi.restoreAllMocks();
});

afterEach(async () => {
  if (root) await act(async () => root.unmount());
  container?.remove();
  vi.unstubAllGlobals();
});

describe('normalizePhoneNumber', () => {
  it('trả null khi số điện thoại là chuỗi trống hoặc khoảng trắng', () => {
    // Số trống không phải số điện thoại hợp lệ để chuẩn hóa gửi lên API.
    expect(normalizePhoneNumber('')).toBeNull();
    expect(normalizePhoneNumber('   ')).toBeNull();
  });
});

describe('CallbackRequestForm validation', () => {
  it('số trống thì hiện lỗi bắt buộc dưới ô bằng FieldError', async () => {
    const sendSpy = vi.spyOn(supportApi, 'sendCallbackRequest');
    await renderForm();

    await submitForm();

    // Ô số điện thoại phải gắn aria-invalid và trỏ tới id của FieldError.
    const input = phoneInput();
    expect(input.getAttribute('aria-invalid')).toBe('true');
    const errorId = input.getAttribute('aria-describedby');
    expect(errorId).not.toBeNull();

    const error = fieldErrorFor(input);
    expect(error).not.toBeNull();
    expect(error?.textContent).toBe('Số điện thoại không được để trống.');
    expect(sendSpy).not.toHaveBeenCalled();
  });

  it('từ chối đầu số sai và hiện câu không đúng định dạng', async () => {
    const sendSpy = vi.spyOn(supportApi, 'sendCallbackRequest');
    await renderForm();

    // Đầu số 01, 02, 04, 06 không phải số di động Việt Nam.
    await typeIntoInput(phoneInput(), '0123456789');
    await submitForm();

    const error = fieldErrorFor(phoneInput());
    expect(error?.textContent).toBe('Số điện thoại không đúng định dạng.');
    expect(sendSpy).not.toHaveBeenCalled();
  });

  it('từ chối chữ số ngoài ASCII như chữ số Ả Rập và fullwidth', async () => {
    const sendSpy = vi.spyOn(supportApi, 'sendCallbackRequest');
    await renderForm();

    // Hệ thống chỉ nhận chữ số ASCII để bảo đảm tích hợp viễn thông.
    await typeIntoInput(phoneInput(), '٠٩٠١٢٣٤٥٦٧');
    await submitForm();

    const error = fieldErrorFor(phoneInput());
    expect(error?.textContent).toBe('Số điện thoại không đúng định dạng.');
    expect(sendSpy).not.toHaveBeenCalled();
  });

  it('từ chối ký tự phân tách lạ ngoài khoảng trắng, chấm và gạch ngang', async () => {
    const sendSpy = vi.spyOn(supportApi, 'sendCallbackRequest');
    await renderForm();

    // Dấu ngoặc đơn hoặc dấu gạch chéo không thuộc bộ phân tách cho phép.
    await typeIntoInput(phoneInput(), '(090)1234567');
    await submitForm();

    const error = fieldErrorFor(phoneInput());
    expect(error?.textContent).toBe('Số điện thoại không đúng định dạng.');
    expect(sendSpy).not.toHaveBeenCalled();
  });

  it('từ chối số thừa chữ số hoặc thiếu chữ số', async () => {
    const sendSpy = vi.spyOn(supportApi, 'sendCallbackRequest');
    await renderForm();

    // Số di động hợp lệ có đúng 9 chữ số sau tiền tố 0 hoặc +84.
    await typeIntoInput(phoneInput(), '09012345678');
    await submitForm();

    expect(fieldErrorFor(phoneInput())?.textContent).toBe('Số điện thoại không đúng định dạng.');
    expect(sendSpy).not.toHaveBeenCalled();
  });

  it('ghi chú vượt quá 500 ký tự thì hiện lỗi dưới ô ghi chú', async () => {
    const sendSpy = vi.spyOn(supportApi, 'sendCallbackRequest');
    await renderForm();

    await typeIntoInput(phoneInput(), '0901234567');
    // Giới hạn ghi chú 500 ký tự theo hợp đồng API.
    await typeIntoInput(noteTextarea(), 'a'.repeat(501));
    await submitForm();

    const textarea = noteTextarea();
    expect(textarea.getAttribute('aria-invalid')).toBe('true');
    const noteErrorId = textarea.getAttribute('aria-describedby');
    expect(noteErrorId).not.toBeNull();

    const error = fieldErrorFor(textarea);
    expect(error?.textContent).toBe('Ghi chú tối đa 500 ký tự.');
    expect(sendSpy).not.toHaveBeenCalled();
  });

  it('từ chối số điện thoại chứa chữ số Ả Rập ở giữa sau tiền tố 0', async () => {
    const sendSpy = vi.spyOn(supportApi, 'sendCallbackRequest');
    await renderForm();

    // Chữ số Ả Rập ở giữa chuỗi phải bị từ chối bởi kiểm tra chữ số ASCII.
    await typeIntoInput(phoneInput(), '0901٢34567');
    await submitForm();

    const error = fieldErrorFor(phoneInput());
    expect(error?.textContent).toBe('Số điện thoại không đúng định dạng.');
    expect(sendSpy).not.toHaveBeenCalled();
  });

  it('render nhiều form trong cùng trang không bị trùng id', async () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    container = document.createElement('div');
    document.body.append(container);
    root = createRoot(container);
    await act(async () => {
      root.render(
        <div>
          <CallbackRequestForm conversationId="conv-1" />
          <CallbackRequestForm conversationId="conv-2" />
        </div>,
      );
    });

    const elementsWithId = Array.from(container.querySelectorAll('[id]'));
    const idList = elementsWithId.map((el) => el.id);
    expect(idList.length).toBeGreaterThanOrEqual(4);

    const uniqueIds = new Set(idList);
    // Mọi id trong container phải là duy nhất để tránh xung đột nhãn và lỗi.
    expect(uniqueIds.size).toBe(idList.length);
  });
});

describe('CallbackRequestForm trạng thái và gửi dữ liệu', () => {
  it('chuẩn hóa số điện thoại +84 và gửi đúng payload gọi lại', async () => {
    const sendSpy = vi.spyOn(supportApi, 'sendCallbackRequest').mockResolvedValueOnce({
      id: 'cb-001',
      status: 'pending',
      createdAt: '2026-11-03T14:05:00Z',
    });

    await renderForm('conv-test');
    // Khách nhập số có mã quốc gia và dấu chấm phân tách.
    await typeIntoInput(phoneInput(), '+84 90.123-4567');
    await typeIntoInput(noteTextarea(), 'Gọi sau 18 giờ giúp mình.');
    await submitForm();

    // Số điện thoại phải chuẩn hóa về dạng 0xxxxxxxxx trước khi gửi.
    expect(sendSpy).toHaveBeenCalledWith({
      conversationId: 'conv-test',
      phone: '0901234567',
      note: 'Gọi sau 18 giờ giúp mình.',
    });
  });

  it('ghi chú trống thì gửi null lên API', async () => {
    const sendSpy = vi.spyOn(supportApi, 'sendCallbackRequest').mockResolvedValueOnce({
      id: 'cb-002',
      status: 'pending',
      createdAt: '2026-11-03T14:05:00Z',
    });

    await renderForm('conv-test');
    await typeIntoInput(phoneInput(), '0901234567');
    await typeIntoInput(noteTextarea(), '   ');
    await submitForm();

    // Chuỗi trống gửi thành null theo hợp đồng API.
    expect(sendSpy).toHaveBeenCalledWith({
      conversationId: 'conv-test',
      phone: '0901234567',
      note: null,
    });
  });

  it('gửi thành công thì form đổi thành lời xác nhận', async () => {
    vi.spyOn(supportApi, 'sendCallbackRequest').mockResolvedValueOnce({
      id: 'cb-003',
      status: 'pending',
      createdAt: '2026-11-03T14:05:00Z',
    });

    await renderForm();
    await typeIntoInput(phoneInput(), '0901234567');
    await submitForm();

    // Form ẩn đi và thay bằng thông báo xác nhận thành công.
    expect(container.querySelector('form')).toBeNull();
    const confirmation = container.querySelector('.alert-ok');
    expect(confirmation).not.toBeNull();
    expect(confirmation?.textContent).toContain('Yêu cầu gọi lại đã được ghi nhận');
  });

  it('lỗi 429 CALLBACK_LIMIT_REACHED hiện thông báo và khóa không cho gửi lại', async () => {
    const sendSpy = vi
      .spyOn(supportApi, 'sendCallbackRequest')
      .mockRejectedValue(
        new ApiError(
          'CALLBACK_LIMIT_REACHED',
          429,
          'Bạn đã gửi 3 yêu cầu gọi lại hôm nay. CSKH sẽ liên hệ sớm.',
        ),
      );

    await renderForm();
    await typeIntoInput(phoneInput(), '0901234567');
    await submitForm();

    // Hiện thông báo lỗi qua ErrorAlert.
    const errorAlert = container.querySelector('.alert-list');
    expect(errorAlert?.textContent).toContain(
      'Bạn đã gửi 3 yêu cầu gọi lại hôm nay. CSKH sẽ liên hệ sớm.',
    );

    // Nút gửi bị khóa sau lỗi 429.
    const button = submitButton();
    expect(button.disabled).toBe(true);

    // Lần bấm tiếp theo không được tạo request mới.
    await submitForm();
    expect(sendSpy).toHaveBeenCalledTimes(1);
  });

  it('giữ giá trị đã nhập khi request gặp lỗi mạng', async () => {
    vi.spyOn(supportApi, 'sendCallbackRequest').mockRejectedValueOnce(
      new ApiError('NETWORK_ERROR', 0, 'Mất kết nối mạng. Hãy kiểm tra kết nối.'),
    );

    await renderForm();
    await typeIntoInput(phoneInput(), '0901234567');
    await typeIntoInput(noteTextarea(), 'Gọi vào buổi chiều.');
    await submitForm();

    // Form giữ nguyên dữ liệu đã nhập để khách không phải gõ lại.
    expect(phoneInput().value).toBe('0901234567');
    expect(noteTextarea().value).toBe('Gọi vào buổi chiều.');
    expect(submitButton().disabled).toBe(false);
  });

  it('chống request trùng khi nhấn gửi liên tiếp', async () => {
    // TypeScript của repo chưa có `Promise.withResolvers`, nên giữ `resolve` bằng biến ngoài.
    let resolveRequest: () => void = () => {};
    const pendingPromise = new Promise<{ id: string; status: 'pending'; createdAt: string }>(
      (resolve) => {
        resolveRequest = () =>
          resolve({ id: 'cb-004', status: 'pending', createdAt: '2026-11-03T14:05:00Z' });
      },
    );
    const sendSpy = vi.spyOn(supportApi, 'sendCallbackRequest').mockReturnValue(pendingPromise);

    await renderForm();
    await typeIntoInput(phoneInput(), '0901234567');

    // Kích hoạt submit hai lần trong lúc request đầu tiên đang chờ phản hồi.
    await submitForm();
    await submitForm();

    expect(sendSpy).toHaveBeenCalledTimes(1);

    await act(async () => {
      resolveRequest();
    });
  });
});
