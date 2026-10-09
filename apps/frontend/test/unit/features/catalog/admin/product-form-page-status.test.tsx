// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ProductFormPage } from '../../../../../src/features/catalog/admin/product-form';
import type { ActorData } from '../../../../../src/features/identity/hooks/use-actor';

let container: HTMLDivElement;
let root: Root;

const readerActor: ActorData = {
  anonymousId: 'anon',
  userId: 'user-1',
  kind: 'staff',
  roles: ['sales'],
  permissions: ['products:read'],
  consent: { personalization: false, marketing: false },
};
const writerActor: ActorData = {
  ...readerActor,
  permissions: ['products:read', 'products:write'],
};

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

// Chờ truy vấn và render xong. React Query cần vài lượt hàng đợi vi tác vụ sau khi fetch trả về.
async function settle() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status });
}

function errorResponse(code: string, message: string, status: number, details?: unknown) {
  return jsonResponse({ error: { code, message, details } }, status);
}

interface ServerOptions {
  // Trả lỗi này cho lần đổi trạng thái thay vì thành công.
  statusFailure?: Response;
  // Trả lỗi này cho lần xóa thay vì 204.
  deleteFailure?: Response;
  // Lỗi đọc sản phẩm phải hiện khung lỗi và đường về danh sách thay vì chặn trang.
  productFailure?: Response;
  // Sau khi đổi trạng thái, mọi lần đọc lại không bao giờ trả về. Chứng minh nhãn đổi nhờ ghi cache.
  hangReadsAfterStatusChange?: boolean;
}

// Máy chủ giả giữ trạng thái: sau khi đổi trạng thái, các lần đọc kế tiếp thấy dữ liệu mới như API thật.
function createServer(options: ServerOptions = {}) {
  const product = {
    id: 'p1',
    categoryId: 'cat',
    brandId: 'brand',
    name: 'MacBook Air M3',
    shortDesc: '',
    description: { type: 'doc', content: [{ type: 'paragraph' }] },
    specs: {},
    options: [],
    variants: [
      {
        id: 'sku-1',
        code: 'MAC-01',
        optionValues: {},
        price: 25000000,
        stock: 3,
        enabled: true,
        weightGrams: 1200,
        lengthCm: 30,
        widthCm: 21,
        heightCm: 2,
      },
    ],
    warrantyMonths: 12,
    status: 'draft',
    minPrice: 25000000,
    images: [],
  };
  const state = { product, statusChanged: false };
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    const url = new URL(input, 'http://localhost');
    const method = init?.method ?? 'GET';
    if (state.statusChanged && options.hangReadsAfterStatusChange && method === 'GET') {
      return new Promise<Response>(() => {});
    }
    if (url.pathname === '/api/admin/categories') {
      return jsonResponse({
        data: [{ id: 'cat', parentId: null, name: 'Laptop', attributes: [] }],
      });
    }
    if (url.pathname === '/api/brands') {
      return jsonResponse({ data: [{ id: 'brand', name: 'Apple' }] });
    }
    if (url.pathname === '/api/admin/products/p1' && method === 'GET') {
      if (options.productFailure) return options.productFailure;
      return jsonResponse({ data: state.product });
    }
    if (url.pathname === '/api/admin/products/p1' && method === 'PATCH') {
      const body = JSON.parse(init!.body as string) as { name: string };
      state.product = { ...state.product, name: body.name };
      return jsonResponse({ data: state.product });
    }
    if (url.pathname === '/api/admin/products/p1/status' && method === 'POST') {
      if (options.statusFailure) return options.statusFailure;
      const body = JSON.parse(init!.body as string) as { status: string };
      state.product = { ...state.product, status: body.status };
      state.statusChanged = true;
      return jsonResponse({ data: state.product });
    }
    if (url.pathname === '/api/admin/products/p1' && method === 'DELETE') {
      return options.deleteFailure ?? new Response(null, { status: 204 });
    }
    return errorResponse('NOT_FOUND', 'Không có route giả.', 404);
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

async function renderPage(actor: ActorData, path = '/admin/products/p1') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  queryClient.setQueryData(['actor'], actor);
  await act(async () =>
    root.render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route path="/admin/products/new" element={<ProductFormPage />} />
            <Route path="/admin/products/:id" element={<ProductFormPage />} />
            <Route path="/admin/products" element={<h1>Danh sách sản phẩm giả</h1>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    ),
  );
  await settle();
  return queryClient;
}

