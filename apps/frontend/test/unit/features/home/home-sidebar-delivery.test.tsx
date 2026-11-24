// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  BrandFacet,
  CategoryNode,
  ProductListItem,
  ProductListResponse,
} from '../../../../src/features/catalog/customer/catalog-api';
import { COMPARE_LIST_LIMIT } from '../../../../src/features/catalog/customer/compare-list';
import { HomePage } from '../../../../src/features/home/home-page';
import { buildNewestProductsPath } from '../../../../src/features/home/home-queries';

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  // Chỉ giả hẹn giờ lặp để fetch và Promise của React Query hoàn tất bình thường.
  vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

function category(slug: string, name: string): CategoryNode {
  return { id: `id-${slug}`, parentId: null, name, slug, children: [] };
}

function product(
  slug: string,
  minPrice: number | null,
  name = `Sản phẩm ${slug}`,
): ProductListItem {
  return {
    id: `p-${slug}`,
    slug,
    name,
    brand: { id: 'b-apple', name: 'Apple', slug: 'apple' },
    minPrice,
    inStock: true,
    status: 'active',
    image: null,
  };
}

const defaultCategories: CategoryNode[] = [
  category('dien-thoai', 'Điện thoại'),
  category('laptop', 'Laptop'),
];

const manyBrands: BrandFacet[] = [
  { slug: 'sony', name: 'Sony', count: 4 },
  { slug: 'apple', name: 'Apple', count: 12 },
  { slug: 'xiaomi', name: 'Xiaomi', count: 7 },
  { slug: 'samsung', name: 'Samsung', count: 10 },
  { slug: 'google', name: 'Google', count: 5 },
  { slug: 'oppo', name: 'OPPO', count: 6 },
  { slug: 'dell', name: 'Dell', count: 3 },
];

function listResponse(
  items: ProductListItem[],
  brands: BrandFacet[] = manyBrands,
): ProductListResponse {
  return {
    items,
    page: 1,
    pageSize: items.length,
    total: items.length,
    facets: { brands, attributes: [] },
  };
}

interface SidebarApiStub {
  categories?: CategoryNode[] | 'error';
  newestItems?: ProductListItem[] | 'error' | 'pending';
  overviewBrands?: BrandFacet[] | 'error' | 'pending';
}

function stubSidebarApi({
  categories = defaultCategories,
  newestItems = [
    product('oppo-reno12', 12_990_000, 'OPPO Reno12 5G'),
    product('lenovo-flex-5', 16_990_000, 'Lenovo IdeaPad Flex 5'),
    product('soundcore-4-nc', 2_290_000, 'Soundcore Liberty 4 NC'),
  ],
  overviewBrands = manyBrands,
}: SidebarApiStub) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input), 'http://localhost');
    const params = url.searchParams;

    if (url.pathname === '/api/categories') {
      if (categories === 'error') {
        return Response.json({ error: { code: 'ERROR', message: 'Lỗi.' } }, { status: 500 });
      }
      return Response.json({ data: categories });
    }

    if (url.pathname === '/api/product-details') {
      return Response.json({ data: [] });
    }

    // Request tổng quan của catalog lấy facet hãng và tổng sản phẩm.
    if (params.get('pageSize') === '1' && !params.get('category')) {
      if (overviewBrands === 'pending') return new Promise<Response>(() => {});
      if (overviewBrands === 'error') {
        return Response.json({ error: { code: 'ERROR', message: 'Lỗi.' } }, { status: 500 });
      }
      return Response.json({
        data: listResponse([product('tong-quan', 1_000_000)], overviewBrands),
      });
    }

    // Request sản phẩm mới toàn cửa hàng có sort=newest và pageSize=3, không lọc danh mục.
    if (
      params.get('sort') === 'newest' &&
      params.get('pageSize') === '3' &&
      !params.get('category') &&
      !params.get('brand')
    ) {
      if (newestItems === 'pending') return new Promise<Response>(() => {});
      if (newestItems === 'error') {
        return Response.json({ error: { code: 'ERROR', message: 'Lỗi.' } }, { status: 500 });
      }
      return Response.json({ data: listResponse(newestItems) });
    }

    return Response.json({ data: listResponse([product('mac-dinh', 5_000_000)]) });
  });

  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

async function settle() {
  for (let turn = 0; turn < 10; turn += 1) {
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
  }
}

async function renderHome() {
  // Tắt retry của React Query để trạng thái lỗi xuất hiện ngay trong test.
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  await act(async () =>
    root.render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <HomePage />
        </MemoryRouter>
      </QueryClientProvider>,
    ),
  );
  await settle();
}

function getSidebar(): HTMLElement {
  const sidebar = container.querySelector<HTMLElement>('aside[aria-label="Khám phá nhanh"]');
  if (!sidebar) throw new Error('Thiếu cột phải Khám phá nhanh.');
  return sidebar;
}

function findHeading(scope: ParentNode, text: string): HTMLHeadingElement | undefined {
  return [...scope.querySelectorAll<HTMLHeadingElement>('h2')].find(
    (heading) => heading.textContent?.trim() === text,
  );
}

function findLink(scope: ParentNode, text: string): HTMLAnchorElement | undefined {
  return [...scope.querySelectorAll<HTMLAnchorElement>('a')].find((link) =>
    link.textContent?.includes(text),
  );
}

