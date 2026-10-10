// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ActorData } from '../../../../src/features/identity/hooks/use-actor';
import { ShippingSettingsPage } from '../../../../src/features/shipping/admin/shipping-settings-page';

let container: HTMLDivElement;
let root: Root;
let requests: { method: string; url: string; body: unknown }[];

const managerActor: ActorData = {
  anonymousId: 'anon',
  userId: 'user-1',
  kind: 'staff',
  roles: ['sales'],
  permissions: ['shipping-settings:manage'],
  consent: { personalization: false, marketing: false },
};

const readOnlyActor: ActorData = { ...managerActor, permissions: ['products:read'] };

const provinces = [
  { code: '01', name: 'Hà Nội' },
  { code: '79', name: 'Hồ Chí Minh' },
];
const wardsByProvince: Record<string, { code: string; name: string }[]> = {
  '01': [{ code: '0101', name: 'Phường Ba Đình' }],
  '79': [{ code: '7901', name: 'Phường Bến Nghé' }],
};

interface StoredSettings {
  senderName: string;
  senderPhone: string;
  provinceCode: string;
  provinceName: string;
  wardCode: string;
  wardName: string;
  line: string;
  defaultServiceTypeId: number | null;
  updatedAt: string;
}

const storedSettings: StoredSettings = {
  senderName: 'Kho Pickory',
  senderPhone: '0912345678',
  provinceCode: '01',
  provinceName: 'Hà Nội',
  wardCode: '0101',
  wardName: 'Phường Ba Đình',
  line: '12 Phố Test',
  defaultServiceTypeId: 5,
  updatedAt: '2026-10-10T06:30:15.000Z',
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
  settings?: StoredSettings | null;
  ghnShopId?: string | null;
  getStatus?: number;
  // Hàm trả phản hồi cho `PUT`. Test dùng nó để giả lập thành công, lỗi 400 hoặc lỗi khác.
  putHandler?: (body: Record<string, unknown>) => Promise<Response> | Response;
}

function stubServer(options: ServerOptions = {}) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: string, init?: RequestInit) => {
      const method = init?.method ?? 'GET';
      const url = new URL(input, 'http://localhost');
      const body = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined;
      requests.push({ method, url: url.pathname + url.search, body });
      if (url.pathname === '/api/addresses/provinces') return jsonResponse({ data: provinces });
      if (url.pathname === '/api/addresses/wards') {
        const provinceCode = url.searchParams.get('provinceCode') ?? '';
        return jsonResponse({ data: wardsByProvince[provinceCode] ?? [] });
      }
      if (url.pathname === '/api/admin/shipping/settings') {
        if (method === 'PUT') {
          if (!options.putHandler)
            return jsonResponse({ error: { code: 'X', message: 'Chưa giả lập.' } }, 500);
          return options.putHandler(body);
        }
        if (options.getStatus && options.getStatus !== 200) {
          return jsonResponse(
            { error: { code: 'INTERNAL_ERROR', message: 'Máy chủ đang bận.' } },
            options.getStatus,
          );
        }
        return jsonResponse({
          data: {
            settings: options.settings === undefined ? null : options.settings,
            ghnShopId: options.ghnShopId === undefined ? '123456' : options.ghnShopId,
          },
        });
      }
      return jsonResponse({ error: { code: 'NOT_FOUND', message: 'Không có route giả.' } }, 404);
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
          <ShippingSettingsPage />
        </MemoryRouter>
      </QueryClientProvider>,
    ),
  );
  await settle();
}

function settingsRequests(method: string) {
  return requests.filter(
    (request) => request.method === method && request.url === '/api/admin/shipping/settings',
  );
}

describe('ShippingSettingsPage: quyền', () => {
  it('thiếu quyền thì hiện thông báo 403 nêu quyền cần có', async () => {
    stubServer();
    await renderPage(readOnlyActor);

    const notice = container.querySelector('[role="alert"]');
    expect(notice?.textContent).toContain('403');
    expect(notice?.textContent).toContain('shipping-settings:manage');
    expect(container.querySelector('form')).toBeNull();
  });

  it('thiếu quyền thì không gọi API cấu hình', async () => {
    stubServer();
    await renderPage(readOnlyActor);

    expect(settingsRequests('GET')).toHaveLength(0);
    expect(settingsRequests('PUT')).toHaveLength(0);
  });

  it('có quyền thì hiện form và gọi GET cấu hình đúng một lần', async () => {
    stubServer();
    await renderPage(managerActor);

    expect(container.querySelector('form')).not.toBeNull();
    expect(container.querySelector('h1')?.textContent).toBe('Cấu hình vận chuyển');
    expect(settingsRequests('GET')).toHaveLength(1);
  });
});

