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
import { HomePage } from '../../../../src/features/home/home-page';

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  // Chỉ giả hẹn giờ lặp của carousel để fetch và React Query chạy bình thường.
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

function category(slug: string, name: string, withChild = true): CategoryNode {
  return {
    id: `id-${slug}`,
    parentId: null,
    name,
    slug,
    children: withChild
      ? [
          {
            id: `id-${slug}-child`,
            parentId: `id-${slug}`,
            name: `${name} con`,
            slug: `${slug}-con`,
            children: [],
          },
        ]
      : [],
  };
}

const fiveCategories = [
  category('dien-thoai', 'Điện thoại'),
  category('laptop', 'Laptop'),
  category('man-hinh', 'Màn hình'),
  category('tai-nghe', 'Tai nghe'),
  category('phu-kien', 'Phụ kiện'),
];

function product(
  slug: string,
  minPrice: number | null,
  imageUrl:
    string | null = `https://res.cloudinary.com/pickory/image/upload/pickory/products/${slug}.jpg`,
): ProductListItem {
  return {
    id: `p-${slug}`,
    slug,
    name: `Sản phẩm ${slug}`,
    brand: { id: 'b-apple', name: 'Apple', slug: 'apple' },
    minPrice,
    inStock: true,
    status: 'active',
    image: imageUrl
      ? {
          publicId: `pickory/products/${slug}`,
          alt: `Sản phẩm ${slug}`,
          url: imageUrl,
        }
      : null,
  };
}

const tenBrands: BrandFacet[] = [
  { slug: 'sony', name: 'Sony', count: 4 },
  { slug: 'apple', name: 'Apple', count: 15 },
  { slug: 'dell', name: 'Dell', count: 6 },
  { slug: 'samsung', name: 'Samsung', count: 14 },
  { slug: 'xiaomi', name: 'Xiaomi', count: 12 },
  { slug: 'oppo', name: 'OPPO', count: 10 },
  { slug: 'google', name: 'Google', count: 8 },
  { slug: 'asus', name: 'ASUS', count: 5 },
  { slug: 'anker', name: 'Anker', count: 2 },
  { slug: 'jbl', name: 'JBL', count: 1 },
];

function listResponse(
  items: ProductListItem[],
  { total = items.length, brands = tenBrands } = {},
): ProductListResponse {
  return { items, page: 1, pageSize: items.length, total, facets: { brands, attributes: [] } };
}

function errorResponse(status: number) {
  return Response.json({ error: { code: 'ERROR', message: 'Lỗi.' } }, { status });
}

interface FakeHomeApi {
  categories?: CategoryNode[];
  categoryItems?: Record<string, ProductListItem[] | 'error'>;
  categoryBrands?: Record<string, BrandFacet[] | 'error'>;
  overviewBrands?: BrandFacet[] | 'error';
}

function stubApi({
  categories = fiveCategories,
  categoryItems = {},
  categoryBrands = {},
  overviewBrands = tenBrands,
}: FakeHomeApi) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input), 'http://localhost');
    const params = url.searchParams;

    if (url.pathname === '/api/categories') {
      return Response.json({ data: categories });
    }

    if (url.pathname === '/api/product-details') {
      return Response.json({ data: [] });
    }

    const categorySlug = params.get('category');
    if (categorySlug !== null) {
      // Summary query có pageSize=1 và sort=price_asc.
      if (params.get('pageSize') === '1') {
        const brandConfig = categoryBrands[categorySlug];
        if (brandConfig === 'error') return errorResponse(500);
        const items = categoryItems[categorySlug];
        const safeItems = Array.isArray(items) ? items : [product(`${categorySlug}-1`, 5_990_000)];
        return Response.json({
          data: listResponse(safeItems.slice(0, 1), {
            total: safeItems.length,
            brands: brandConfig ?? tenBrands,
          }),
        });
      }

      const items = categoryItems[categorySlug] ?? [product(`${categorySlug}-1`, 5_990_000)];
      if (items === 'error') return errorResponse(500);
      return Response.json({ data: listResponse(items) });
    }

    // Tổng quan cửa hàng lấy facet hãng với pageSize=1.
    if (params.get('pageSize') === '1') {
      if (overviewBrands === 'error') return errorResponse(500);
      return Response.json({
        data: listResponse([product('tong-quan', 1_000_000)], {
          total: 40,
          brands: overviewBrands,
        }),
      });
    }

    return Response.json({ data: listResponse([product('tat-ca-1', 1_500_000)], { total: 1 }) });
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

