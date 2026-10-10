// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  AddressPicker,
  EMPTY_ADDRESS,
  type AddressValue,
} from '../../../../src/features/shipping/address-picker';
import type { FieldErrors } from '../../../../src/shared/field-error';

type PickerErrors = FieldErrors<'province' | 'ward' | 'line'>;

const provinces = [
  { code: '1001', name: 'Cần Thơ' },
  { code: '1002', name: 'Đà Nẵng' },
  { code: '1003', name: 'Hà Nội' },
];

const wardsByProvince: Record<string, { code: string; name: string }[]> = {
  '1001': [
    { code: '2001', name: 'Phường Cái Khế' },
    { code: '2002', name: 'Phường Ninh Kiều' },
  ],
  '1003': [
    { code: '3001', name: 'Phường Ba Đình' },
    { code: '3002', name: 'Phường Hoàn Kiếm' },
  ],
};

let container: HTMLDivElement;
let root: Root;
let requestedUrls: string[];

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  requestedUrls = [];
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
  provincesStatus?: number;
  // Trả `Promise` để test giữ lời gọi phường/xã ở trạng thái đang tải rồi mới cho chạy tiếp.
  wardsGate?: Promise<void>;
}

function stubServer(options: ServerOptions = {}) {
  const state = { provincesStatus: options.provincesStatus ?? 200 };
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: string) => {
      requestedUrls.push(input);
      const url = new URL(input, 'http://localhost');
      if (url.pathname === '/api/addresses/provinces') {
        if (state.provincesStatus !== 200) {
          return jsonResponse(
            { error: { code: 'X', message: 'Máy chủ đang bận.' } },
            state.provincesStatus,
          );
        }
        return jsonResponse({ data: provinces });
      }
      if (url.pathname === '/api/addresses/wards') {
        await options.wardsGate;
        const provinceCode = url.searchParams.get('provinceCode') ?? '';
        return jsonResponse({ data: wardsByProvince[provinceCode] ?? [] });
      }
      return jsonResponse({ error: { code: 'NOT_FOUND', message: 'Không có route giả.' } }, 404);
    }),
  );
  return state;
}

function wardRequestCount(provinceCode: string): number {
  return requestedUrls.filter((url) =>
    url.includes(`/api/addresses/wards?provinceCode=${provinceCode}`),
  ).length;
}

// Cha giữ giá trị như form thật: `AddressPicker` không tự giữ giá trị.
function Harness({
  initial,
  errors,
  onChangeSpy,
  idPrefix,
}: {
  initial?: AddressValue;
  errors?: PickerErrors;
  onChangeSpy?: (value: AddressValue) => void;
  idPrefix?: string;
}) {
  const [value, setValue] = useState<AddressValue>(initial ?? EMPTY_ADDRESS);
  return (
    <AddressPicker
      value={value}
      errors={errors}
      idPrefix={idPrefix}
      onChange={(next) => {
        onChangeSpy?.(next);
        setValue(next);
      }}
    />
  );
}

async function settle() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
}

async function renderNode(node: React.ReactNode) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  await act(async () =>
    root.render(<QueryClientProvider client={queryClient}>{node}</QueryClientProvider>),
  );
  await settle();
}

function inputById(suffix: string): HTMLInputElement {
  const input = container.querySelector<HTMLInputElement>(
    `input[role="combobox"][id$="-${suffix}"]`,
  );
  if (!input) throw new Error(`Không có ô "${suffix}" trong AddressPicker.`);
  return input;
}

function provinceInput(): HTMLInputElement {
  return inputById('province');
}

function wardInput(): HTMLInputElement {
  return inputById('ward');
}

function lineInput(): HTMLInputElement {
  return container.querySelector('input[name="addressLine"]') as HTMLInputElement;
}

// Danh sách đóng vẫn giữ dòng trong DOM (ẩn bằng `hidden`), nên chỉ đếm dòng trong listbox của phường/xã.
function wardOptionNames(): string[] {
  return Array.from(container.querySelectorAll('[id$="-ward-listbox"] [role="option"]')).map(
    (option) => option.textContent ?? '',
  );
}

async function openAndChoose(input: HTMLInputElement, optionName: string) {
  // Mở danh sách bằng bàn phím rồi bấm đúng dòng, như khách dùng chuột sau khi mở.
  await act(async () => {
    input.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }),
    );
  });
  const optionElement = Array.from(container.querySelectorAll('[role="option"]')).find(
    (element) => element.textContent === optionName,
  ) as HTMLElement;
  expect(optionElement, `không có dòng "${optionName}"`).toBeTruthy();
  await act(async () => optionElement.click());
  await settle();
}

