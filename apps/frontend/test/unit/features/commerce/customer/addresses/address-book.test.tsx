// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AddressBook } from '../../../../../../src/features/commerce/customer/addresses/address-book';

let container: HTMLDivElement;
let root: Root;

interface Call {
  method: string;
  path: string;
  body: unknown;
}

let calls: Call[];
let book: Record<string, unknown>[];

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  vi.stubGlobal('confirm', () => true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  calls = [];
  book = [];
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

function address(id: string, overrides: Record<string, unknown> = {}) {
  return {
    id,
    label: `Nhãn ${id}`,
    name: 'Nguyễn Văn An',
    phone: '0912345678',
    isDefault: false,
    address: {
      provinceCode: '201',
      provinceName: 'Hà Nội',
      wardCode: '1A0101',
      wardName: 'Phường Ba Đình',
      line: `${id} Phố Test`,
    },
    ...overrides,
  };
}

interface Reply {
  status: number;
  body: unknown;
}

// API giả: sổ địa chỉ giữ trong bộ nhớ. Test thay phản hồi ghi bằng `onWrite`.
function stubApi(onWrite?: (call: Call) => Reply | null) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = String(input);
      const method = (init?.method ?? 'GET').toUpperCase();
      const body = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined;
      const call = { method, path, body };
      calls.push(call);
      if (path === '/api/addresses/provinces') {
        return Response.json({ data: [{ code: '201', name: 'Hà Nội' }] });
      }
      if (path.startsWith('/api/addresses/wards')) {
        return Response.json({ data: [{ code: '1A0101', name: 'Phường Ba Đình' }] });
      }
      if (path === '/api/me/addresses' && method === 'GET') {
        return Response.json({ data: book });
      }
      const reply = onWrite?.(call);
      if (reply) return Response.json(reply.body, { status: reply.status });
      if (method === 'DELETE') return new Response(null, { status: 204 });
      return Response.json({ data: address('moi') }, { status: method === 'POST' ? 201 : 200 });
    }),
  );
}

async function settle() {
  for (let turn = 0; turn < 5; turn += 1) {
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
  }
}

async function renderBook() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  await act(async () =>
    root.render(
      <QueryClientProvider client={client}>
        <MemoryRouter>
          <AddressBook />
        </MemoryRouter>
      </QueryClientProvider>,
    ),
  );
  await settle();
}

function rowOf(label: string) {
  return [...container.querySelectorAll('li')].find((item) => item.textContent?.includes(label))!;
}

function buttonIn(parent: ParentNode, label: string) {
  return [...parent.querySelectorAll('button')].find(
    (button) => button.textContent?.trim() === label,
  ) as HTMLButtonElement | undefined;
}

function dialog() {
  return container.querySelector('[role="dialog"]');
}

async function click(element: HTMLElement) {
  await act(async () => element.click());
  await settle();
}

function setValue(element: HTMLInputElement | HTMLTextAreaElement, value: string) {
  const prototype =
    element instanceof HTMLTextAreaElement
      ? HTMLTextAreaElement.prototype
      : HTMLInputElement.prototype;
  Object.getOwnPropertyDescriptor(prototype, 'value')!.set!.call(element, value);
  element.dispatchEvent(new Event('input', { bubbles: true }));
}

async function typeInto(selector: string, value: string) {
  const element = dialog()!.querySelector<HTMLInputElement>(selector)!;
  await act(async () => setValue(element, value));
}

async function chooseOption(inputId: string, typed: string, optionName: string) {
  const input = dialog()!.querySelector<HTMLInputElement>(`#${inputId}`)!;
  await act(async () => {
    input.focus();
    setValue(input, typed);
  });
  const option = [...container.querySelectorAll<HTMLElement>('li[role="option"]')].find(
    (item) => item.textContent === optionName,
  )!;
  await act(async () => option.click());
  await settle();
}

