// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  CategoryNode,
  ProductListResponse,
} from '../../../../../src/features/catalog/customer/catalog-api';
import { CategoryPage } from '../../../../../src/features/catalog/customer/category-page';
import { COMPARE_STORAGE_KEY } from '../../../../../src/features/catalog/customer/compare-list';
import { compareListStore } from '../../../../../src/features/catalog/customer/use-compare-list';

let container: HTMLDivElement;
let root: Root;

const categories: CategoryNode[] = [
  {
    id: 'cat-phone',
    parentId: null,
    name: 'Điện thoại',
    slug: 'dien-thoai',
    children: [
      { id: 'cat-iphone', parentId: 'cat-phone', name: 'iPhone', slug: 'iphone', children: [] },
    ],
  },
];

const listData: ProductListResponse = {
  items: [
    {
      id: 'p-16',
      slug: 'iphone-16',
      name: 'iPhone 16',
      brand: { id: 'b-apple', name: 'Apple', slug: 'apple' },
      minPrice: 22_990_000,
      inStock: true,
      status: 'active',
      image: null,
    },
  ],
  page: 1,
  pageSize: 24,
  total: 1,
  facets: { brands: [], attributes: [] },
};

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.localStorage.clear();
  compareListStore.reload();
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input), 'http://localhost');
      if (url.pathname === '/api/categories') {
        return Response.json({ data: categories });
      }
      return Response.json({ data: listData });
    }),
  );
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  window.localStorage.clear();
  compareListStore.reload();
  vi.unstubAllGlobals();
});

async function settle() {
  for (let turn = 0; turn < 10; turn += 1) {
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
  }
}

async function renderCategory(path: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  await act(async () =>
    root.render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route path="/c/:slug" element={<CategoryPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    ),
  );
  await settle();
}

describe('CategoryPage: nút so sánh trên thẻ sản phẩm', () => {
  it('truyền categoryId khi xem danh mục lá và lưu đúng danh mục vào danh sách so sánh', async () => {
    await renderCategory('/c/iphone');

    const compareButton = container.querySelector<HTMLButtonElement>(
      'button[aria-label*="so sánh"]',
    )!;
    expect(compareButton).not.toBeNull();

    await act(async () => compareButton.click());
    expect(JSON.parse(window.localStorage.getItem(COMPARE_STORAGE_KEY) ?? 'null')).toEqual({
      categoryId: 'cat-iphone',
      productIds: ['p-16'],
    });
  });

  it('không hiện nút so sánh trên thẻ khi danh mục đang xem có danh mục con', async () => {
    await renderCategory('/c/dien-thoai');

    expect(container.querySelector('.product-card')).not.toBeNull();
    expect(container.querySelector('button[aria-label*="so sánh"]')).toBeNull();
  });
});