describe('AddressPicker: bố cục và nhãn', () => {
  it('có ba ô với nhãn đúng và ô phường/xã tắt khi chưa chọn tỉnh/thành', async () => {
    stubServer();
    await renderNode(<Harness />);
    const labels = Array.from(container.querySelectorAll('label')).map(
      (label) => label.textContent,
    );
    expect(labels).toEqual(['Tỉnh/Thành phố', 'Phường/Xã', 'Số nhà, tên đường']);
    const province = provinceInput();
    const ward = wardInput();
    expect(province.disabled).toBe(false);
    expect(ward.disabled).toBe(true);
    expect(lineInput().disabled).toBe(false);
  });

  it('chưa chọn tỉnh/thành thì không gọi route phường/xã', async () => {
    stubServer();
    await renderNode(<Harness />);
    expect(requestedUrls).toEqual(['/api/addresses/provinces']);
  });

  it('mỗi nhãn nối với đúng ô qua htmlFor', async () => {
    stubServer();
    await renderNode(<Harness />);
    for (const label of Array.from(container.querySelectorAll('label'))) {
      const target = container.querySelector(`[id="${label.htmlFor}"]`);
      expect(target, `nhãn "${label.textContent}"`).toBeTruthy();
    }
  });

  it('hai AddressPicker trên một trang không trùng id', async () => {
    stubServer();
    await renderNode(
      <>
        <Harness />
        <Harness />
      </>,
    );
    const ids = Array.from(container.querySelectorAll('[id]')).map((element) => element.id);
    expect(ids.length).toBeGreaterThan(6);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it('idPrefix do form cha đặt được dùng làm tiền tố id', async () => {
    stubServer();
    await renderNode(<Harness idPrefix="ship-to" />);
    expect(provinceInput().id.startsWith('ship-to')).toBe(true);
  });
});

describe('AddressPicker: chọn tỉnh/thành và phường/xã', () => {
  it('chọn tỉnh/thành gọi onChange kèm mã và tên, giữ số nhà đã nhập', async () => {
    stubServer();
    const onChangeSpy = vi.fn();
    await renderNode(
      <Harness onChangeSpy={onChangeSpy} initial={{ ...EMPTY_ADDRESS, line: '12 Lê Lợi' }} />,
    );
    await openAndChoose(provinceInput(), 'Hà Nội');
    expect(onChangeSpy).toHaveBeenLastCalledWith({
      provinceCode: '1003',
      provinceName: 'Hà Nội',
      wardCode: '',
      wardName: '',
      line: '12 Lê Lợi',
    });
  });

  it('chọn tỉnh/thành xong thì ô phường/xã bật và nạp danh sách của tỉnh đó', async () => {
    stubServer();
    await renderNode(<Harness />);
    await openAndChoose(provinceInput(), 'Hà Nội');
    expect(wardInput().disabled).toBe(false);
    expect(wardRequestCount('1003')).toBe(1);
  });

  it('chọn phường/xã gọi onChange kèm mã và tên của phường/xã', async () => {
    stubServer();
    const onChangeSpy = vi.fn();
    await renderNode(<Harness onChangeSpy={onChangeSpy} />);
    await openAndChoose(provinceInput(), 'Hà Nội');
    await openAndChoose(wardInput(), 'Phường Hoàn Kiếm');
    expect(onChangeSpy).toHaveBeenLastCalledWith({
      provinceCode: '1003',
      provinceName: 'Hà Nội',
      wardCode: '3002',
      wardName: 'Phường Hoàn Kiếm',
      line: '',
    });
  });

  it('đổi sang tỉnh/thành khác thì xóa phường/xã và nạp danh sách của tỉnh mới', async () => {
    stubServer();
    const onChangeSpy = vi.fn();
    await renderNode(<Harness onChangeSpy={onChangeSpy} />);
    await openAndChoose(provinceInput(), 'Hà Nội');
    await openAndChoose(wardInput(), 'Phường Hoàn Kiếm');
    await openAndChoose(provinceInput(), 'Cần Thơ');
    expect(onChangeSpy).toHaveBeenLastCalledWith({
      provinceCode: '1001',
      provinceName: 'Cần Thơ',
      wardCode: '',
      wardName: '',
      line: '',
    });
    expect(wardInput().value).toBe('');
    expect(wardRequestCount('1001')).toBe(1);
  });

  it('chọn lại đúng tỉnh/thành đang chọn thì không xóa phường/xã', async () => {
    stubServer();
    const onChangeSpy = vi.fn();
    await renderNode(
      <Harness
        onChangeSpy={onChangeSpy}
        initial={{
          provinceCode: '1003',
          provinceName: 'Hà Nội',
          wardCode: '3002',
          wardName: 'Phường Hoàn Kiếm',
          line: '5 Tràng Tiền',
        }}
      />,
    );
    await openAndChoose(provinceInput(), 'Hà Nội');
    expect(onChangeSpy).not.toHaveBeenCalled();
    expect(wardInput().value).toBe('Phường Hoàn Kiếm');
  });

  it('nhập số nhà gọi onChange và giữ phần còn lại', async () => {
    stubServer();
    const onChangeSpy = vi.fn();
    const initial: AddressValue = {
      provinceCode: '1003',
      provinceName: 'Hà Nội',
      wardCode: '3002',
      wardName: 'Phường Hoàn Kiếm',
      line: '',
    };
    await renderNode(<Harness onChangeSpy={onChangeSpy} initial={initial} />);
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!;
    await act(async () => {
      setter.call(lineInput(), '5 Tràng Tiền');
      lineInput().dispatchEvent(new Event('input', { bubbles: true }));
    });
    expect(onChangeSpy).toHaveBeenLastCalledWith({ ...initial, line: '5 Tràng Tiền' });
  });
});

describe('AddressPicker: tải danh sách', () => {
  it('đang tải phường/xã của tỉnh mới thì không hiện dòng của tỉnh trước', async () => {
    stubServer();
    await renderNode(<Harness />);
    await openAndChoose(provinceInput(), 'Hà Nội');
    // Đổi fetch sang lời hứa chưa giải quyết. Lời gọi của tỉnh tiếp theo nằm ở trạng thái đang tải.
    let release: () => void = () => {};
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    stubServer({ wardsGate: gate });
    await openAndChoose(provinceInput(), 'Cần Thơ');
    await act(async () => {
      wardInput().dispatchEvent(
        new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }),
      );
    });
    expect(wardOptionNames()).toEqual([]);
    expect(container.textContent).toContain('Đang tải');
    await act(async () => release());
    await settle();
    await act(async () => {
      wardInput().dispatchEvent(
        new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }),
      );
    });
    expect(wardOptionNames()).toEqual(['Phường Cái Khế', 'Phường Ninh Kiều']);
  });

  it('quay lại tỉnh/thành đã nạp thì dùng cache, không gọi lại route', async () => {
    stubServer();
    await renderNode(<Harness />);
    await openAndChoose(provinceInput(), 'Hà Nội');
    await openAndChoose(provinceInput(), 'Cần Thơ');
    await openAndChoose(provinceInput(), 'Hà Nội');
    expect(wardRequestCount('1003')).toBe(1);
  });

  it('lỗi tải tỉnh/thành hiện thông báo tiếng Việt và nút "Thử lại" trong khối địa chỉ', async () => {
    const server = stubServer({ provincesStatus: 500 });
    await renderNode(<Harness />);
    const alert = container.querySelector('[role="alert"]') as HTMLElement;
    expect(alert.textContent).toContain('Không tải được danh sách tỉnh/thành phố.');
    expect(alert.textContent).toContain('Máy chủ đang bận.');
    const retry = Array.from(container.querySelectorAll('button')).find(
      (button) => button.textContent === 'Thử lại',
    );
    expect(retry).toBeTruthy();

    server.provincesStatus = 200;
    await act(async () => retry!.click());
    await settle();
    expect(container.querySelector('[role="alert"]')).toBeNull();
    await act(async () => {
      provinceInput().dispatchEvent(
        new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }),
      );
    });
    expect(container.querySelectorAll('[role="option"]')).toHaveLength(3);
  });
});

