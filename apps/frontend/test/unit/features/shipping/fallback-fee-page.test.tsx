// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ActorData } from '../../../../src/features/identity/hooks/use-actor';
import { FallbackFeePage } from '../../../../src/features/shipping/admin/fallback-fee-page';

const FEE_PATH = '/api/admin/shipping/fallback-fee';

let container: HTMLDivElement;
let root: Root;
let requests: { method: string; url: string; body: Record<string, unknown> | undefined }[];

const managerActor: ActorData = {
  anonymousId: 'anon',
  userId: 'user-1',
  kind: 'staff',
  roles: ['sales'],
  permissions: ['shipping-settings:manage'],
  consent: { personalization: false, marketing: false },
};

const readOnlyActor: ActorData = { ...managerActor, permissions: ['products:read'] };

const storedFee = {
  enabled: true,
  sameProvinceBaseFee: 20_000,
  sameProvinceBaseWeightGrams: 1000,
  sameProvinceStepFee: 5_000,
  sameRegionBaseFee: 30_000,
  sameRegionBaseWeightGrams: 1000,
  sameRegionStepFee: 6_000,
  crossRegionBaseFee: 40_000,
  crossRegionBaseWeightGrams: 2000,
  crossRegionStepFee: 7_000,
  updatedAt: '2026-10-19T08:00:00.000Z',
};

const unsavedFee = {
  enabled: false,
  sameProvinceBaseFee: null,
  sameProvinceBaseWeightGrams: null,
  sameProvinceStepFee: null,
  sameRegionBaseFee: null,
  sameRegionBaseWeightGrams: null,
  sameRegionStepFee: null,
  crossRegionBaseFee: null,
  crossRegionBaseWeightGrams: null,
  crossRegionStepFee: null,
  updatedAt: null,
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
  fee?: typeof storedFee | typeof unsavedFee;
  getStatus?: number;
  // Hàm trả phản hồi cho `PUT`. Test dùng nó để giả lập thành công, lỗi 400, lỗi 422 hoặc lỗi khác.
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
      if (url.pathname !== FEE_PATH) {
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
      return jsonResponse({ data: options.fee ?? unsavedFee });
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
          <FallbackFeePage />
        </MemoryRouter>
      </QueryClientProvider>,
    ),
  );
  await settle();
}

function feeRequests(method: string) {
  return requests.filter((request) => request.method === method && request.url === FEE_PATH);
}

// Id ô theo quy ước `fallback-fee-<tên ô viết gạch nối>`, ví dụ `fallback-fee-same-province-base-fee`.
const IDS = {
  enabled: 'fallback-fee-enabled',
  sameProvinceBaseFee: 'fallback-fee-same-province-base-fee',
  sameProvinceBaseWeightGrams: 'fallback-fee-same-province-base-weight-grams',
  sameProvinceStepFee: 'fallback-fee-same-province-step-fee',
  sameRegionBaseFee: 'fallback-fee-same-region-base-fee',
  sameRegionBaseWeightGrams: 'fallback-fee-same-region-base-weight-grams',
  sameRegionStepFee: 'fallback-fee-same-region-step-fee',
  crossRegionBaseFee: 'fallback-fee-cross-region-base-fee',
  crossRegionBaseWeightGrams: 'fallback-fee-cross-region-base-weight-grams',
  crossRegionStepFee: 'fallback-fee-cross-region-step-fee',
} as const;

function input(id: string): HTMLInputElement {
  const element = container.querySelector<HTMLInputElement>(`#${id}`);
  if (!element) throw new Error(`Không thấy ô #${id}`);
  return element;
}

// Ô tiền hiện số có dấu chấm nghìn, nên so sánh theo chữ số.
function digitsOf(id: string): string {
  return input(id).value.replace(/\D/g, '');
}

