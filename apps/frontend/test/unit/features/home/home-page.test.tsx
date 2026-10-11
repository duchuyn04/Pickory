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
import { AUTOPLAY_INTERVAL_MS } from '../../../../src/features/home/carousel-index';
import { HOME_BANNERS } from '../../../../src/features/home/home-banners';
import { HomePage } from '../../../../src/features/home/home-page';

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  // Chỉ giả hẹn giờ lặp của carousel. `setTimeout` thật vẫn chạy để fetch và React Query hoàn tất.
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

const allCategories = [
  category('dien-thoai', 'Điện thoại'),
  category('laptop', 'Laptop'),
  category('man-hinh', 'Màn hình'),
  category('phu-kien', 'Phụ kiện'),
  category('tai-nghe', 'Tai nghe'),
];

function product(slug: string, minPrice: number, brandSlug = 'hang'): ProductListItem {
  return {
    id: `p-${slug}`,
    slug,
    name: `Sản phẩm ${slug}`,
    brand: { id: `b-${brandSlug}`, name: brandSlug.toUpperCase(), slug: brandSlug },
    minPrice,
    inStock: true,
    status: 'active',
    image: null,
  };
}

const defaultBrands: BrandFacet[] = [
  { slug: 'samsung', name: 'Samsung', count: 8 },
  { slug: 'apple', name: 'Apple', count: 9 },
];

function listResponse(
  items: ProductListItem[],
  { total = items.length, page = 1, pageSize = items.length, brands = defaultBrands } = {},
): ProductListResponse {
  return { items, page, pageSize, total, facets: { brands, attributes: [] } };
}

function errorResponse(status: number) {
  return Response.json({ error: { code: 'ERROR', message: 'Lỗi.' } }, { status });
}

interface FakeApi {
  // `'pending'` giữ request cây danh mục không bao giờ trả về, để kiểm trạng thái đang tải.
  categories?: CategoryNode[] | 'error' | 'pending';
  // Danh mục có giá trị `'error'` thì lưới sản phẩm của danh mục đó trả lỗi.
  categoryItems?: Record<string, ProductListItem[] | 'error'>;
  brandItems?: Record<string, ProductListItem[]>;
  // Tổng số sản phẩm của lưới "Tất cả sản phẩm". Mỗi trang trả đủ 12 sản phẩm tới khi hết.
  catalogTotal?: number;
  // Slug sản phẩm của banner mà API trả 404, như khi admin đã xóa sản phẩm.
  missingProductSlugs?: string[];
}

