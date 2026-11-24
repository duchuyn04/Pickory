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
  // Tổng số sản phẩm của lưới "Tất cả sản phẩm". Mỗi trang trả đủ 12 sản phẩm tới khi hết.
  catalogTotal?: number;
  // Slug sản phẩm của banner mà API trả 404, như khi admin đã xóa sản phẩm.
  missingProductSlugs?: string[];
}

function detailForSlug(detailSlug: string) {
  return {
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
  };
}

// Trả response theo đường dẫn, để mỗi test chỉ khai báo phần dữ liệu nó kiểm.
function stubApi({
  categories = allCategories,
  categoryItems = {},
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

    // Danh sách cố định bỏ slug không còn bán, không biến thiếu sản phẩm thành lỗi 404.
    if (url.pathname === '/api/product-details') {
      return Response.json({
        data: params
          .getAll('slug')
          .filter((slug) => !missingProductSlugs.includes(slug))
          .map(detailForSlug),
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

function productNames(scope: Element | null): string[] {
  return [...(scope?.querySelectorAll('.product-card-name') ?? [])].map(
    (name) => name.textContent ?? '',
  );
}

function findLinkByText(
  text: string,
  scope: ParentNode = container,
): HTMLAnchorElement | undefined {
  return [...scope.querySelectorAll<HTMLAnchorElement>('a')].find((link) =>
    link.textContent?.includes(text),
  );
}

const firstCatalogBanner = HOME_BANNERS.find((banner) => banner.productSlug !== null);

describe('HomePage: tải danh mục', () => {
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
  it('đổi dải sản phẩm và liên kết "Xem tất cả" theo chip danh mục', async () => {
    stubApi({
      categoryItems: {
        'dien-thoai': [product('iphone', 20_000_000)],
        laptop: [product('xps', 40_000_000)],
      },
    });
    await renderPage();

    const section = container.querySelector('.new-arrivals')!;
    const phoneTab = [...section.querySelectorAll('[role="tab"]')].find(
      (tab) => tab.textContent === 'Điện thoại',
    )!;
    const laptopTab = [...section.querySelectorAll('[role="tab"]')].find(
      (tab) => tab.textContent === 'Laptop',
    )!;

    expect(phoneTab.getAttribute('aria-selected')).toBe('true');
    expect(laptopTab.getAttribute('aria-selected')).toBe('false');
    expect(section.querySelector('[role="tabpanel"]')?.getAttribute('aria-labelledby')).toBe(
      phoneTab.id,
    );
    expect(section.querySelector('ul[aria-label="Sản phẩm điện thoại mới về"]')).not.toBeNull();
    expect(productNames(section)).toEqual(['Sản phẩm iphone']);
    expect(findLinkByText('Xem tất cả', section)?.getAttribute('href')).toBe('/c/dien-thoai');

    await click(laptopTab);

    expect(laptopTab.getAttribute('aria-selected')).toBe('true');
    expect(phoneTab.getAttribute('aria-selected')).toBe('false');
    expect(section.querySelector('[role="tabpanel"]')?.getAttribute('aria-labelledby')).toBe(
      laptopTab.id,
    );
    expect(section.querySelector('ul[aria-label="Sản phẩm laptop mới về"]')).not.toBeNull();
    expect(productNames(section)).toEqual(['Sản phẩm xps']);
    const moreLink = findLinkByText('Xem tất cả', section);
    expect(moreLink?.getAttribute('href')).toBe('/c/laptop');
    expect(moreLink?.getAttribute('aria-label')).toBe('Xem tất cả laptop');
  });

  it('phím mũi tên chọn chip danh mục kế tiếp và đưa focus theo', async () => {
    stubApi({});
    await renderPage();

    const section = container.querySelector('.new-arrivals')!;
    const phoneTab = section.querySelector<HTMLButtonElement>('[role="tab"]')!;
    await act(async () => {
      phoneTab.focus();
      phoneTab.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    });
    await settle();

    const tabs = [...section.querySelectorAll<HTMLButtonElement>('[role="tab"]')];
    expect(tabs[1]?.getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement).toBe(tabs[1]);
  });

  it('nút cuộn dải sản phẩm gọi scrollBy và nút trái vô hiệu ở đầu dải', async () => {
    const items = Array.from({ length: 6 }, (_, index) =>
      product(`phone-${index + 1}`, 10_000_000),
    );
    stubApi({ categoryItems: { 'dien-thoai': items } });
    await renderPage();

    const section = container.querySelector('.new-arrivals')!;
    const track = section.querySelector<HTMLUListElement>('ul.product-rail-track')!;
    const scrollByMock = vi.fn();
    track.scrollBy = scrollByMock;

    const prevButton = section.querySelector<HTMLButtonElement>(
      'button[aria-label="Cuộn sang trái"]',
    )!;
    const nextButton = section.querySelector<HTMLButtonElement>(
      'button[aria-label="Cuộn sang phải"]',
    )!;

    expect(prevButton.disabled).toBe(true);
    expect(nextButton.disabled).toBe(false);

    await click(nextButton);
    expect(scrollByMock).toHaveBeenCalledWith(
      expect.objectContaining({ left: expect.any(Number) }),
    );
    expect(scrollByMock.mock.calls[0]?.[0]?.left).toBeGreaterThan(0);

    // Giả lập dải đã cuộn sang phải để nút trái bật lại và cuộn ngược chiều.
    Object.defineProperty(track, 'scrollLeft', { value: 240, configurable: true });
    await act(async () => {
      track.dispatchEvent(new Event('scroll'));
    });
    expect(prevButton.disabled).toBe(false);

    await click(prevButton);
    expect(scrollByMock.mock.calls[1]?.[0]?.left).toBeLessThan(0);
  });

  it('báo lỗi trong dải tải hỏng và cho thử lại', async () => {
    const fetchMock = stubApi({ categoryItems: { 'dien-thoai': 'error' } });
    await renderPage();

    const section = container.querySelector('.new-arrivals');
    expect(section?.querySelector('[role="alert"]')?.textContent).toContain(
      'Không tải được sản phẩm điện thoại.',
    );
    const retryButton = section?.querySelector<HTMLButtonElement>('.section-error button');
    expect(retryButton?.textContent).toBe('Thử lại');

    const beforeCalls = fetchMock.mock.calls.length;
    await click(retryButton);
    expect(fetchMock.mock.calls.length).toBeGreaterThan(beforeCalls);
  });

  it('báo danh mục chưa có sản phẩm thay vì để dải trống', async () => {
    stubApi({ categoryItems: { 'dien-thoai': [] } });
    await renderPage();

    expect(container.querySelector('.new-arrivals .section-empty')?.textContent).toContain(
      'Danh mục điện thoại chưa có sản phẩm.',
    );
  });

  it('danh mục có con không hiện nút so sánh trên thẻ, danh mục lá thì hiện', async () => {
    const categoriesWithChild: CategoryNode[] = [
      {
        id: 'id-dien-thoai',
        parentId: null,
        name: 'Điện thoại',
        slug: 'dien-thoai',
        children: [
          {
            id: 'id-iphone',
            parentId: 'id-dien-thoai',
            name: 'iPhone',
            slug: 'iphone',
            children: [],
          },
        ],
      },
      category('laptop', 'Laptop'),
    ];
    stubApi({
      categories: categoriesWithChild,
      categoryItems: {
        'dien-thoai': [product('iphone', 20_000_000)],
        laptop: [product('xps', 40_000_000)],
      },
    });
    await renderPage();

    const section = container.querySelector('.new-arrivals')!;
    expect(section.querySelectorAll('button[aria-label*="so sánh"]')).toHaveLength(0);

    const laptopTab = [...section.querySelectorAll('[role="tab"]')].find(
      (tab) => tab.textContent === 'Laptop',
    )!;
    await click(laptopTab);

    expect(section.querySelectorAll('button[aria-label*="so sánh"]')).toHaveLength(1);
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

describe('HomePage: khối đầu trang chủ', () => {
  it('menu danh mục liệt kê tối đa 5 danh mục gốc, ba lối tắt và link xem tất cả', async () => {
    const extraCategories = [
      ...allCategories,
      category('dong-ho', 'Đồng hồ'),
      category('may-tinh-bang', 'Máy tính bảng'),
    ];
    stubApi({ categories: extraCategories });
    await renderPage();

    const menuNav = container.querySelector('nav[aria-label="Danh mục sản phẩm"]');
    expect(menuNav).not.toBeNull();
    const links = [...(menuNav?.querySelectorAll('a') ?? [])];
    expect(links.map((link) => link.getAttribute('href'))).toEqual([
      '/c/dien-thoai',
      '/c/laptop',
      '/c/man-hinh',
      '/c/phu-kien',
      '/c/tai-nghe',
      '/order-lookup',
      '/policy/giao-hang',
      '/compare',
      '/c/dien-thoai',
    ]);
    expect(findLinkByText('Tra cứu đơn hàng', menuNav ?? container)?.getAttribute('href')).toBe(
      '/order-lookup',
    );
    expect(findLinkByText('Phí giao hàng', menuNav ?? container)?.getAttribute('href')).toBe(
      '/policy/giao-hang',
    );
    expect(findLinkByText('So sánh sản phẩm', menuNav ?? container)?.getAttribute('href')).toBe(
      '/compare',
    );
    expect(findLinkByText('Xem tất cả sản phẩm', menuNav ?? container)?.getAttribute('href')).toBe(
      '/c/dien-thoai',
    );
    expect(findLinkByText('Đồng hồ', menuNav ?? container)).toBeUndefined();
  });

  it('danh mục rỗng thì không hiện mục danh mục trong menu và không có link xem tất cả', async () => {
    stubApi({ categories: [] });
    await renderPage();

    const menuNav = container.querySelector('nav[aria-label="Danh mục sản phẩm"]');
    expect(findLinkByText('Xem tất cả sản phẩm', menuNav ?? container)).toBeUndefined();
    expect(findLinkByText('Tra cứu đơn hàng', menuNav ?? container)?.getAttribute('href')).toBe(
      '/order-lookup',
    );
  });

  it('ba banner nhỏ dẫn đúng link, ẩn banner của danh mục không tồn tại và vẫn render khi thiếu ảnh', async () => {
    stubApi({});
    await renderPage();

    const smallBannersRegion = container.querySelector(
      '[aria-label="Danh mục và tính năng nổi bật"]',
    );
    expect(smallBannersRegion).not.toBeNull();
    const laptopLink = findLinkByText('Gaming và đồ họa', smallBannersRegion ?? container);
    const headphoneLink = findLinkByText('Chống ồn chủ động', smallBannersRegion ?? container);
    const compareLink = findLinkByText('So sánh sản phẩm', smallBannersRegion ?? container);
    expect(laptopLink?.getAttribute('href')).toBe('/c/laptop');
    expect(headphoneLink?.getAttribute('href')).toBe('/c/tai-nghe');
    expect(compareLink?.getAttribute('href')).toBe('/compare');
    // Khi sản phẩm không có ảnh (`image: null`), banner vẫn đủ chữ và không render thẻ img.
    expect(laptopLink?.querySelector('img')).toBeNull();
    expect(laptopLink?.textContent).toContain('Cấu hình mạnh');
  });

  it('ẩn banner nhỏ của danh mục đã bị xóa khỏi cây danh mục', async () => {
    stubApi({ categories: allCategories.filter((node) => node.slug !== 'tai-nghe') });
    await renderPage();

    const smallBannersRegion = container.querySelector(
      '[aria-label="Danh mục và tính năng nổi bật"]',
    );
    expect(findLinkByText('Gaming và đồ họa', smallBannersRegion ?? container)).toBeDefined();
    expect(findLinkByText('Chống ồn chủ động', smallBannersRegion ?? container)).toBeUndefined();
    expect(findLinkByText('So sánh sản phẩm', smallBannersRegion ?? container)).toBeDefined();
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

  it('nạp các slug cố định bằng một request mà không hỏi chi tiết từng slug', async () => {
    const fetchMock = stubApi({});
    await renderPage();

    const paths = fetchMock.mock.calls.map(([url]) => String(url));
    const batchPaths = paths.filter((path) => path.startsWith('/api/product-details?'));
    expect(batchPaths).toHaveLength(1);
    expect(batchPaths[0]).toContain(`slug=${firstCatalogBanner?.productSlug}`);
    expect(paths.filter((path) => /^\/api\/products\/[^?]+/.test(path))).toEqual([]);
    expect(
      container.querySelector(`#banner-slide-${firstCatalogBanner?.id} .banner-price`)?.textContent,
    ).toContain('25.990.000 đ');
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
