// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  CompareSpecValue,
  ComparedProduct,
  ProductCompareResponse,
} from '../../../../../src/features/catalog/customer/catalog-api';
import { ComparePage } from '../../../../../src/features/catalog/customer/compare-page';
import { compareListStore } from '../../../../../src/features/catalog/customer/use-compare-list';

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.localStorage.clear();
  // Kho danh sách sống suốt phiên, nên mỗi test nạp lại từ storage vừa xóa.
  compareListStore.reload();
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

function product(
  id: string,
  specs: Record<string, CompareSpecValue>,
  summary: Partial<ComparedProduct['summary']> = {},
): ComparedProduct {
  return {
    summary: {
      id,
      slug: `slug-${id}`,
      name: `Laptop ${id}`,
      brand: { id: 'b', name: 'ASUS', slug: 'asus' },
      minPrice: 15_000_000,
      inStock: true,
      status: 'active',
      image: null,
      ...summary,
    },
    specs,
  };
}

function response(products: ComparedProduct[]): ProductCompareResponse {
  return {
    category: { id: 'cat-1', name: 'Laptop', slug: 'laptop' },
    attributeGroups: [
      {
        name: 'Cấu hình',
        attributes: [
          {
            key: 'ram',
            label: 'RAM',
            group: 'Cấu hình',
            type: 'enum',
            unit: 'GB',
            options: ['8', '16'],
            required: false,
            filterable: true,
          },
          {
            key: 'os',
            label: 'Hệ điều hành',
            group: 'Cấu hình',
            type: 'text',
            unit: null,
            options: null,
            required: false,
            filterable: false,
          },
        ],
      },
    ],
    products,
  };
}