function inputValue(id: string): string {
  const element = container.querySelector<HTMLInputElement | HTMLSelectElement>(`#${id}`);
  if (!element) throw new Error(`Không thấy ô #${id}`);
  return element.value;
}

describe('ShippingSettingsPage: tải và điền form', () => {
  it('chưa cấu hình thì mọi ô trống và gói dịch vụ là mặc định của GHN', async () => {
    stubServer({ settings: null });
    await renderPage(managerActor);

    expect(inputValue('shipping-sender-name')).toBe('');
    expect(inputValue('shipping-sender-phone')).toBe('');
    expect(inputValue('shipping-address-province')).toBe('');
    expect(inputValue('shipping-address-ward')).toBe('');
    expect(inputValue('shipping-address-line')).toBe('');
    expect(inputValue('shipping-service-type')).toBe('');
  });

  it('đã cấu hình thì điền đúng, kể cả tên tỉnh/thành và phường/xã', async () => {
    stubServer({ settings: storedSettings });
    await renderPage(managerActor);

    expect(inputValue('shipping-sender-name')).toBe('Kho Pickory');
    expect(inputValue('shipping-sender-phone')).toBe('0912345678');
    expect(inputValue('shipping-address-province')).toBe('Hà Nội');
    expect(inputValue('shipping-address-ward')).toBe('Phường Ba Đình');
    expect(inputValue('shipping-address-line')).toBe('12 Phố Test');
    expect(inputValue('shipping-service-type')).toBe('5');
  });

  it('gói dịch vụ có đúng ba lựa chọn: mặc định, hàng nhẹ và hàng nặng', async () => {
    stubServer({ settings: null });
    await renderPage(managerActor);

    const options = Array.from(
      container.querySelectorAll<HTMLOptionElement>('#shipping-service-type option'),
    ).map((option) => [option.value, option.textContent]);
    expect(options).toEqual([
      ['', 'Theo mặc định của GHN (hàng nhẹ)'],
      ['2', 'Hàng nhẹ'],
      ['5', 'Hàng nặng'],
    ]);
  });

  it('hiện GHN ShopId ở ô bị khóa, người dùng không sửa được', async () => {
    stubServer({ settings: null, ghnShopId: '123456' });
    await renderPage(managerActor);

    const field = container.querySelector<HTMLInputElement>('#shipping-ghn-shop-id');
    expect(field?.value).toBe('123456');
    expect(field?.disabled).toBe(true);
  });

  it('ghnShopId là null thì cảnh báo chưa đặt GHN_SHOP_ID', async () => {
    stubServer({ settings: null, ghnShopId: null });
    await renderPage(managerActor);

    const alerts = Array.from(container.querySelectorAll('[role="alert"]'));
    expect(alerts.some((alert) => alert.textContent?.includes('GHN_SHOP_ID'))).toBe(true);
  });

  it('lỗi tải cấu hình thì hiện thông báo lỗi của máy chủ và không hiện form', async () => {
    stubServer({ getStatus: 503 });
    await renderPage(managerActor);

    const alert = container.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('Máy chủ đang bận.');
    expect(container.querySelector('#shipping-sender-name')).toBeNull();
  });
});

// Đặt giá trị cho ô có kiểm soát của React: gán bằng setter gốc rồi phát sự kiện `input`.
async function typeInto(id: string, text: string) {
  const element = container.querySelector<HTMLInputElement | HTMLSelectElement>(`#${id}`);
  if (!element) throw new Error(`Không thấy ô #${id}`);
  const isSelect = element instanceof HTMLSelectElement;
  const prototype = isSelect ? HTMLSelectElement.prototype : HTMLInputElement.prototype;
  const setter = Object.getOwnPropertyDescriptor(prototype, 'value')?.set;
  await act(async () => {
    setter?.call(element, text);
    element.dispatchEvent(new Event(isSelect ? 'change' : 'input', { bubbles: true }));
  });
}

