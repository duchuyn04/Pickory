// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CommerceSettingsPage } from '../../../../src/features/commerce/admin/commerce-settings-page';
import type { CommerceSettings } from '../../../../src/features/commerce/commerce-settings-api';
import type { ActorData } from '../../../../src/features/identity/hooks/use-actor';

const SETTINGS_PATH = '/api/admin/commerce/settings';

let container: HTMLDivElement;
let root: Root;
let requests: { method: string; url: string; body: unknown }[];

const managerActor: ActorData = {
  anonymousId: 'anon',
  userId: 'user-1',
  kind: 'staff',
  roles: ['sales'],
  permissions: ['commerce-settings:manage'],
  consent: { personalization: false, marketing: false },
};

const readOnlyActor: ActorData = { ...managerActor, permissions: ['products:read'] };

const defaultSettings: CommerceSettings = {
  freeShippingThreshold: 500000,
  codMaxAmount: 20000000,
  maxQuantityPerSku: 5,
  maxCartLines: 20,
  sepayAccountNumber: null,
  sepayBankCode: null,
  updatedAt: null,
};

const storedSettings: CommerceSettings = {
  freeShippingThreshold: 700000,
  codMaxAmount: 15000000,
  maxQuantityPerSku: 3,
  maxCartLines: 10,
  sepayAccountNumber: '0123456789',
  sepayBankCode: 'MB',
  updatedAt: '2026-10-12T06:30:15.000Z',
};

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  requests = [];
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status });
}

interface ServerOptions {
  settings?: CommerceSettings;
  getStatus?: number;
  putHandler?: (body: Record<string, unknown>) => Promise<Response> | Response;
}

function stubServer(options: ServerOptions = {}) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: string, init?: RequestInit) => {
      const method = init?.method ?? 'GET';
      const url = new URL(input, 'http://localhost');
      const body = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined;
      requests.push({ method, url: url.pathname, body });
      if (url.pathname !== SETTINGS_PATH) {
        return jsonResponse({ error: { code: 'NOT_FOUND', message: 'Không có route giả.' } }, 404);
      }
      if (method === 'PUT') {
        if (!options.putHandler) {
          return jsonResponse({ error: { code: 'X', message: 'Chưa giả lập.' } }, 500);
        }
        return options.putHandler(body);
      }
      if (options.getStatus && options.getStatus !== 200) {
        return jsonResponse(
          { error: { code: 'INTERNAL_ERROR', message: 'Máy chủ đang bận.' } },
          options.getStatus,
        );
      }
      return jsonResponse({ data: options.settings ?? defaultSettings });
    }),
  );
}

async function settle() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
}

async function renderPage(actor: ActorData) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  queryClient.setQueryData(['actor'], actor);
  await act(async () =>
    root.render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <CommerceSettingsPage />
        </MemoryRouter>
      </QueryClientProvider>,
    ),
  );
  await settle();
}

function settingsRequests(method: string) {
  return requests.filter((request) => request.method === method && request.url === SETTINGS_PATH);
}

function inputValue(id: string): string {
  const element = container.querySelector<HTMLInputElement>(`#${id}`);
  if (!element) throw new Error(`Không thấy ô #${id}`);
  return element.value;
}