async function fillDialog() {
  await typeInto('#book-label', 'Công ty');
  await typeInto('#book-name', 'Trần Thị Bình');
  await typeInto('#book-phone', '0987654321');
  await chooseOption('book-province', 'ha noi', 'Hà Nội');
  await chooseOption('book-ward', 'ba dinh', 'Phường Ba Đình');
  await typeInto('#book-line', '99 Đường Công Ty');
}

describe('AddressBook: danh sách', () => {
  it('hiện badge Mặc định đúng dòng và nút Đặt làm mặc định chỉ ở dòng chưa mặc định', async () => {
    book = [address('a', { isDefault: true }), address('b')];
    stubApi();
    await renderBook();

    expect(rowOf('Nhãn a').textContent).toContain('Mặc định');
    expect(buttonIn(rowOf('Nhãn a'), 'Đặt làm mặc định')).toBeUndefined();
    expect(rowOf('Nhãn b').textContent).not.toContain('Mặc định');
    expect(buttonIn(rowOf('Nhãn b'), 'Đặt làm mặc định')).toBeDefined();
  });

  it('mỗi dòng hiện tên, số điện thoại và địa chỉ một dòng', async () => {
    book = [address('a')];
    stubApi();
    await renderBook();

    const text = rowOf('Nhãn a').textContent ?? '';
    expect(text).toContain('Nguyễn Văn An');
    expect(text).toContain('0912345678');
    expect(text).toContain('a Phố Test, Phường Ba Đình, Hà Nội');
  });

  it('sổ rỗng hiện thông báo và nút thêm địa chỉ', async () => {
    stubApi();
    await renderBook();

    expect(container.textContent).toContain('Bạn chưa lưu địa chỉ nào.');
    expect(buttonIn(container, 'Thêm địa chỉ')!.disabled).toBe(false);
  });

  it('đủ 10 địa chỉ thì nút thêm tắt và có dòng giải thích', async () => {
    book = Array.from({ length: 10 }, (_, index) => address(`d${index}`));
    stubApi();
    await renderBook();

    expect(buttonIn(container, 'Thêm địa chỉ')!.disabled).toBe(true);
    expect(container.textContent).toContain('Đã đủ 10 địa chỉ');
  });

  it('9 địa chỉ thì còn thêm được', async () => {
    book = Array.from({ length: 9 }, (_, index) => address(`d${index}`));
    stubApi();
    await renderBook();

    expect(buttonIn(container, 'Thêm địa chỉ')!.disabled).toBe(false);
  });
});