// Trả response theo đường dẫn, để mỗi test chỉ khai báo phần dữ liệu nó kiểm.
function stubApi({
  categories = allCategories,
  categoryItems = {},
  brandItems = {},
  catalogTotal = 30,
  missingProductSlugs = [],
}: FakeApi) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input), 'http://localhost');
    const params = url.searchParams;

    if (url.pathname === '/api/categories') {
      if (categories === 'pending') return new Promise<Response>(() => {});
      if (categories === 'error') return errorResponse(500);
      return Response.json({ data: categories });
    }

    // Chi tiết sản phẩm của banner: giá 25.990.000 đ, bảo hành 12 tháng.
    const detailSlug = url.pathname.match(/^\/api\/products\/(.+)$/)?.[1];
    if (detailSlug) {
      if (missingProductSlugs.includes(detailSlug)) return errorResponse(404);
      return Response.json({
        data: {
          id: `id-${detailSlug}`,
          slug: detailSlug,
          name: `Máy ${detailSlug}`,
          minPrice: 25_990_000,
          warrantyMonths: 12,
          brand: { id: 'b-apple', name: 'Apple', slug: 'apple' },
          category: { id: 'cat-phone', name: 'Điện thoại', slug: 'dien-thoai' },
          images: [],
          specs: [
            {
              key: 'ram_gb',
              label: 'RAM',
              group: 'Cấu hình',
              type: 'number',
              unit: 'GB',
              value: 8,
            },
            {
              key: 'screen_inch',
              label: 'Kích thước màn hình',
              group: 'Màn hình',
              type: 'number',
              unit: 'inch',
              value: 6.3,
            },
          ],
        },
      });
    }

    const categorySlug = params.get('category');
    if (categorySlug !== null) {
      const items = categoryItems[categorySlug] ?? [product(`${categorySlug}-1`, 1_990_000)];
      if (items === 'error') return errorResponse(500);
      // Request tóm tắt sắp giá tăng và lấy một sản phẩm, nên chỉ trả sản phẩm đầu kèm tổng số thật.
      if (params.get('pageSize') === '1') {
        return Response.json({ data: listResponse(items.slice(0, 1), { total: items.length }) });
      }
      return Response.json({ data: listResponse(items) });
    }

    const brandSlug = params.get('brand');
    if (brandSlug !== null) {
      const items = brandItems[brandSlug] ?? [product(`${brandSlug}-1`, 990_000, brandSlug)];
      return Response.json({ data: listResponse(items) });
    }

    // Không lọc gì và lấy một sản phẩm: request tổng quan lấy tổng số và facet thương hiệu.
    if (params.get('pageSize') === '1') {
      return Response.json({
        data: listResponse([product('tong-quan', 1)], { total: catalogTotal }),
      });
    }

    const page = Number(params.get('page') ?? '1');
    const pageSize = Number(params.get('pageSize') ?? '12');
    const firstIndex = (page - 1) * pageSize;
    const count = Math.max(Math.min(pageSize, catalogTotal - firstIndex), 0);
    const items = Array.from({ length: count }, (_, index) =>
      product(`tat-ca-${firstIndex + index + 1}`, 500_000),
    );
    return Response.json({ data: listResponse(items, { total: catalogTotal, page, pageSize }) });
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