describe('AddressPicker: lỗi theo từng ô', () => {
  it('lỗi hiện dưới đúng ô, ô sai có aria-invalid và aria-describedby', async () => {
    stubServer();
    await renderNode(
      <Harness
        errors={{
          province: 'Vui lòng chọn tỉnh/thành phố.',
          ward: 'Vui lòng chọn phường/xã.',
          line: 'Vui lòng nhập số nhà, tên đường.',
        }}
      />,
    );
    const province = provinceInput();
    const ward = wardInput();
    const line = lineInput();
    for (const [input, text] of [
      [province, 'Vui lòng chọn tỉnh/thành phố.'],
      [ward, 'Vui lòng chọn phường/xã.'],
      [line, 'Vui lòng nhập số nhà, tên đường.'],
    ] as const) {
      expect(input.getAttribute('aria-invalid')).toBe('true');
      const error = container.querySelector(`[id="${input.getAttribute('aria-describedby')}"]`);
      expect(error?.textContent).toBe(text);
      expect(error?.className).toBe('form-error');
    }
  });

  it('ô không có lỗi thì không có aria-invalid', async () => {
    stubServer();
    await renderNode(<Harness errors={{ ward: 'Vui lòng chọn phường/xã.' }} />);
    const province = provinceInput();
    const ward = wardInput();
    expect(province.getAttribute('aria-invalid')).toBeNull();
    expect(ward.getAttribute('aria-invalid')).toBe('true');
    expect(lineInput().getAttribute('aria-invalid')).toBeNull();
  });
});