async function renderPage() {
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

async function click(element: Element | null | undefined) {
  await act(async () => (element as HTMLElement | null)?.click());
  await settle();
}

function sectionHeadings(): string[] {
  return [...container.querySelectorAll('main h2')]
    .filter((heading) => !heading.closest('.banner-carousel'))
    .map((heading) => heading.textContent?.trim() ?? '');
}

describe('HomePage: năm khối danh mục và thương hiệu nổi bật', () => {
  it('hiển thị đúng thứ tự các khối trên trang chủ', async () => {
    stubApi({});
    await renderPage();

    expect(sectionHeadings()).toEqual([
      'Sản phẩm mới',
      'Mua theo thương hiệu',
      'Mua online, nhận hàng tận nơi',
      'Hàng mới về',
      'Điện thoại',
      'Laptop',
      'Màn hình',
      'Tai nghe',
      'Phụ kiện',
      'Thương hiệu nổi bật',
      'Tất cả sản phẩm',
    ]);
  });

  it('giới hạn tối đa 5 khối danh mục gốc và mỗi khối dùng section với aria-labelledby', async () => {
    const extraCategories = [...fiveCategories, category('dong-ho', 'Đồng hồ')];
    stubApi({ categories: extraCategories });
    await renderPage();

    const blocks = [...container.querySelectorAll('section.category-block')];
    expect(blocks).toHaveLength(5);
    for (const block of blocks) {
      const labelledBy = block.getAttribute('aria-labelledby');
      expect(labelledBy).toBeTruthy();
      expect(block.querySelector(`#${labelledBy}`)?.tagName).toBe('H2');
    }
    expect(sectionHeadings()).not.toContain('Đồng hồ');
  });

  it('banner chọn sản phẩm mới nhất có ảnh, hiện giá từ, nút Xem ngay tới /p/:slug và giới hạn 8 thẻ không nút so sánh', async () => {
    const phoneProducts = [
      product('no-image-first', 29_990_000, null),
      product('banner-phone', 25_990_000),
      ...Array.from({ length: 8 }, (_, index) => product(`phone-${index + 3}`, 10_000_000)),
    ];
    stubApi({
      categoryItems: { 'dien-thoai': phoneProducts },
    });
    await renderPage();

    const phoneSection = container.querySelector(
      'section.category-block[data-category="dien-thoai"]',
    )!;
    expect(phoneSection).not.toBeNull();

    const banner = phoneSection.querySelector('.category-banner')!;
    expect(banner.getAttribute('aria-label')).toBe('Sản phẩm nổi bật của điện thoại');
    expect(banner.querySelector('.category-banner-title')?.tagName).toBe('P');
    expect(banner.querySelector('.category-banner-title')?.textContent).toBe(
      'Sản phẩm banner-phone',
    );
    expect(banner.querySelector('.category-banner-price')?.textContent).toBe('Giá từ 25.990.000 đ');
    expect(banner.querySelector('.category-banner-cta')?.getAttribute('href')).toBe(
      '/p/banner-phone',
    );
    expect(banner.querySelector('.category-banner-media img')?.getAttribute('src')).toContain(
      'banner-phone.jpg',
    );

    const gridCards = [...phoneSection.querySelectorAll('.category-block-grid .product-card')];
    expect(gridCards).toHaveLength(8);
    expect(phoneSection.querySelectorAll('button[aria-label*="so sánh"]')).toHaveLength(0);

    const seeAllLink = phoneSection.querySelector('a.category-block-more');
    expect(seeAllLink?.getAttribute('href')).toBe('/c/dien-thoai');
  });

  it('chip hãng sắp xếp theo số lượng giảm dần tối đa 5 hãng và gắn query ?brand=', async () => {
    stubApi({});
    await renderPage();

    const phoneSection = container.querySelector(
      'section.category-block[data-category="dien-thoai"]',
    )!;
    const chips = [...phoneSection.querySelectorAll('a.category-brand-chip')];
    expect(chips.map((chip) => chip.textContent)).toEqual([
      'Apple',
      'Samsung',
      'Xiaomi',
      'OPPO',
      'Google',
    ]);
    expect(chips.map((chip) => chip.getAttribute('href'))).toEqual([
      '/c/dien-thoai?brand=apple',
      '/c/dien-thoai?brand=samsung',
      '/c/dien-thoai?brand=xiaomi',
      '/c/dien-thoai?brand=oppo',
      '/c/dien-thoai?brand=google',
    ]);
  });

  it('ẩn khối danh mục khi danh mục chưa có sản phẩm', async () => {
    stubApi({ categoryItems: { laptop: [] } });
    await renderPage();

    expect(container.querySelector('section.category-block[data-category="laptop"]')).toBeNull();
    expect(sectionHeadings()).not.toContain('Laptop');
  });

  it('banner chỉ hiện chữ và nút tới /c/:slug khi sản phẩm trong danh mục đều thiếu ảnh', async () => {
    stubApi({
      categoryItems: {
        'man-hinh': [product('monitor-no-img', 6_490_000, null)],
      },
    });
    await renderPage();

    const monitorSection = container.querySelector(
      'section.category-block[data-category="man-hinh"]',
    )!;
    const banner = monitorSection.querySelector('.category-banner')!;
    expect(banner.querySelector('img')).toBeNull();
    expect(banner.querySelector('.category-banner-cta')?.getAttribute('href')).toBe('/c/man-hinh');
  });

  it('hiện SectionError với nút Thử lại trong khối danh mục tải lỗi', async () => {
    const fetchMock = stubApi({ categoryItems: { 'tai-nghe': 'error' } });
    await renderPage();

    const section = container.querySelector('section.category-block[data-category="tai-nghe"]')!;
    expect(section.querySelector('[role="alert"]')?.textContent).toContain(
      'Không tải được sản phẩm tai nghe.',
    );
    const retryButton = section.querySelector<HTMLButtonElement>('.section-error button');
    expect(retryButton?.textContent).toBe('Thử lại');

    const beforeCalls = fetchMock.mock.calls.length;
    await click(retryButton);
    expect(fetchMock.mock.calls.length).toBeGreaterThan(beforeCalls);
  });

  it('ẩn hàng chip hãng khi API tóm tắt lỗi hoặc không có hãng', async () => {
    stubApi({
      categoryBrands: {
        'dien-thoai': 'error',
        laptop: [],
      },
    });
    await renderPage();

    const phoneSection = container.querySelector(
      'section.category-block[data-category="dien-thoai"]',
    )!;
    const laptopSection = container.querySelector(
      'section.category-block[data-category="laptop"]',
    )!;
    expect(phoneSection.querySelector('.category-brand-chips')).toBeNull();
    expect(laptopSection.querySelector('.category-brand-chips')).toBeNull();
  });

  it('Thương hiệu nổi bật hiện tối đa 8 hãng theo thứ tự giảm dần và dẫn tới danh mục đầu tiên', async () => {
    stubApi({});
    await renderPage();

    const featured = container.querySelector('section.featured-brands')!;
    expect(featured).not.toBeNull();
    const featuredMore = featured.querySelector('.featured-brands-more');
    expect(featuredMore?.getAttribute('href')).toBe('/c/dien-thoai');
    expect(featuredMore?.getAttribute('aria-label')).toBe('Xem tất cả thương hiệu');

    const brandLinks = [...featured.querySelectorAll('a.featured-brand-tile')];
    expect(brandLinks.map((link) => link.textContent)).toEqual([
      'Apple',
      'Samsung',
      'Xiaomi',
      'OPPO',
      'Google',
      'Dell',
      'ASUS',
      'Sony',
    ]);
    expect(brandLinks.map((link) => link.getAttribute('href'))).toEqual([
      '/c/dien-thoai?brand=apple',
      '/c/dien-thoai?brand=samsung',
      '/c/dien-thoai?brand=xiaomi',
      '/c/dien-thoai?brand=oppo',
      '/c/dien-thoai?brand=google',
      '/c/dien-thoai?brand=dell',
      '/c/dien-thoai?brand=asus',
      '/c/dien-thoai?brand=sony',
    ]);
  });

  it('ẩn Thương hiệu nổi bật khi API tổng quan lỗi hoặc không có hãng', async () => {
    stubApi({ overviewBrands: 'error' });
    await renderPage();
    expect(container.querySelector('section.featured-brands')).toBeNull();

    stubApi({ overviewBrands: [] });
    await renderPage();
    expect(container.querySelector('section.featured-brands')).toBeNull();
  });

  it('gắn IntersectionObserver sau khi dữ liệu tải xong để khối danh mục không kẹt ở trạng thái ẩn', async () => {
    const observed: Element[] = [];
    class FakeObserver {
      private callback: IntersectionObserverCallback;
      constructor(callback: IntersectionObserverCallback) {
        this.callback = callback;
      }
      observe(target: Element) {
        observed.push(target);
        this.callback(
          [{ isIntersecting: true, target } as IntersectionObserverEntry],
          this as unknown as IntersectionObserver,
        );
      }
      unobserve() {}
      disconnect() {}
    }
    vi.stubGlobal('IntersectionObserver', FakeObserver);

    stubApi({});
    await renderPage();

    const phoneSection = container.querySelector(
      'section.category-block[data-category="dien-thoai"]',
    )!;
    const featuredSection = container.querySelector('section.featured-brands')!;
    expect(observed).toContain(phoneSection);
    expect(observed).toContain(featuredSection);
    expect(phoneSection.classList.contains('is-revealed')).toBe(true);
    expect(featuredSection.classList.contains('is-revealed')).toBe(true);
  });
});