// Gán giá trị cho ô có kiểm soát của React: dùng setter gốc rồi phát sự kiện `input`.
async function typeInto(id: string, text: string) {
  const element = container.querySelector<HTMLInputElement>(`#${id}`);
  if (!element) throw new Error(`Không thấy ô #${id}`);
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
  await act(async () => {
    setter?.call(element, text);
    element.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

function saveButton(): HTMLButtonElement {
  const match = container.querySelector<HTMLButtonElement>('form button[type="submit"]');
  if (!match) throw new Error('Không thấy nút Lưu');
  return match;
}

async function clickSave() {
  await act(async () => {
    saveButton().click();
  });
  await settle();
}

function errorText(inputId: string): string | null {
  return container.querySelector(`#${inputId}-error`)?.textContent ?? null;
}

function putBodies() {
  return settingsRequests('PUT').map((request) => request.body);
}

function validationFailure(details: { key: string; message: string }[]) {
  const summary = details.map((detail) => `${detail.key}: ${detail.message}`).join('; ');
  return jsonResponse(
    { error: { code: 'VALIDATION_ERROR', message: `Dữ liệu không hợp lệ (${summary}).`, details } },
    400,
  );
}

const FIELD_IDS = {
  freeShippingThreshold: 'commerce-free-shipping-threshold',
  codMaxAmount: 'commerce-cod-max-amount',
  maxQuantityPerSku: 'commerce-max-quantity-per-sku',
  maxCartLines: 'commerce-max-cart-lines',
  sepayAccountNumber: 'commerce-sepay-account-number',
  sepayBankCode: 'commerce-sepay-bank-code',
};

describe('CommerceSettingsPage: quyền', () => {
  it('thiếu quyền thì hiện thông báo 403 nêu quyền cần có và không có form', async () => {
    stubServer();
    await renderPage(readOnlyActor);

    const notice = container.querySelector('[role="alert"]');
    expect(notice?.textContent).toContain('403');
    expect(notice?.textContent).toContain('commerce-settings:manage');
    expect(container.querySelector('form')).toBeNull();
  });

  it('thiếu quyền thì không gọi API cấu hình', async () => {
    stubServer();
    await renderPage(readOnlyActor);

    expect(requests).toHaveLength(0);
  });

  it('có quyền thì hiện tiêu đề, form và gọi GET đúng một lần', async () => {
    stubServer();
    await renderPage(managerActor);

    expect(container.querySelector('h1')?.textContent).toBe('Cấu hình bán hàng');
    expect(container.querySelector('form')).not.toBeNull();
    expect(settingsRequests('GET')).toHaveLength(1);
  });
});

describe('CommerceSettingsPage: tải và điền form', () => {
  it('chưa lưu lần nào thì điền giá trị mặc định và hai ô SePay trống', async () => {
    stubServer({ settings: defaultSettings });
    await renderPage(managerActor);

    expect(inputValue(FIELD_IDS.freeShippingThreshold)).toBe('500.000');
    expect(inputValue(FIELD_IDS.codMaxAmount)).toBe('20.000.000');
    expect(inputValue(FIELD_IDS.maxQuantityPerSku)).toBe('5');
    expect(inputValue(FIELD_IDS.maxCartLines)).toBe('20');
    expect(inputValue(FIELD_IDS.sepayAccountNumber)).toBe('');
    expect(inputValue(FIELD_IDS.sepayBankCode)).toBe('');
  });

  it('đã lưu thì điền đúng giá trị server trả về', async () => {
    stubServer({ settings: storedSettings });
    await renderPage(managerActor);

    expect(inputValue(FIELD_IDS.freeShippingThreshold)).toBe('700.000');
    expect(inputValue(FIELD_IDS.codMaxAmount)).toBe('15.000.000');
    expect(inputValue(FIELD_IDS.maxQuantityPerSku)).toBe('3');
    expect(inputValue(FIELD_IDS.maxCartLines)).toBe('10');
    expect(inputValue(FIELD_IDS.sepayAccountNumber)).toBe('0123456789');
    expect(inputValue(FIELD_IDS.sepayBankCode)).toBe('MB');
  });

  it('mỗi ô có nhãn và hai nhóm có tiêu đề đúng', async () => {
    stubServer({ settings: storedSettings });
    await renderPage(managerActor);

    const labels = Array.from(container.querySelectorAll('label')).map(
      (label) => label.textContent,
    );
    expect(labels).toEqual([
      'Ngưỡng miễn phí giao hàng',
      'Hạn mức thanh toán COD',
      'Số lượng tối đa mỗi sản phẩm',
      'Số dòng tối đa mỗi giỏ',
      'Số tài khoản',
      'Mã ngân hàng',
    ]);
    const headings = Array.from(container.querySelectorAll('h2')).map(
      (heading) => heading.textContent,
    );
    expect(headings).toEqual(['Giỏ hàng và giao hàng', 'Tài khoản nhận chuyển khoản']);
  });

  it('gợi ý dưới ô tiền chỉ nêu khoảng giới hạn, số đang nhập đã hiện trong ô', async () => {
    stubServer({ settings: defaultSettings });
    await renderPage(managerActor);

    const thresholdHint = container.querySelector(`#${FIELD_IDS.freeShippingThreshold}-hint`);
    expect(thresholdHint?.textContent).toBe('Từ 0 đến 1.000.000.000 đ.');
    const codHint = container.querySelector(`#${FIELD_IDS.codMaxAmount}-hint`);
    expect(codHint?.textContent).toBe('Từ 0 đến 1.000.000.000 đ.');
    expect(inputValue(FIELD_IDS.freeShippingThreshold)).toBe('500.000');
    expect(inputValue(FIELD_IDS.codMaxAmount)).toBe('20.000.000');
  });

  it('gợi ý không đổi theo số đang nhập vì ô đã hiện số theo định dạng VND', async () => {
    stubServer({ settings: defaultSettings });
    await renderPage(managerActor);

    await typeInto(FIELD_IDS.codMaxAmount, '1500000');

    expect(inputValue(FIELD_IDS.codMaxAmount)).toBe('1.500.000');
    expect(container.querySelector(`#${FIELD_IDS.codMaxAmount}-hint`)?.textContent).toBe(
      'Từ 0 đến 1.000.000.000 đ.',
    );
  });

  it('gợi ý giới hạn của số lượng, số dòng và cặp SePay', async () => {
    stubServer({ settings: defaultSettings });
    await renderPage(managerActor);

    const text = container.textContent ?? '';
    expect(text).toContain('Từ 1 đến 99.');
    expect(text).toContain('Từ 1 đến 100.');
    expect(text).toContain('Điền cả số tài khoản và mã ngân hàng, hoặc để trống cả hai.');
  });

  it('không có ô hay chữ nào nhắc tới khóa webhook SePay', async () => {
    stubServer({ settings: storedSettings });
    await renderPage(managerActor);

    expect(container.textContent?.toLowerCase()).not.toContain('webhook');
    expect(container.innerHTML).not.toContain('SEPAY_WEBHOOK_API_KEY');
  });

  it('lỗi tải cấu hình thì hiện thông báo của máy chủ và không hiện form', async () => {
    stubServer({ getStatus: 503 });
    await renderPage(managerActor);

    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Máy chủ đang bận.');
    expect(container.querySelector('form')).toBeNull();
  });
});

describe('CommerceSettingsPage: kiểm phía client', () => {
  // Ô tiền bỏ dấu trừ khi gõ nên không thể nhập số âm. Xóa trống ô là cách còn lại để ép lỗi ở ô tiền.
  it('ô tiền để trống thì lỗi hiện dưới đúng ô và không gửi PUT', async () => {
    stubServer({ settings: storedSettings });
    await renderPage(managerActor);
    await typeInto(FIELD_IDS.freeShippingThreshold, '');

    await clickSave();

    expect(errorText(FIELD_IDS.freeShippingThreshold)).toBe(
      'Ngưỡng miễn phí giao hàng phải là số nguyên từ 0 đến 1.000.000.000 đ.',
    );
    expect(errorText(FIELD_IDS.codMaxAmount)).toBeNull();
    expect(settingsRequests('PUT')).toHaveLength(0);
  });

  it('ô sai nhận aria-invalid và nối với câu lỗi', async () => {
    stubServer({ settings: storedSettings });
    await renderPage(managerActor);
    await typeInto(FIELD_IDS.maxCartLines, '0');

    await clickSave();

    const input = container.querySelector(`#${FIELD_IDS.maxCartLines}`);
    expect(input?.getAttribute('aria-invalid')).toBe('true');
    expect(input?.getAttribute('aria-describedby')).toBe(`${FIELD_IDS.maxCartLines}-error`);
  });

  it('chỉ nhập số tài khoản thì lỗi nằm ở ô mã ngân hàng', async () => {
    stubServer({ settings: defaultSettings });
    await renderPage(managerActor);
    await typeInto(FIELD_IDS.sepayAccountNumber, '0123456789');

    await clickSave();

    expect(errorText(FIELD_IDS.sepayBankCode)).toBe(
      'Cần nhập cả số tài khoản và mã ngân hàng, hoặc để trống cả hai.',
    );
    expect(errorText(FIELD_IDS.sepayAccountNumber)).toBeNull();
    expect(settingsRequests('PUT')).toHaveLength(0);
  });

  it('sửa ô sai thì lỗi của ô đó biến mất', async () => {
    stubServer({ settings: storedSettings });
    await renderPage(managerActor);
    await typeInto(FIELD_IDS.maxCartLines, '0');
    await clickSave();
    expect(errorText(FIELD_IDS.maxCartLines)).not.toBeNull();

    await typeInto(FIELD_IDS.maxCartLines, '10');

    expect(errorText(FIELD_IDS.maxCartLines)).toBeNull();
  });
});

describe('CommerceSettingsPage: lưu', () => {
  it('PUT đúng 6 trường, số là kiểu number và SePay trống thành null', async () => {
    stubServer({
      settings: storedSettings,
      putHandler: () => jsonResponse({ data: defaultSettings }),
    });
    await renderPage(managerActor);
    await typeInto(FIELD_IDS.freeShippingThreshold, ' 800000 ');
    await typeInto(FIELD_IDS.sepayAccountNumber, '');
    await typeInto(FIELD_IDS.sepayBankCode, '');

    await clickSave();

    expect(putBodies()).toEqual([
      {
        freeShippingThreshold: 800000,
        codMaxAmount: 15000000,
        maxQuantityPerSku: 3,
        maxCartLines: 10,
        sepayAccountNumber: null,
        sepayBankCode: null,
      },
    ]);
  });

  it('lưu thành công thì hiện thông báo role status và form nhận giá trị server trả về', async () => {
    stubServer({
      settings: storedSettings,
      putHandler: () => jsonResponse({ data: { ...storedSettings, sepayBankCode: 'VCB' } }),
    });
    await renderPage(managerActor);
    await typeInto(FIELD_IDS.sepayBankCode, ' vcb ');

    await clickSave();

    const status = container.querySelector('[role="status"]');
    expect(status?.textContent).toBe('Đã lưu cấu hình bán hàng.');
    expect(inputValue(FIELD_IDS.sepayBankCode)).toBe('VCB');
  });

  it('nút Lưu tắt khi đang gửi nên không gửi hai lần', async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    stubServer({
      settings: storedSettings,
      putHandler: async () => {
        await gate;
        return jsonResponse({ data: storedSettings });
      },
    });
    await renderPage(managerActor);

    await act(async () => {
      saveButton().click();
    });
    await settle();
    expect(saveButton().disabled).toBe(true);
    await act(async () => {
      saveButton().click();
    });
    await settle();
    expect(settingsRequests('PUT')).toHaveLength(1);

    await act(async () => {
      release();
    });
    await settle();
    expect(saveButton().disabled).toBe(false);
  });

  it('phím Enter gửi form lần hai khi đang lưu cũng không tạo PUT thứ hai', async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    stubServer({
      settings: storedSettings,
      putHandler: async () => {
        await gate;
        return jsonResponse({ data: storedSettings });
      },
    });
    await renderPage(managerActor);
    const form = container.querySelector('form') as HTMLFormElement;

    await act(async () => {
      form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    });
    await settle();
    await act(async () => {
      form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    });
    await settle();

    expect(settingsRequests('PUT')).toHaveLength(1);
    await act(async () => {
      release();
    });
    await settle();
  });

  it('lỗi 400 của server hiện dưới đúng ô, không hiện ErrorAlert, giữ dữ liệu đã nhập', async () => {
    stubServer({
      settings: storedSettings,
      putHandler: () =>
        validationFailure([
          { key: 'codMaxAmount', message: 'codMaxAmount phải là số nguyên từ 0 đến 1000000000.' },
        ]),
    });
    await renderPage(managerActor);
    await typeInto(FIELD_IDS.maxCartLines, '12');

    await clickSave();

    expect(errorText(FIELD_IDS.codMaxAmount)).toBe(
      'Hạn mức COD phải là số nguyên từ 0 đến 1.000.000.000 đ.',
    );
    expect(container.querySelector('[role="alert"]')).toBeNull();
    expect(inputValue(FIELD_IDS.maxCartLines)).toBe('12');
    expect(container.querySelector('[role="status"]')).toBeNull();
  });

  it('lỗi 400 của cặp SePay hiện dưới ô còn thiếu', async () => {
    stubServer({
      settings: storedSettings,
      putHandler: () =>
        validationFailure([
          { key: 'sepayAccountNumber', message: 'Cần nhập cả hai trường SePay.' },
        ]),
    });
    await renderPage(managerActor);

    await clickSave();

    expect(errorText(FIELD_IDS.sepayAccountNumber)).toBe('Cần nhập cả hai trường SePay.');
    expect(errorText(FIELD_IDS.sepayBankCode)).toBeNull();
  });

  it('lỗi 400 không gắn được vào ô nào thì hiện ErrorAlert đầu form', async () => {
    stubServer({
      settings: storedSettings,
      putHandler: () =>
        validationFailure([
          { key: 'sepayWebhookApiKey', message: 'Trường này không được hỗ trợ.' },
        ]),
    });
    await renderPage(managerActor);

    await clickSave();

    const alert = container.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('Trường này không được hỗ trợ.');
    const position = container.querySelector('form')?.compareDocumentPosition(alert as Node);
    expect((position ?? 0) & Node.DOCUMENT_POSITION_PRECEDING).toBeTruthy();
  });

  it('lỗi 503 hiện ErrorAlert đầu form và giữ dữ liệu đã nhập', async () => {
    stubServer({
      settings: storedSettings,
      putHandler: () =>
        jsonResponse({ error: { code: 'UNAVAILABLE', message: 'Dịch vụ tạm ngừng.' } }, 503),
    });
    await renderPage(managerActor);
    await typeInto(FIELD_IDS.maxQuantityPerSku, '7');

    await clickSave();

    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Dịch vụ tạm ngừng.');
    expect(inputValue(FIELD_IDS.maxQuantityPerSku)).toBe('7');
    expect(container.querySelector('[role="status"]')).toBeNull();
  });

  it('lưu lại sau lỗi thì thông báo thành công thay lỗi cũ', async () => {
    let attempt = 0;
    stubServer({
      settings: storedSettings,
      putHandler: () => {
        attempt += 1;
        return attempt === 1
          ? jsonResponse({ error: { code: 'UNAVAILABLE', message: 'Dịch vụ tạm ngừng.' } }, 503)
          : jsonResponse({ data: storedSettings });
      },
    });
    await renderPage(managerActor);

    await clickSave();
    await clickSave();

    expect(container.querySelector('[role="status"]')?.textContent).toBe(
      'Đã lưu cấu hình bán hàng.',
    );
    expect(container.querySelector('[role="alert"]')).toBeNull();
  });
});

describe('CommerceSettingsPage: ô tiền VND', () => {
  it('gõ số hiện dấu chấm nghìn và PUT số nguyên đồng', async () => {
    stubServer({
      settings: storedSettings,
      putHandler: () => jsonResponse({ data: storedSettings }),
    });
    await renderPage(managerActor);

    await typeInto(FIELD_IDS.freeShippingThreshold, '1250000');
    expect(inputValue(FIELD_IDS.freeShippingThreshold)).toBe('1.250.000');
    await clickSave();

    expect(putBodies()[0]).toEqual(expect.objectContaining({ freeShippingThreshold: 1250000 }));
  });

  it('dán chuỗi có đơn vị ra cùng số đồng', async () => {
    stubServer({ settings: storedSettings });
    await renderPage(managerActor);

    await typeInto(FIELD_IDS.codMaxAmount, '12.500.000 đ');

    expect(inputValue(FIELD_IDS.codMaxAmount)).toBe('12.500.000');
  });

  it('bỏ dấu trừ khi gõ nên ô tiền không nhận số âm', async () => {
    stubServer({ settings: storedSettings });
    await renderPage(managerActor);

    await typeInto(FIELD_IDS.freeShippingThreshold, '-1');

    expect(inputValue(FIELD_IDS.freeShippingThreshold)).toBe('1');
  });

  it('hai ô tiền có hậu tố đ, hai ô đếm thì không', async () => {
    stubServer({ settings: defaultSettings });
    await renderPage(managerActor);

    const isMoneyInput = (id: string) =>
      container.querySelector(`#${id}`)?.parentElement?.classList.contains('money-input');
    expect(isMoneyInput(FIELD_IDS.freeShippingThreshold)).toBe(true);
    expect(isMoneyInput(FIELD_IDS.codMaxAmount)).toBe(true);
    expect(isMoneyInput(FIELD_IDS.maxQuantityPerSku)).toBe(false);
    expect(isMoneyInput(FIELD_IDS.maxCartLines)).toBe(false);
  });

  it('ô tiền sai giữ câu lỗi đứng đầu aria-describedby và có thêm đơn vị', async () => {
    stubServer({ settings: storedSettings });
    await renderPage(managerActor);
    await typeInto(FIELD_IDS.freeShippingThreshold, '');

    await clickSave();

    const input = container.querySelector(`#${FIELD_IDS.freeShippingThreshold}`);
    expect(input?.getAttribute('aria-invalid')).toBe('true');
    const describedBy = (input?.getAttribute('aria-describedby') ?? '').split(' ');
    expect(describedBy[0]).toBe(`${FIELD_IDS.freeShippingThreshold}-error`);
    expect(document.getElementById(describedBy[1]!)?.textContent).toBe('đồng');
    expect(errorText(FIELD_IDS.freeShippingThreshold)).not.toBeNull();
  });
});

describe('CommerceSettingsPage: căn lề ô tiền', () => {
  it('hai ô tiền căn trái như các ô đếm ngay bên dưới', async () => {
    stubServer({ settings: defaultSettings });
    await renderPage(managerActor);

    const frames = [...container.querySelectorAll('.money-input')];
    expect(frames).toHaveLength(2);
    expect(frames.some((frame) => frame.classList.contains('money-input-end'))).toBe(false);
  });
});