async function renderPage() {
  // `retry: false` để lỗi hiện ngay, không chờ thử lại trong test.
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

// Nhường vài vòng sự kiện để fetch, React Query và effect hoàn tất.
async function settle() {
  for (let turn = 0; turn < 10; turn += 1) {
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
  }
}

async function click(element: Element | null | undefined) {
  await act(async () => (element as HTMLElement | null)?.click());
  await settle();
}

function findButton(text: string): HTMLButtonElement | undefined {
  return [...container.querySelectorAll('button')].find((button) =>
    button.textContent?.startsWith(text),
  );
}

function selectedBannerName(): string | null | undefined {
  return container.querySelector('.banner-tab[aria-selected="true"] .banner-tab-name')?.textContent;
}

// Tiêu đề các khối dưới banner. Tên máy trên banner cũng là h2, nên bỏ phần carousel ra.
function sectionHeadings(): string[] {
  return [...container.querySelectorAll('main h2')]
    .filter((heading) => !heading.closest('.banner-carousel'))
    .map((heading) => heading.textContent ?? '');
}

function productNames(scope: Element | null): string[] {
  return [...(scope?.querySelectorAll('.product-card-name') ?? [])].map(
    (name) => name.textContent ?? '',
  );
}

const firstCatalogBanner = HOME_BANNERS.find((banner) => banner.productSlug !== null);

describe('HomePage: các khối nội dung', () => {
  it('hiện các khối theo thứ tự của thegioididong.com', async () => {
    stubApi({});
    await renderPage();

    expect(sectionHeadings()).toEqual([
      'Danh mục sản phẩm',
      'Hàng mới về',
      'Mua theo thương hiệu',
      'Danh mục nổi bật',
      'Tất cả sản phẩm',
      'Thương hiệu tại Pickory',
      'So sánh nhanh',
      'Tìm nhanh theo danh mục',
    ]);
  });

  it('dải cam kết ghi số sản phẩm và số thương hiệu lấy từ catalog', async () => {
    stubApi({ catalogTotal: 50 });
    await renderPage();

    expect(container.querySelector('.service-strip')?.textContent).toContain(
      '50 sản phẩm từ 2 thương hiệu',
    );
  });

  it('ô danh mục dẫn tới trang danh mục và ghi số sản phẩm, giá thấp nhất', async () => {
    stubApi({
      categoryItems: { 'dien-thoai': [product('re', 3_490_000), product('dat', 9_990_000)] },
    });
    await renderPage();

    const tiles = [...container.querySelectorAll('a.category-tile')];
    expect(tiles.map((tile) => tile.getAttribute('href'))).toEqual([
      '/c/dien-thoai',
      '/c/laptop',
      '/c/man-hinh',
      '/c/phu-kien',
      '/c/tai-nghe',
    ]);
    expect(tiles[0]?.querySelector('.category-tile-count')?.textContent).toBe(
      '2 sản phẩm, từ 3.490.000 đ',
    );
  });

  it('hiện khung chờ của ô danh mục khi cây danh mục còn đang tải', async () => {
    stubApi({ categories: 'pending' });
    await renderPage();

    expect(container.querySelectorAll('.category-tile-skeleton')).toHaveLength(5);
  });

  it('báo lỗi khi không tải được danh mục và cho thử lại', async () => {
    const fetchMock = stubApi({ categories: 'error' });
    await renderPage();

    expect(container.querySelector('.home-error [role="alert"]')?.textContent).toContain(
      'Không tải được danh mục sản phẩm.',
    );
    const countCategoryCalls = () =>
      fetchMock.mock.calls.filter(([url]) => String(url).startsWith('/api/categories')).length;
    const callsBefore = countCategoryCalls();

    await click(container.querySelector('.home-error button'));

    expect(countCategoryCalls()).toBe(callsBefore + 1);
  });
});

describe('HomePage: hàng mới về', () => {
  it('đổi lưới sản phẩm theo tab danh mục', async () => {
    stubApi({
      categoryItems: {
        'dien-thoai': [product('iphone', 20_000_000)],
        laptop: [product('xps', 40_000_000)],
      },
    });
    await renderPage();

    const section = container.querySelector('.new-arrivals');
    expect(productNames(section)).toEqual(['Sản phẩm iphone']);
    expect(section?.querySelector('.section-more')?.getAttribute('href')).toBe('/c/dien-thoai');

    const laptopTab = [...(section?.querySelectorAll('[role="tab"]') ?? [])].find(
      (tab) => tab.textContent === 'Laptop',
    );
    await click(laptopTab);

    expect(productNames(section)).toEqual(['Sản phẩm xps']);
    expect(section?.querySelector('.section-more')?.textContent).toBe('Xem tất cả laptop');
  });

  it('báo lỗi trong lưới tải hỏng và cho thử lại', async () => {
    stubApi({ categoryItems: { 'dien-thoai': 'error' } });
    await renderPage();

    const section = container.querySelector('.new-arrivals');
    expect(section?.querySelector('[role="alert"]')?.textContent).toContain(
      'Không tải được sản phẩm điện thoại.',
    );
    expect(section?.querySelector('.section-error button')?.textContent).toBe('Thử lại');
  });

  it('báo danh mục chưa có sản phẩm thay vì để lưới trống', async () => {
    stubApi({ categoryItems: { 'dien-thoai': [] } });
    await renderPage();

    expect(container.querySelector('.new-arrivals .section-empty')?.textContent).toContain(
      'Danh mục điện thoại chưa có sản phẩm.',
    );
  });
});

describe('HomePage: mua theo thương hiệu', () => {
  it('xếp hãng nhiều sản phẩm trước và mở sẵn hãng đầu tiên', async () => {
    stubApi({});
    await renderPage();

    const pills = [...container.querySelectorAll('.brand-pill')];
    expect(pills.map((pill) => pill.firstChild?.textContent)).toEqual(['Apple', 'Samsung']);
    expect(pills[0]?.getAttribute('aria-pressed')).toBe('true');
    expect(productNames(container.querySelector('.brand-deals'))).toEqual(['Sản phẩm apple-1']);
  });

  it('hiện một hàng sáu thẻ, nút "Xem thêm" mở phần còn lại', async () => {
    const appleItems = Array.from({ length: 8 }, (_, index) =>
      product(`apple-${index + 1}`, 1_000_000, 'apple'),
    );
    stubApi({ brandItems: { apple: appleItems } });
    await renderPage();

    const deals = container.querySelector('.brand-deals');
    expect(productNames(deals)).toHaveLength(6);

    await click(findButton('Xem thêm 2 sản phẩm Apple'));
    expect(productNames(deals)).toHaveLength(8);
    expect(findButton('Thu gọn')?.getAttribute('aria-expanded')).toBe('true');
  });

  it('bấm hãng khác thì đổi lưới sang sản phẩm của hãng đó', async () => {
    stubApi({ brandItems: { samsung: [product('galaxy', 9_000_000, 'samsung')] } });
    await renderPage();

    await click(findButton('Samsung'));

    expect(productNames(container.querySelector('.brand-deals'))).toEqual(['Sản phẩm galaxy']);
  });
});

describe('HomePage: tất cả sản phẩm', () => {
  it('nút "Xem thêm" tải trang kế tiếp và báo khi đã xem hết', async () => {
    stubApi({ catalogTotal: 15 });
    await renderPage();

    const section = container.querySelector('section[aria-labelledby="home-catalog-heading"]');
    expect(productNames(section)).toHaveLength(12);

    await click(findButton('Xem thêm 3 sản phẩm'));

    expect(productNames(section)).toHaveLength(15);
    expect(section?.querySelector('.section-end')?.textContent).toBe('Bạn đã xem hết 15 sản phẩm.');
  });

  it('đổi cách sắp xếp thì hỏi lại API với tham số sort mới', async () => {
    const fetchMock = stubApi({});
    await renderPage();

    await click(findButton('Giá thấp đến cao'));

    const sortedRequest = fetchMock.mock.calls.find(([url]) =>
      String(url).includes('sort=price_asc&page=1'),
    );
    expect(sortedRequest).toBeDefined();
  });
});

describe('HomePage: banner sản phẩm', () => {
  it('máy đang bán lấy giá và bảo hành từ catalog, nút mở trang chi tiết', async () => {
    stubApi({});
    await renderPage();

    const slide = container.querySelector(`#banner-slide-${firstCatalogBanner?.id}`);
    expect(slide?.querySelector('.banner-price')?.textContent).toContain('25.990.000 đ');
    expect(slide?.querySelector('.banner-price')?.textContent).toContain('Bảo hành 12 tháng');
    expect(slide?.querySelector('.banner-cta')?.getAttribute('href')).toBe(
      `/p/${firstCatalogBanner?.productSlug}`,
    );
  });

  it('máy chưa có trong catalog ghi "Sắp có" và không có nút mua', async () => {
    stubApi({});
    await renderPage();

    const slide = container.querySelector('#banner-slide-iphone-18-pro');
    expect(slide?.querySelector('.banner-soon')?.textContent).toBe('Sắp có tại Pickory');
    expect(slide?.querySelector('.banner-cta')).toBeNull();
    expect(slide?.querySelector('.banner-price')?.textContent).toContain('38.999.000 đ');
  });

  it('bỏ banner của sản phẩm đã bị xóa khỏi catalog', async () => {
    const missingSlug = firstCatalogBanner?.productSlug ?? '';
    stubApi({ missingProductSlugs: [missingSlug] });
    await renderPage();

    expect(container.querySelectorAll('.banner-tab')).toHaveLength(HOME_BANNERS.length - 1);
    expect(container.querySelector(`#banner-slide-${firstCatalogBanner?.id}`)).toBeNull();
  });

  it('tự chuyển banner sau mỗi khoảng thời gian', async () => {
    stubApi({});
    await renderPage();
    expect(selectedBannerName()).toBe(HOME_BANNERS[0]?.tabName);

    await act(async () => vi.advanceTimersByTime(AUTOPLAY_INTERVAL_MS));
    expect(selectedBannerName()).toBe(HOME_BANNERS[1]?.tabName);
  });

  it('nút dừng giữ nguyên banner hiện tại', async () => {
    stubApi({});
    await renderPage();

    const pauseButton = container.querySelector<HTMLButtonElement>(
      '[aria-label="Dừng tự chuyển banner"]',
    );
    await act(async () => pauseButton?.click());
    // Click cũng đưa focus vào carousel. Rời focus để chỉ còn nút dừng giữ banner đứng yên.
    await act(async () => pauseButton?.blur());
    await act(async () => vi.advanceTimersByTime(AUTOPLAY_INTERVAL_MS * 2));

    expect(selectedBannerName()).toBe(HOME_BANNERS[0]?.tabName);
    expect(pauseButton?.getAttribute('aria-label')).toBe('Bật tự chuyển banner');
  });

  it('phím mũi tên chọn tab kế tiếp và đưa focus theo', async () => {
    stubApi({});
    await renderPage();

    const firstTab = container.querySelector<HTMLButtonElement>('.banner-tab');
    await act(async () => {
      firstTab?.focus();
      firstTab?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    });

    const lastName = HOME_BANNERS[HOME_BANNERS.length - 1]?.tabName;
    expect(selectedBannerName()).toBe(lastName);
    expect(document.activeElement?.querySelector('.banner-tab-name')?.textContent).toBe(lastName);
  });

  it('banner ẩn không nhận focus', async () => {
    stubApi({});
    await renderPage();

    const slides = [...container.querySelectorAll('.banner-slide')];
    expect(slides.filter((slide) => !slide.hasAttribute('inert'))).toHaveLength(1);
    expect(slides[0]?.hasAttribute('inert')).toBe(false);
  });
});

describe('HomePage: banner danh mục', () => {
  it('ghi số mẫu, giá thấp nhất và các hãng của danh mục, cả banner dẫn tới danh mục', async () => {
    stubApi({
      categoryItems: {
        laptop: [product('xps', 40_000_000, 'dell'), product('zenbook', 20_000_000, 'asus')],
      },
    });
    await renderPage();

    const laptopPromo = [...container.querySelectorAll('.promo')].find(
      (promo) => promo.querySelector('.promo-title')?.textContent === 'Laptop',
    );
    expect(laptopPromo?.querySelector('.promo-meta')?.textContent).toBe(
      '2 mẫu, giá từ 40.000.000 đ',
    );
    expect(laptopPromo?.querySelector('.promo-brands .sr')?.textContent).toBe(
      'Các hãng: Apple, Samsung',
    );
    expect(laptopPromo?.querySelector('.promo-cta')?.getAttribute('href')).toBe('/c/laptop');
  });

  it('bỏ banner của danh mục không còn tồn tại', async () => {
    stubApi({ categories: allCategories.filter((node) => node.slug !== 'tai-nghe') });
    await renderPage();

    const titles = [...container.querySelectorAll('.promo-title')].map(
      (title) => title.textContent,
    );
    expect(titles).toEqual(['Laptop']);
  });
});

describe('HomePage: so sánh nhanh', () => {
  it('đặt thông số chung của hai máy cạnh nhau, số thập phân dùng dấu phẩy', async () => {
    stubApi({});
    await renderPage();

    const rows = [...container.querySelectorAll('.duel-table tbody tr')].map((row) =>
      [...row.children].map((cell) => cell.textContent),
    );
    expect(rows).toEqual([
      ['8 GB', 'RAM', '8 GB'],
      ['6,3 inch', 'Kích thước màn hình', '6,3 inch'],
    ]);
  });

  it('nút so sánh đưa đúng hai máy vào danh sách so sánh', async () => {
    stubApi({});
    await renderPage();

    await click(findButton('So sánh chi tiết hai máy'));

    expect(JSON.parse(window.localStorage.getItem('pickory.compare') ?? 'null')).toEqual({
      categoryId: 'cat-phone',
      productIds: ['id-apple-iphone-16-pro', 'id-samsung-galaxy-s24'],
    });
  });

  it('một máy không còn trong catalog thì hiện dải mời so sánh chung', async () => {
    stubApi({ missingProductSlugs: ['samsung-galaxy-s24'] });
    await renderPage();

    expect(container.querySelector('.duel')).toBeNull();
    expect(container.querySelector('.compare-band h2')?.textContent).toBe('Chưa chọn được máy?');
  });
});