describe('AddressBook: thêm, sửa, xóa, đặt mặc định', () => {
  it('thêm địa chỉ gửi đúng body, đóng hộp thoại và tải lại danh sách', async () => {
    stubApi();
    await renderBook();
    const readsBefore = calls.filter(
      (call) => call.method === 'GET' && call.path === '/api/me/addresses',
    ).length;

    await click(buttonIn(container, 'Thêm địa chỉ')!);
    await fillDialog();
    await click(buttonIn(dialog()!, 'Lưu địa chỉ')!);

    const post = calls.find((call) => call.method === 'POST')!;
    expect(post.path).toBe('/api/me/addresses');
    expect(post.body).toEqual({
      label: 'Công ty',
      name: 'Trần Thị Bình',
      phone: '0987654321',
      address: { provinceCode: '201', wardCode: '1A0101', line: '99 Đường Công Ty' },
      isDefault: false,
    });
    expect(dialog()).toBeNull();
    const readsAfter = calls.filter(
      (call) => call.method === 'GET' && call.path === '/api/me/addresses',
    ).length;
    expect(readsAfter).toBeGreaterThan(readsBefore);
  });

  it('lỗi 400 theo trường hiện dưới đúng ô và giữ hộp thoại mở', async () => {
    stubApi((call) =>
      call.method === 'POST'
        ? {
            status: 400,
            body: {
              error: {
                code: 'VALIDATION_ERROR',
                message: 'Dữ liệu không hợp lệ.',
                details: [{ key: 'phone', message: 'Số điện thoại không đúng định dạng.' }],
              },
            },
          }
        : null,
    );
    await renderBook();

    await click(buttonIn(container, 'Thêm địa chỉ')!);
    await fillDialog();
    await click(buttonIn(dialog()!, 'Lưu địa chỉ')!);

    expect(dialog()).not.toBeNull();
    const error = dialog()!.querySelector('#book-phone-error');
    expect(error?.textContent).toBe('Số điện thoại không đúng định dạng.');
  });

  it('form thiếu ô bắt buộc hiện lỗi và không gọi API', async () => {
    stubApi();
    await renderBook();

    await click(buttonIn(container, 'Thêm địa chỉ')!);
    await click(buttonIn(dialog()!, 'Lưu địa chỉ')!);

    expect(calls.some((call) => call.method === 'POST')).toBe(false);
    expect(dialog()!.textContent).toContain('Cần nhập nhãn địa chỉ.');
  });

  it('lỗi 422 đủ giới hạn hiện thông báo của API trong hộp thoại', async () => {
    const message = 'Mỗi tài khoản lưu tối đa 10 địa chỉ.';
    stubApi((call) =>
      call.method === 'POST'
        ? { status: 422, body: { error: { code: 'ADDRESS_LIMIT_REACHED', message } } }
        : null,
    );
    await renderBook();

    await click(buttonIn(container, 'Thêm địa chỉ')!);
    await fillDialog();
    await click(buttonIn(dialog()!, 'Lưu địa chỉ')!);

    expect(dialog()!.querySelector('[role="alert"]')!.textContent).toContain(message);
  });

  it('sửa địa chỉ điền sẵn form và gửi PATCH tới đúng id', async () => {
    book = [address('a', { isDefault: true })];
    stubApi();
    await renderBook();

    await click(buttonIn(rowOf('Nhãn a'), 'Sửa')!);
    expect(dialog()!.querySelector<HTMLInputElement>('#book-label')!.value).toBe('Nhãn a');
    expect(dialog()!.querySelector<HTMLInputElement>('#book-line')!.value).toBe('a Phố Test');

    await typeInto('#book-label', 'Nhà riêng');
    await click(buttonIn(dialog()!, 'Lưu địa chỉ')!);

    const patch = calls.find((call) => call.method === 'PATCH')!;
    expect(patch.path).toBe('/api/me/addresses/a');
    expect((patch.body as Record<string, unknown>).label).toBe('Nhà riêng');
  });

  it('Đặt làm mặc định gửi PATCH với isDefault true', async () => {
    book = [address('a', { isDefault: true }), address('b')];
    stubApi();
    await renderBook();

    await click(buttonIn(rowOf('Nhãn b'), 'Đặt làm mặc định')!);

    const patch = calls.find((call) => call.method === 'PATCH')!;
    expect(patch.path).toBe('/api/me/addresses/b');
    expect(patch.body).toEqual({ isDefault: true });
  });

  it('xóa cần xác nhận rồi gửi DELETE và tải lại danh sách', async () => {
    book = [address('a'), address('b')];
    stubApi();
    await renderBook();
    const readsBefore = calls.filter(
      (call) => call.method === 'GET' && call.path === '/api/me/addresses',
    ).length;

    await click(buttonIn(rowOf('Nhãn b'), 'Xóa')!);
    expect(dialog()).not.toBeNull();
    expect(calls.some((call) => call.method === 'DELETE')).toBe(false);

    await click(buttonIn(dialog()!, 'Xóa địa chỉ')!);

    const remove = calls.find((call) => call.method === 'DELETE')!;
    expect(remove.path).toBe('/api/me/addresses/b');
    const readsAfter = calls.filter(
      (call) => call.method === 'GET' && call.path === '/api/me/addresses',
    ).length;
    expect(readsAfter).toBeGreaterThan(readsBefore);
  });

  it('hủy hộp thoại xóa thì không gọi API', async () => {
    book = [address('a')];
    stubApi();
    await renderBook();

    await click(buttonIn(rowOf('Nhãn a'), 'Xóa')!);
    await click(buttonIn(dialog()!, 'Hủy')!);

    expect(dialog()).toBeNull();
    expect(calls.some((call) => call.method === 'DELETE')).toBe(false);
  });
});