function stubFetch(status: number, body: unknown) {
  const fetchMock = vi.fn(async () => Response.json(body, { status }));
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

function seedList(ids: string[]) {
  for (const id of ids) compareListStore.add('cat-1', id);
}

async function renderPage() {
  // `retry: false` để lỗi hiện ngay, không chờ thử lại trong test.
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  await act(async () =>
    root.render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/compare']}>
          <Routes>
            <Route path="/compare" element={<ComparePage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    ),
  );
  await settle();
}

// Nhường vài vòng sự kiện để fetch, React Query và effect hoàn tất.
async function settle() {
  for (let turn = 0; turn < 6; turn += 1) {
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
  }
}

const twoProducts = () => [
  product('a', { ram: '8', os: 'Windows' }),
  product('b', { ram: '16', os: 'Windows' }),
];

const rowByLabel = (label: string) =>
  [...container.querySelectorAll('tbody tr')].find((row) =>
    row.querySelector('th[scope="row"]')?.textContent?.startsWith(label),
  );

describe('ComparePage: bảng so sánh', () => {
  it('gọi API với id theo đúng thứ tự trong danh sách', async () => {
    const fetchMock = stubFetch(200, { data: response(twoProducts()) });
    seedList(['a', 'b']);
    await renderPage();
    expect(fetchMock).toHaveBeenCalledWith('/api/products/compare?ids=a,b', expect.anything());
  });

  it('mỗi sản phẩm một cột, đầu cột có tên link, giá và nút bỏ khỏi so sánh', async () => {
    stubFetch(200, {
      data: response([
        product('a', { ram: '8', os: 'x' }),
        product('b', { ram: '16', os: 'x' }, { inStock: false, status: 'discontinued' }),
      ]),
    });
    seedList(['a', 'b']);
    await renderPage();
    const headers = [...container.querySelectorAll('thead th[scope="col"]')];
    // Cột đầu là cột tên thông số, hai cột sau là sản phẩm.
    expect(headers).toHaveLength(3);
    expect(headers[1]?.querySelector('a')?.getAttribute('href')).toBe('/p/slug-a');
    expect(headers[1]?.textContent).toContain('15.000.000 đ');
    expect(headers[2]?.textContent).toContain('Hết hàng');
    expect(headers[2]?.textContent).toContain('Ngừng kinh doanh');
    expect(headers[1]?.querySelector('button')?.textContent).toBe('Bỏ khỏi so sánh');
    expect(container.querySelector('caption.sr')).not.toBeNull();
  });

  it('dòng khác nhau có nhãn Khác nhau, dòng giống nhau không có', async () => {
    stubFetch(200, { data: response(twoProducts()) });
    seedList(['a', 'b']);
    await renderPage();
    const ramRow = rowByLabel('RAM');
    const osRow = rowByLabel('Hệ điều hành');
    expect(ramRow?.textContent).toContain('Khác nhau');
    expect(ramRow?.classList.contains('is-diff')).toBe(true);
    expect(osRow?.textContent).not.toContain('Khác nhau');
    expect(osRow?.classList.contains('is-diff')).toBe(false);
  });

  it('hiện Chưa có thông tin cho giá trị null', async () => {
    stubFetch(200, {
      data: response([
        product('a', { ram: '8', os: null }),
        product('b', { ram: '8', os: 'Linux' }),
      ]),
    });
    seedList(['a', 'b']);
    await renderPage();
    expect(rowByLabel('Hệ điều hành')?.textContent).toContain('Chưa có thông tin');
  });

  it('công tắc Chỉ hiện điểm khác nhau ẩn dòng giống nhau', async () => {
    stubFetch(200, { data: response(twoProducts()) });
    seedList(['a', 'b']);
    await renderPage();
    const toggle = container.querySelector<HTMLInputElement>('input[type="checkbox"]');
    expect(toggle?.closest('label')?.textContent).toContain('Chỉ hiện điểm khác nhau');
    expect(rowByLabel('Hệ điều hành')).toBeDefined();
    await act(async () => toggle?.click());
    expect(rowByLabel('Hệ điều hành')).toBeUndefined();
    expect(rowByLabel('RAM')).toBeDefined();
  });

  it('báo không có điểm khác nhau khi bật công tắc mà mọi dòng giống nhau', async () => {
    stubFetch(200, { data: response([product('a', { ram: '8', os: 'x' })]) });
    seedList(['a']);
    await renderPage();
    await act(async () =>
      container.querySelector<HTMLInputElement>('input[type="checkbox"]')?.click(),
    );
    expect(container.textContent).toContain('Không có điểm khác nhau');
  });

  it('bỏ một sản phẩm thì cột biến mất và danh sách đổi', async () => {
    stubFetch(200, { data: response(twoProducts()) });
    seedList(['a', 'b']);
    await renderPage();
    const removeButton = container.querySelector<HTMLButtonElement>('thead th[scope="col"] button');
    await act(async () => removeButton?.click());
    await settle();
    expect(container.querySelectorAll('thead th[scope="col"]')).toHaveLength(2);
    expect(container.textContent).not.toContain('Laptop a');
    expect(compareListStore.getSnapshot().productIds).toEqual(['b']);
  });
});

describe('ComparePage: danh sách và API lệch nhau', () => {
  it('bỏ id mà API không trả khỏi danh sách và hiện dòng thông báo', async () => {
    stubFetch(200, { data: response([product('a', { ram: '8' }), product('c', { ram: '16' })]) });
    seedList(['a', 'b', 'c']);
    await renderPage();
    expect(compareListStore.getSnapshot().productIds).toEqual(['a', 'c']);
    expect(container.querySelector('[role="status"]')?.textContent).toContain(
      '1 sản phẩm không còn hiển thị đã được bỏ khỏi danh sách.',
    );
  });

  it('hiện ErrorAlert và nút xóa danh sách khi API trả COMPARE_CATEGORY_MISMATCH', async () => {
    stubFetch(400, {
      error: {
        code: 'COMPARE_CATEGORY_MISMATCH',
        message: 'Chỉ so sánh được các sản phẩm cùng danh mục.',
      },
    });
    seedList(['a', 'b']);
    await renderPage();
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('cùng danh mục');
    const clearButton = [...container.querySelectorAll('button')].find(
      (button) => button.textContent === 'Xóa danh sách so sánh',
    );
    await act(async () => clearButton?.click());
    expect(compareListStore.getSnapshot().productIds).toEqual([]);
    expect(container.textContent).toContain('Chưa có sản phẩm nào để so sánh');
  });

  it('hiện ErrorAlert khi API trả lỗi khác', async () => {
    stubFetch(500, { error: { code: 'INTERNAL', message: 'Máy chủ gặp lỗi.' } });
    seedList(['a']);
    await renderPage();
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Máy chủ gặp lỗi.');
  });
});

describe('ComparePage: trạng thái rỗng và đang tải', () => {
  it('danh sách rỗng hiện lời nhắn kèm link về trang chủ và không gọi API', async () => {
    const fetchMock = stubFetch(200, { data: response([]) });
    await renderPage();
    expect(container.textContent).toContain('Chưa có sản phẩm nào để so sánh');
    expect(container.querySelector('a[href="/"]')).not.toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('hiện trạng thái đang tải trong lúc chờ dữ liệu', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => new Promise(() => {})),
    );
    seedList(['a']);
    await renderPage();
    expect(container.querySelector('[role="status"]')?.textContent).toContain('Đang tải');
    expect(container.querySelector('table')).toBeNull();
  });
});