describe('buildNewestProductsPath', () => {
  it('tạo đường dẫn lấy ba sản phẩm mới nhất của toàn cửa hàng', () => {
    expect(buildNewestProductsPath()).toBe('/api/products?sort=newest&pageSize=3');
  });
});

describe('HomeSidebar và DeliveryNotice', () => {
  it('hiển thị ba sản phẩm mới nhất kèm link chi tiết, giá và liên kết xem tất cả', async () => {
    stubSidebarApi({});
    await renderHome();

    const sidebar = getSidebar();
    const heading = findHeading(sidebar, 'Sản phẩm mới');
    expect(heading).toBeDefined();

    const newestSection = heading?.closest('section');
    if (!newestSection) throw new Error('Thiếu khối Sản phẩm mới.');

    const seeAllLink = findLink(newestSection, 'Xem tất cả');
    expect(seeAllLink?.getAttribute('href')).toBe('/c/dien-thoai');

    const oppoLink = findLink(newestSection, 'OPPO Reno12 5G');
    expect(oppoLink?.getAttribute('href')).toBe('/p/oppo-reno12');
    expect(oppoLink?.textContent).toContain('12.990.000 đ');

    const lenovoLink = findLink(newestSection, 'Lenovo IdeaPad Flex 5');
    expect(lenovoLink?.getAttribute('href')).toBe('/p/lenovo-flex-5');
    expect(lenovoLink?.textContent).toContain('16.990.000 đ');

    const soundcoreLink = findLink(newestSection, 'Soundcore Liberty 4 NC');
    expect(soundcoreLink?.getAttribute('href')).toBe('/p/soundcore-4-nc');
    expect(soundcoreLink?.textContent).toContain('2.290.000 đ');
  });

  it('hiển thị "Liên hệ" khi sản phẩm mới chưa có giá', async () => {
    stubSidebarApi({
      newestItems: [product('chua-co-gia', null, 'Sản phẩm sắp về')],
    });
    await renderHome();

    const sidebar = getSidebar();
    const itemLink = findLink(sidebar, 'Sản phẩm sắp về');
    expect(itemLink?.textContent).toContain('Liên hệ');
  });

  it('ẩn thẻ sản phẩm mới khi API lỗi hoặc danh sách rỗng', async () => {
    stubSidebarApi({ newestItems: 'error' });
    await renderHome();

    expect(findHeading(getSidebar(), 'Sản phẩm mới')).toBeUndefined();

    stubSidebarApi({ newestItems: [] });
    await renderHome();

    expect(findHeading(getSidebar(), 'Sản phẩm mới')).toBeUndefined();
  });

  it('hiển thị tối đa 6 hãng theo thứ tự giảm dần số sản phẩm và dẫn tới bộ lọc hãng', async () => {
    stubSidebarApi({});
    await renderHome();

    const sidebar = getSidebar();
    const brandsNav = sidebar.querySelector<HTMLElement>('nav[aria-label="Mua theo thương hiệu"]');
    if (!brandsNav) throw new Error('Thiếu điều hướng Mua theo thương hiệu.');

    const brandLinks = [...brandsNav.querySelectorAll('a')];
    expect(brandLinks.map((link) => link.textContent?.trim())).toEqual([
      'Apple',
      'Samsung',
      'Xiaomi',
      'OPPO',
      'Google',
      'Sony',
    ]);
    expect(brandLinks.map((link) => link.getAttribute('href'))).toEqual([
      '/c/dien-thoai?brand=apple',
      '/c/dien-thoai?brand=samsung',
      '/c/dien-thoai?brand=xiaomi',
      '/c/dien-thoai?brand=oppo',
      '/c/dien-thoai?brand=google',
      '/c/dien-thoai?brand=sony',
    ]);
  });

  it('ẩn thẻ thương hiệu khi API lỗi hoặc danh sách hãng rỗng', async () => {
    stubSidebarApi({ overviewBrands: 'error' });
    await renderHome();

    expect(findHeading(getSidebar(), 'Mua theo thương hiệu')).toBeUndefined();

    stubSidebarApi({ overviewBrands: [] });
    await renderHome();

    expect(findHeading(getSidebar(), 'Mua theo thương hiệu')).toBeUndefined();
  });

  it('hiển thị thẻ gợi ý so sánh với giới hạn từ COMPARE_LIST_LIMIT và link tới /compare', async () => {
    stubSidebarApi({});
    await renderHome();

    const compareLink = findLink(
      getSidebar(),
      `So sánh tối đa ${COMPARE_LIST_LIMIT} sản phẩm cạnh nhau`,
    );
    expect(compareLink?.getAttribute('href')).toBe('/compare');
  });

  it('hiển thị dải thông báo phí giao hàng với tiêu đề, mô tả và nút dẫn tới /policy/giao-hang', async () => {
    stubSidebarApi({});
    await renderHome();

    const noticeHeading = findHeading(container, 'Mua online, nhận hàng tận nơi');
    const noticeSection = noticeHeading?.closest('section');
    if (!noticeSection) throw new Error('Thiếu dải thông báo phí giao hàng.');

    expect(noticeSection.textContent).toContain(
      'Phí giao hàng và thời hạn bảo hành được ghi rõ trước khi đặt.',
    );

    const policyLink = findLink(noticeSection, 'Xem phí giao hàng');
    expect(policyLink?.getAttribute('href')).toBe('/policy/giao-hang');
  });
});