function buttonByName(name: string): HTMLButtonElement | undefined {
  return [...document.querySelectorAll('button')].find((button) => button.textContent === name);
}

async function click(button: HTMLButtonElement | undefined) {
  await act(async () => button!.click());
  await settle();
}

async function typeInto(selector: string, value: string) {
  const element = document.querySelector<HTMLInputElement>(selector)!;
  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(element, value);
    element.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

const statusLabel = () => document.querySelector('.status-current .badge')?.textContent;
const postsToStatus = (fetchMock: ReturnType<typeof createServer>) =>
  fetchMock.mock.calls.filter(
    ([input, init]) => String(input).endsWith('/status') && init?.method === 'POST',
  );

describe('ProductFormPage: lỗi tải sản phẩm', () => {
  it('hiện khung lỗi có icon và liên kết về danh sách khi API trả lỗi', async () => {
    createServer({ productFailure: errorResponse('PRODUCT_FAILURE', 'Máy chủ tạm ngừng.', 500) });
    await renderPage(writerActor);

    const page = container.querySelector('.page')!;
    const alert = page.querySelector<HTMLElement>('[role="alert"]')!;
    expect(alert.classList.contains('alert-list')).toBe(true);
    expect(alert.querySelector('svg.alert-list-icon')).not.toBeNull();
    expect(alert.textContent).toContain('Máy chủ tạm ngừng.');
    const returnLink = page.querySelector<HTMLAnchorElement>('a[href="/admin/products"]')!;
    expect(returnLink.textContent).toBe('Về danh sách');
    await act(async () => returnLink.click());
    expect(container.textContent).toContain('Danh sách sản phẩm giả');
  });
});

describe('ProductFormPage: khối trạng thái', () => {
  it('trang sửa hiện khối trạng thái, trang tạo mới thì không', async () => {
    createServer();
    await renderPage(writerActor);
    expect(statusLabel()).toBe('Bản nháp');
    await act(async () => root.unmount());
    root = createRoot(container);

    await renderPage(writerActor, '/admin/products/new');
    expect(document.querySelector('.status-panel')).toBeNull();
    expect(buttonByName('Xóa sản phẩm')).toBeUndefined();
  });

  it('đăng bán thành công thì nhãn đổi ngay, có thông báo, không tải lại trang', async () => {
    // Lần đọc lại không trả về, nên nhãn chỉ đổi được nếu phản hồi của lệnh đổi trạng thái được ghi vào cache.
    const fetchMock = createServer({ hangReadsAfterStatusChange: true });
    const reload = vi.fn();
    vi.stubGlobal('location', { ...window.location, reload });
    const queryClient = await renderPage(writerActor);

    await click(buttonByName('Đăng bán'));

    expect(statusLabel()).toBe('Đang bán');
    expect(document.querySelector('[role="status"]')?.textContent).toBe(
      'Đã đổi trạng thái thành "Đang bán".',
    );
    expect(reload).not.toHaveBeenCalled();
    const calls = postsToStatus(fetchMock);
    expect(calls).toHaveLength(1);
    expect(JSON.parse(calls[0]![1]!.body as string)).toEqual({ status: 'active' });
    expect(queryClient.getQueryData<{ status: string }>(['admin-product', 'p1'])?.status).toBe(
      'active',
    );
    // Từ đang bán chỉ còn nút ẩn và ngừng kinh doanh.
    expect(buttonByName('Đăng bán')).toBeUndefined();
    expect(buttonByName('Ẩn sản phẩm')).toBeDefined();
  });

  it('đổi trạng thái thành công thì làm mới danh sách sản phẩm', async () => {
    createServer();
    const queryClient = await renderPage(writerActor);
    queryClient.setQueryData(['admin-products', '', '', '', false, 1], { items: [], total: 0 });

    await click(buttonByName('Đăng bán'));

    expect(queryClient.getQueryState(['admin-products', '', '', '', false, 1])?.isInvalidated).toBe(
      true,
    );
  });

  it('lỗi 422 hiện từng lý do và nhãn vẫn là Bản nháp', async () => {
    createServer({
      statusFailure: errorResponse(
        'PRODUCT_NOT_PUBLISHABLE',
        'Sản phẩm chưa đủ điều kiện để đăng bán.',
        422,
        ['Sản phẩm phải có ít nhất 1 ảnh để đăng bán.', 'SKU MAC-01: thiếu giá bán.'],
      ),
    });
    await renderPage(writerActor);

    await click(buttonByName('Đăng bán'));

    const alert = document.querySelector('.status-panel [role="alert"]')!;
    expect(alert.textContent).toContain('Sản phẩm chưa đủ điều kiện để đăng bán.');
    expect([...alert.querySelectorAll('li')].map((item) => item.textContent)).toEqual([
      'Sản phẩm phải có ít nhất 1 ảnh để đăng bán.',
      'SKU MAC-01: thiếu giá bán.',
    ]);
    expect(statusLabel()).toBe('Bản nháp');
  });

  it('có thay đổi chưa lưu thì khóa nút trạng thái, lưu xong thì mở lại', async () => {
    const fetchMock = createServer();
    await renderPage(writerActor);
    expect(buttonByName('Đăng bán')!.disabled).toBe(false);

    await typeInto('#product-name', 'MacBook Air M4');
    expect(buttonByName('Đăng bán')!.disabled).toBe(true);
    expect(document.body.textContent).toContain('Lưu thay đổi trước khi đổi trạng thái.');
    await click(buttonByName('Đăng bán'));
    expect(postsToStatus(fetchMock)).toHaveLength(0);

    await click(buttonByName('Lưu sản phẩm'));
    expect(buttonByName('Đăng bán')!.disabled).toBe(false);
    expect(document.body.textContent).not.toContain('Lưu thay đổi trước');
  });

  it('sửa rồi gõ lại đúng giá trị cũ thì không còn thay đổi chưa lưu', async () => {
    createServer();
    await renderPage(writerActor);
    await typeInto('#product-name', 'MacBook Air M4');
    await typeInto('#product-name', 'MacBook Air M3');
    expect(buttonByName('Đăng bán')!.disabled).toBe(false);
  });

  it('thiếu products:write thì thấy nhãn nhưng không có nút trạng thái và nút xóa', async () => {
    createServer();
    await renderPage(readerActor);
    expect(statusLabel()).toBe('Bản nháp');
    expect(buttonByName('Đăng bán')).toBeUndefined();
    expect(buttonByName('Xóa sản phẩm')).toBeUndefined();
  });
});

describe('ProductFormPage: xóa sản phẩm', () => {
  async function confirmDelete() {
    await click(buttonByName('Xóa sản phẩm'));
    const confirm = [...document.querySelectorAll('[role="dialog"] button')].find(
      (button) => button.textContent === 'Xóa vĩnh viễn',
    ) as HTMLButtonElement;
    await click(confirm);
  }

  const deleteCalls = (fetchMock: ReturnType<typeof createServer>) =>
    fetchMock.mock.calls.filter(([, init]) => init?.method === 'DELETE');

  it('hủy thì không gọi API xóa', async () => {
    const fetchMock = createServer();
    await renderPage(writerActor);
    await click(buttonByName('Xóa sản phẩm'));
    await click(buttonByName('Hủy'));
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    expect(deleteCalls(fetchMock)).toHaveLength(0);
  });

  it('xác nhận thì gọi DELETE, làm mới danh sách và chuyển về /admin/products', async () => {
    const fetchMock = createServer();
    const queryClient = await renderPage(writerActor);
    queryClient.setQueryData(['admin-products', '', '', '', false, 1], { items: [], total: 0 });

    await confirmDelete();

    expect(deleteCalls(fetchMock)).toHaveLength(1);
    expect(document.querySelector('h1')?.textContent).toBe('Danh sách sản phẩm giả');
    expect(queryClient.getQueryState(['admin-products', '', '', '', false, 1])?.isInvalidated).toBe(
      true,
    );
  });

  it('lỗi 409 PRODUCT_HAS_ORDERS hiện trong hộp thoại, không chuyển trang', async () => {
    createServer({
      deleteFailure: errorResponse(
        'PRODUCT_HAS_ORDERS',
        'Sản phẩm đã có đơn hàng nên không xóa được.',
        409,
      ),
    });
    await renderPage(writerActor);

    await confirmDelete();

    const dialog = document.querySelector('[role="dialog"]')!;
    expect(dialog.querySelector('[role="alert"]')?.textContent).toBe(
      'Sản phẩm đã có đơn hàng nên không xóa được.',
    );
    expect(statusLabel()).toBe('Bản nháp');
    expect(document.querySelector('h1')?.textContent).not.toBe('Danh sách sản phẩm giả');
  });
});