async function typeInto(id: string, text: string) {
  const element = input(id);
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
  await act(async () => {
    setter?.call(element, text);
    element.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

async function toggleEnabled() {
  await act(async () => {
    input(IDS.enabled).click();
  });
}

async function clickSave() {
  const button = container.querySelector<HTMLButtonElement>('form button[type="submit"]');
  if (!button) throw new Error('Không thấy nút Lưu');
  await act(async () => {
    button.click();
  });
  await settle();
}

function errorText(inputId: string): string | null {
  return container.querySelector(`#${inputId}-error`)?.textContent ?? null;
}

function exampleFee(tierId: string): string {
  const cell = container.querySelector(`#fallback-fee-example-${tierId}`);
  if (!cell) throw new Error(`Không thấy ô ví dụ ${tierId}`);
  return cell.textContent ?? '';
}

async function fillAllNineFields() {
  await typeInto(IDS.sameProvinceBaseFee, '20000');
  await typeInto(IDS.sameProvinceBaseWeightGrams, '1000');
  await typeInto(IDS.sameProvinceStepFee, '5000');
  await typeInto(IDS.sameRegionBaseFee, '30000');
  await typeInto(IDS.sameRegionBaseWeightGrams, '1000');
  await typeInto(IDS.sameRegionStepFee, '6000');
  await typeInto(IDS.crossRegionBaseFee, '40000');
  await typeInto(IDS.crossRegionBaseWeightGrams, '2000');
  await typeInto(IDS.crossRegionStepFee, '7000');
}

describe('FallbackFeePage: quyền', () => {
  it('thiếu quyền thì hiện thông báo 403 nêu quyền cần có và không có form', async () => {
    stubServer();
    await renderPage(readOnlyActor);

    const notice = container.querySelector('[role="alert"]');
    expect(notice?.textContent).toContain('403');
    expect(notice?.textContent).toContain('shipping-settings:manage');
    expect(container.querySelector('form')).toBeNull();
  });

  it('thiếu quyền thì không gọi API phí dự phòng', async () => {
    stubServer();
    await renderPage(readOnlyActor);

    expect(feeRequests('GET')).toHaveLength(0);
    expect(feeRequests('PUT')).toHaveLength(0);
  });

  it('có quyền thì hiện tiêu đề, dòng giải thích và gọi GET đúng một lần', async () => {
    stubServer();
    await renderPage(managerActor);

    expect(container.querySelector('h1')?.textContent).toBe('Phí ship dự phòng');
    const description = container.querySelector('.page-head-text p')?.textContent ?? '';
    expect(description).toContain('GHN không phản hồi');
    expect(description).toContain('Khách thấy phí như bình thường');
    expect(feeRequests('GET')).toHaveLength(1);
  });

  it('lỗi tải hiện thông báo và không có form', async () => {
    stubServer({ getStatus: 500 });
    await renderPage(managerActor);

    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Máy chủ đang bận.');
    expect(container.querySelector('form')).toBeNull();
  });
});

describe('FallbackFeePage: bố cục và tải dữ liệu', () => {
  it('chưa lưu lần nào thì checkbox tắt và cả chín ô trống', async () => {
    stubServer({ fee: unsavedFee });
    await renderPage(managerActor);

    expect(input(IDS.enabled).checked).toBe(false);
    for (const [name, id] of Object.entries(IDS)) {
      if (name === 'enabled') continue;
      expect(input(id).value).toBe('');
    }
  });

  it('đã lưu thì điền đúng chín ô và bật checkbox', async () => {
    stubServer({ fee: storedFee });
    await renderPage(managerActor);

    expect(input(IDS.enabled).checked).toBe(true);
    expect(digitsOf(IDS.sameProvinceBaseFee)).toBe('20000');
    expect(input(IDS.sameProvinceBaseWeightGrams).value).toBe('1000');
    expect(digitsOf(IDS.sameProvinceStepFee)).toBe('5000');
    expect(digitsOf(IDS.sameRegionBaseFee)).toBe('30000');
    expect(input(IDS.sameRegionBaseWeightGrams).value).toBe('1000');
    expect(digitsOf(IDS.sameRegionStepFee)).toBe('6000');
    expect(digitsOf(IDS.crossRegionBaseFee)).toBe('40000');
    expect(input(IDS.crossRegionBaseWeightGrams).value).toBe('2000');
    expect(digitsOf(IDS.crossRegionStepFee)).toBe('7000');
  });

  it('có ba nhóm đúng thứ tự, mỗi nhóm là fieldset có legend và ba ô', async () => {
    stubServer();
    await renderPage(managerActor);

    const groups = Array.from(container.querySelectorAll('fieldset'));
    expect(groups.map((group) => group.querySelector('legend')?.textContent)).toEqual([
      'Nội tỉnh',
      'Nội vùng',
      'Liên vùng',
    ]);
    for (const group of groups) {
      expect(group.querySelectorAll('input')).toHaveLength(3);
      const labels = Array.from(group.querySelectorAll('label')).map((label) => label.textContent);
      expect(labels).toEqual(['Giá gốc', 'Khối lượng gốc', 'Giá mỗi 500 g tiếp theo']);
    }
  });

  it('ô tiền hiện đơn vị đồng, ô khối lượng nêu đơn vị gram', async () => {
    stubServer();
    await renderPage(managerActor);

    const moneyBox = input(IDS.sameProvinceBaseFee).closest('.money-input');
    expect(moneyBox?.textContent).toContain('đ');
    const weightField = input(IDS.sameProvinceBaseWeightGrams).closest('.field');
    expect(weightField?.textContent).toContain('gram');
  });

  it('ô khối lượng hiện chữ g trong ô như ô tiền hiện chữ đ, và nối đơn vị gram vào ô', async () => {
    stubServer();
    await renderPage(managerActor);

    for (const id of [
      IDS.sameProvinceBaseWeightGrams,
      IDS.sameRegionBaseWeightGrams,
      IDS.crossRegionBaseWeightGrams,
    ]) {
      const weightInput = input(id);
      const box = weightInput.closest('.money-input');
      const suffix = box?.querySelector('.money-input-suffix');
      expect(suffix?.textContent).toBe('g');
      expect(suffix?.getAttribute('aria-hidden')).toBe('true');
      // Đơn vị đọc cho trình đọc màn hình qua phần tử ẩn nối bằng `aria-describedby`.
      const describedBy = (weightInput.getAttribute('aria-describedby') ?? '').split(' ');
      const unitElement = container.querySelector(`#${id}-unit`);
      expect(unitElement?.textContent).toBe('gram');
      expect(describedBy).toContain(`${id}-unit`);
    }
  });

  it('checkbox có nhãn rõ và tắt thì các ô giá vẫn sửa được', async () => {
    stubServer({ fee: storedFee });
    await renderPage(managerActor);

    const label = container.querySelector(`label[for="${IDS.enabled}"]`);
    expect(label?.textContent).toContain('Dùng phí dự phòng khi GHN không phản hồi');
    await toggleEnabled();

    expect(input(IDS.enabled).checked).toBe(false);
    expect(input(IDS.sameRegionBaseFee).disabled).toBe(false);
    await typeInto(IDS.sameRegionBaseWeightGrams, '1500');
    expect(input(IDS.sameRegionBaseWeightGrams).value).toBe('1500');
  });
});

describe('FallbackFeePage: ví dụ tính phí', () => {
  it('hiện gói mẫu và khối lượng tính cước 1.500 g', async () => {
    stubServer();
    await renderPage(managerActor);

    const example = container.querySelector('#fallback-fee-example');
    expect(example?.textContent).toContain('1.200 g');
    expect(example?.textContent).toContain('30 × 20 × 10 cm');
    expect(example?.textContent).toContain('1.500 g');
  });

  it('chưa nhập giá thì cả ba tuyến hiện chưa đủ giá để tính', async () => {
    stubServer();
    await renderPage(managerActor);

    for (const tier of ['same-province', 'same-region', 'cross-region']) {
      expect(exampleFee(tier)).toBe('Chưa đủ giá để tính');
    }
  });

  it('đã lưu thì phí từng tuyến theo giá đã lưu', async () => {
    stubServer({ fee: storedFee });
    await renderPage(managerActor);

    // Nội tỉnh: 1500 g, gốc 1000 g, vượt 500 g là 1 bước: 20.000 + 5.000.
    expect(exampleFee('same-province')).toBe('25.000 đ');
    // Nội vùng: 30.000 + 6.000.
    expect(exampleFee('same-region')).toBe('36.000 đ');
    // Liên vùng: khối lượng gốc 2000 g lớn hơn 1500 g nên chỉ giá gốc.
    expect(exampleFee('cross-region')).toBe('40.000 đ');
  });

  it('nhập giá thì ví dụ đổi theo, và tuyến nào thiếu một ô vẫn hiện chưa đủ giá', async () => {
    stubServer();
    await renderPage(managerActor);

    await typeInto(IDS.sameProvinceBaseFee, '10000');
    await typeInto(IDS.sameProvinceBaseWeightGrams, '1000');
    expect(exampleFee('same-province')).toBe('Chưa đủ giá để tính');

    await typeInto(IDS.sameProvinceStepFee, '3000');
    expect(exampleFee('same-province')).toBe('13.000 đ');

    await typeInto(IDS.sameProvinceStepFee, '4000');
    expect(exampleFee('same-province')).toBe('14.000 đ');
    expect(exampleFee('same-region')).toBe('Chưa đủ giá để tính');
  });

  it('ô sai khoảng làm tuyến đó hiện chưa đủ giá thay vì tính ra số sai', async () => {
    stubServer({ fee: storedFee });
    await renderPage(managerActor);

    await typeInto(IDS.sameProvinceBaseWeightGrams, '0');

    expect(exampleFee('same-province')).toBe('Chưa đủ giá để tính');
    expect(exampleFee('same-region')).toBe('36.000 đ');
  });
});

describe('FallbackFeePage: lưu', () => {
  it('lưu thành công gửi đúng 10 trường, hiện câu đã lưu và nhận dữ liệu server trả về', async () => {
    stubServer({
      putHandler: (body) =>
        jsonResponse({ data: { ...body, updatedAt: '2026-10-19T09:00:00.000Z' } }),
    });
    await renderPage(managerActor);
    await toggleEnabled();
    await fillAllNineFields();

    await clickSave();

    expect(feeRequests('PUT')).toHaveLength(1);
    expect(feeRequests('PUT')[0]?.body).toEqual({
      enabled: true,
      sameProvinceBaseFee: 20_000,
      sameProvinceBaseWeightGrams: 1000,
      sameProvinceStepFee: 5_000,
      sameRegionBaseFee: 30_000,
      sameRegionBaseWeightGrams: 1000,
      sameRegionStepFee: 6_000,
      crossRegionBaseFee: 40_000,
      crossRegionBaseWeightGrams: 2000,
      crossRegionStepFee: 7_000,
    });
    expect(container.querySelector('[role="status"]')?.textContent).toBe(
      'Đã lưu phí ship dự phòng.',
    );
    expect(container.querySelector('[role="alert"]')).toBeNull();
  });

  it('tắt và để trống vẫn lưu được, ô trống gửi null', async () => {
    stubServer({
      putHandler: (body) =>
        jsonResponse({ data: { ...body, updatedAt: '2026-10-19T09:00:00.000Z' } }),
    });
    await renderPage(managerActor);

    await clickSave();

    const body = feeRequests('PUT')[0]?.body;
    expect(body?.enabled).toBe(false);
    expect(body?.sameProvinceBaseFee).toBeNull();
    expect(body?.crossRegionStepFee).toBeNull();
  });

  it('bật mà thiếu ô thì lỗi hiện dưới đúng ô và không gửi PUT', async () => {
    stubServer();
    await renderPage(managerActor);
    await toggleEnabled();
    await fillAllNineFields();
    await typeInto(IDS.sameRegionStepFee, '');

    await clickSave();

    expect(feeRequests('PUT')).toHaveLength(0);
    expect(errorText(IDS.sameRegionStepFee)).toBe('Cần nhập giá trị này khi bật phí dự phòng.');
    expect(errorText(IDS.sameRegionBaseFee)).toBeNull();
    expect(input(IDS.sameRegionStepFee).getAttribute('aria-invalid')).toBe('true');
  });

  it('khối lượng gốc 0 hiện lỗi khoảng gram dưới ô đó và không gửi PUT', async () => {
    stubServer({ fee: storedFee });
    await renderPage(managerActor);
    await typeInto(IDS.crossRegionBaseWeightGrams, '0');

    await clickSave();

    expect(feeRequests('PUT')).toHaveLength(0);
    expect(errorText(IDS.crossRegionBaseWeightGrams)).toBe(
      'Khối lượng gốc phải là số nguyên từ 1 đến 20.000 gram.',
    );
  });

  it('sửa ô sai thì lỗi của ô đó biến mất', async () => {
    stubServer({ fee: storedFee });
    await renderPage(managerActor);
    await typeInto(IDS.crossRegionBaseWeightGrams, '0');
    await clickSave();

    await typeInto(IDS.crossRegionBaseWeightGrams, '2000');

    expect(errorText(IDS.crossRegionBaseWeightGrams)).toBeNull();
  });

  it('lỗi 400 từ server hiện dưới đúng ô và không hiện lỗi chung', async () => {
    stubServer({
      fee: storedFee,
      putHandler: () =>
        jsonResponse(
          {
            error: {
              code: 'VALIDATION_ERROR',
              message: 'Dữ liệu không hợp lệ.',
              details: [
                {
                  key: 'sameRegionBaseFee',
                  message: 'Giá phải là số nguyên từ 0 đến 5.000.000 đồng.',
                },
              ],
            },
          },
          400,
        ),
    });
    await renderPage(managerActor);

    await clickSave();

    expect(feeRequests('PUT')).toHaveLength(1);
    expect(errorText(IDS.sameRegionBaseFee)).toBe('Giá phải là số nguyên từ 0 đến 5.000.000 đồng.');
    expect(errorText(IDS.sameProvinceBaseFee)).toBeNull();
    expect(errorText(IDS.crossRegionBaseFee)).toBeNull();
    expect(container.querySelector('[role="alert"]')).toBeNull();
    expect(container.querySelector('[role="status"]')).toBeNull();
  });

  it('lỗi 422 thiếu giá từ server hiện dưới từng ô thiếu', async () => {
    stubServer({
      fee: storedFee,
      putHandler: () =>
        jsonResponse(
          {
            error: {
              code: 'FALLBACK_FEE_INCOMPLETE',
              message: 'Bật phí dự phòng cần nhập đủ giá của cả 3 tuyến.',
              details: [
                {
                  key: 'crossRegionBaseFee',
                  message: 'Cần nhập giá trị này khi bật phí dự phòng.',
                },
                {
                  key: 'crossRegionStepFee',
                  message: 'Cần nhập giá trị này khi bật phí dự phòng.',
                },
              ],
            },
          },
          422,
        ),
    });
    await renderPage(managerActor);

    await clickSave();

    expect(errorText(IDS.crossRegionBaseFee)).toBe('Cần nhập giá trị này khi bật phí dự phòng.');
    expect(errorText(IDS.crossRegionStepFee)).toBe('Cần nhập giá trị này khi bật phí dự phòng.');
    expect(errorText(IDS.crossRegionBaseWeightGrams)).toBeNull();
    expect(errorText(IDS.sameProvinceBaseFee)).toBeNull();
  });

  it('lỗi không gắn được vào ô (503) hiện ở đầu form và giữ dữ liệu đã nhập', async () => {
    stubServer({
      fee: storedFee,
      putHandler: () =>
        jsonResponse({ error: { code: 'UNAVAILABLE', message: 'Máy chủ đang bảo trì.' } }, 503),
    });
    await renderPage(managerActor);
    await typeInto(IDS.sameRegionBaseWeightGrams, '1500');

    await clickSave();

    expect(container.querySelector('[role="alert"]')?.textContent).toContain(
      'Máy chủ đang bảo trì.',
    );
    expect(input(IDS.sameRegionBaseWeightGrams).value).toBe('1500');
    expect(container.querySelector('[role="status"]')).toBeNull();
  });

  it('sửa ô sau khi lưu thì câu đã lưu biến mất', async () => {
    stubServer({
      fee: storedFee,
      putHandler: (body) =>
        jsonResponse({ data: { ...body, updatedAt: '2026-10-19T09:00:00.000Z' } }),
    });
    await renderPage(managerActor);
    await clickSave();
    expect(container.querySelector('[role="status"]')).not.toBeNull();

    await typeInto(IDS.sameRegionBaseWeightGrams, '1500');

    expect(container.querySelector('[role="status"]')).toBeNull();
  });
});