function saveButton(): HTMLButtonElement {
  // Nút gửi form. Tìm theo loại nút vì nhãn đổi thành "Đang lưu..." khi đang gửi.
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

function savedResponse(overrides: Partial<StoredSettings> = {}) {
  return jsonResponse({
    data: { settings: { ...storedSettings, ...overrides }, ghnShopId: '123456' },
  });
}

// Đúng dạng backend trả: câu `message` cho người đọc và `details` có `key` cho mã nguồn đọc.
function validationFailure(details: { key: string; message: string }[]) {
  const summary = details.map((detail) => `${detail.key}: ${detail.message}`).join('; ');
  return jsonResponse(
    { error: { code: 'VALIDATION_ERROR', message: `Dữ liệu không hợp lệ (${summary}).`, details } },
    400,
  );
}

describe('ShippingSettingsPage: kiểm phía client', () => {
  it('form trống thì lỗi hiện dưới đúng từng ô và không gửi PUT', async () => {
    stubServer({ settings: null });
    await renderPage(managerActor);

    await clickSave();

    expect(errorText('shipping-sender-name')).toBe('Cần nhập tên người gửi.');
    expect(errorText('shipping-sender-phone')).toBe('Cần nhập số điện thoại người gửi.');
    expect(errorText('shipping-address-province')).toBe('Cần chọn tỉnh/thành phố.');
    expect(errorText('shipping-address-ward')).toBe('Cần chọn phường/xã.');
    expect(errorText('shipping-address-line')).toBe('Cần nhập số nhà, tên đường.');
    expect(settingsRequests('PUT')).toHaveLength(0);
  });

  it('số điện thoại sai thì chỉ ô số điện thoại có lỗi và không gửi PUT', async () => {
    stubServer({ settings: storedSettings });
    await renderPage(managerActor);
    await typeInto('shipping-sender-phone', '0212345678');

    await clickSave();

    expect(errorText('shipping-sender-phone')).toBe('Số điện thoại không đúng định dạng.');
    expect(errorText('shipping-sender-name')).toBeNull();
    expect(errorText('shipping-address-line')).toBeNull();
    expect(settingsRequests('PUT')).toHaveLength(0);
  });

  it('ô sai nhận aria-invalid để trình đọc màn hình đọc được lỗi', async () => {
    stubServer({ settings: storedSettings });
    await renderPage(managerActor);
    await typeInto('shipping-sender-name', '   ');

    await clickSave();

    const input = container.querySelector('#shipping-sender-name');
    expect(input?.getAttribute('aria-invalid')).toBe('true');
    expect(input?.getAttribute('aria-describedby')).toBe('shipping-sender-name-error');
  });
});

describe('ShippingSettingsPage: lưu', () => {
  it('PUT đúng 6 trường, không có tên tỉnh, tên phường hay ghnShopId', async () => {
    stubServer({ settings: storedSettings, putHandler: () => savedResponse() });
    await renderPage(managerActor);
    await typeInto('shipping-sender-phone', ' 0912 345 678 ');
    await typeInto('shipping-service-type', '2');

    await clickSave();

    expect(putBodies()).toEqual([
      {
        senderName: 'Kho Pickory',
        senderPhone: '0912 345 678',
        provinceCode: '01',
        wardCode: '0101',
        line: '12 Phố Test',
        defaultServiceTypeId: 2,
      },
    ]);
  });

  it('gói dịch vụ mặc định gửi null', async () => {
    stubServer({ settings: storedSettings, putHandler: () => savedResponse() });
    await renderPage(managerActor);
    await typeInto('shipping-service-type', '');

    await clickSave();

    expect((putBodies()[0] as { defaultServiceTypeId: unknown }).defaultServiceTypeId).toBeNull();
  });

  it('lưu thành công thì hiện thông báo và form nhận số đã chuẩn hóa từ response', async () => {
    stubServer({
      settings: { ...storedSettings, senderPhone: '0900000000' },
      putHandler: () => savedResponse({ senderPhone: '0912345678', senderName: 'Kho đã cắt' }),
    });
    await renderPage(managerActor);
    await typeInto('shipping-sender-phone', '+84 912 345 678');

    await clickSave();

    expect(container.querySelector('.alert-ok')?.textContent).toBe('Đã lưu cấu hình vận chuyển.');
    expect(inputValue('shipping-sender-phone')).toBe('0912345678');
    expect(inputValue('shipping-sender-name')).toBe('Kho đã cắt');
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
        return savedResponse();
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

  it('lỗi 400 của wardCode hiện dưới ô phường/xã, không hiện ErrorAlert, giữ dữ liệu đã nhập', async () => {
    stubServer({
      settings: storedSettings,
      putHandler: () =>
        validationFailure([
          { key: 'wardCode', message: 'Phường/xã không thuộc tỉnh/thành đã chọn.' },
        ]),
    });
    await renderPage(managerActor);
    await typeInto('shipping-sender-name', 'Tên đang sửa dở');

    await clickSave();

    expect(errorText('shipping-address-ward')).toBe('Phường/xã không thuộc tỉnh/thành đã chọn.');
    expect(errorText('shipping-address-province')).toBeNull();
    expect(container.querySelector('[role="alert"]')).toBeNull();
    expect(inputValue('shipping-sender-name')).toBe('Tên đang sửa dở');
  });

  it('lỗi 400 của provinceCode hiện dưới ô tỉnh/thành, lỗi line hiện dưới ô số nhà', async () => {
    stubServer({
      settings: storedSettings,
      putHandler: () =>
        validationFailure([
          {
            key: 'provinceCode',
            message: 'Tỉnh/thành không có trong danh mục hoặc đã ngừng dùng.',
          },
          { key: 'line', message: 'Địa chỉ chi tiết tối đa 200 ký tự.' },
        ]),
    });
    await renderPage(managerActor);

    await clickSave();

    expect(errorText('shipping-address-province')).toBe(
      'Tỉnh/thành không có trong danh mục hoặc đã ngừng dùng.',
    );
    expect(errorText('shipping-address-line')).toBe('Địa chỉ chi tiết tối đa 200 ký tự.');
    expect(errorText('shipping-address-ward')).toBeNull();
  });

  it('lỗi 400 không gắn được vào ô nào thì hiện ErrorAlert đầu form', async () => {
    stubServer({
      settings: storedSettings,
      putHandler: () =>
        validationFailure([{ key: 'provinceName', message: 'Trường này không được hỗ trợ.' }]),
    });
    await renderPage(managerActor);

    await clickSave();

    const alert = container.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('Trường này không được hỗ trợ.');
    // Lỗi đứng trước form để người dùng thấy ngay khi bấm Lưu.
    const formPosition = container.querySelector('form')?.compareDocumentPosition(alert as Node);
    expect((formPosition ?? 0) & Node.DOCUMENT_POSITION_PRECEDING).toBeTruthy();
  });

  it('lỗi 400 không có details thì hiện nguyên câu message ở ErrorAlert, không gán vào ô nào', async () => {
    stubServer({
      settings: storedSettings,
      putHandler: () =>
        jsonResponse(
          {
            error: {
              code: 'VALIDATION_ERROR',
              message:
                'Dữ liệu không hợp lệ (wardCode: Phường/xã không thuộc tỉnh/thành đã chọn.).',
            },
          },
          400,
        ),
    });
    await renderPage(managerActor);

    await clickSave();

    expect(errorText('shipping-address-ward')).toBeNull();
    expect(container.querySelector('[role="alert"]')?.textContent).toContain(
      'Dữ liệu không hợp lệ (wardCode:',
    );
  });

  it('lỗi 503 hiện ErrorAlert đầu form và giữ nguyên dữ liệu đã nhập', async () => {
    stubServer({
      settings: storedSettings,
      putHandler: () =>
        jsonResponse(
          { error: { code: 'SHIPPING_UNAVAILABLE', message: 'Dịch vụ tạm ngừng.' } },
          503,
        ),
    });
    await renderPage(managerActor);
    await typeInto('shipping-address-line', '99 Đường mới');

    await clickSave();

    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Dịch vụ tạm ngừng.');
    expect(inputValue('shipping-address-line')).toBe('99 Đường mới');
    expect(container.querySelector('.alert-ok')).toBeNull();
  });

  it('lưu lại sau lỗi thì thông báo thành công thay lỗi cũ', async () => {
    let attempt = 0;
    stubServer({
      settings: storedSettings,
      putHandler: () => {
        attempt += 1;
        return attempt === 1
          ? jsonResponse(
              { error: { code: 'SHIPPING_UNAVAILABLE', message: 'Dịch vụ tạm ngừng.' } },
              503,
            )
          : savedResponse();
      },
    });
    await renderPage(managerActor);

    await clickSave();
    await clickSave();

    expect(container.querySelector('.alert-ok')?.textContent).toBe('Đã lưu cấu hình vận chuyển.');
    expect(container.querySelector('.alert-list')).toBeNull();
  });
});
