// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  CategoryNode,
  ProductListItem,
  ProductListResponse,
} from '../../../../src/features/catalog/customer/catalog-api';
import { AUTOPLAY_INTERVAL_MS } from '../../../../src/features/home/carousel-index';
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

function product(slug: string, minPrice: number): ProductListItem {
  return {
    id: `p-${slug}`,
    slug,
    name: `Sản phẩm ${slug}`,
    brand: { id: 'b', name: 'Hãng', slug: 'hang' },
    minPrice,
    inStock: true,
    status: 'active',
    image: null,
  };
}

function listResponse(items: ProductListItem[], total = items.length): ProductListResponse {
  return {
    items,
    page: 1,
    pageSize: items.length,
    total,
    facets: { brands: [], attributes: [] },
  };
}

interface FakeApi {
  // `'pending'` giữ request cây danh mục không bao giờ trả về, để kiểm trạng thái đang tải.
  categories?: CategoryNode[] | 'error' | 'pending';
  // Danh mục có giá trị `'error'` thì dải sản phẩm của danh mục đó trả lỗi.
  railItems?: Record<string, ProductListItem[] | 'error'>;
}

// Trả response theo đường dẫn, để mỗi test chỉ khai báo phần dữ liệu nó kiểm.
function stubApi({ categories = allCategories, railItems = {} }: FakeApi) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input), 'http://localhost');
    if (url.pathname === '/api/categories') {
      if (categories === 'pending') {
        return new Promise<Response>(() => {});
      }
      if (categories === 'error') {
        return Response.json(
          { error: { code: 'INTERNAL', message: 'Lỗi máy chủ.' } },
          { status: 500 },
        );
      }
      return Response.json({ data: categories });
    }
    const categorySlug = url.searchParams.get('category') ?? '';
    const items = railItems[categorySlug] ?? [product(`${categorySlug}-1`, 1_990_000)];
    if (items === 'error') {
      return Response.json(
        { error: { code: 'INTERNAL', message: 'Lỗi máy chủ.' } },
        { status: 500 },
      );
    }
    // Request tóm tắt sắp giá tăng và lấy một sản phẩm, nên chỉ trả sản phẩm đầu kèm tổng số thật.
    if (url.searchParams.get('pageSize') === '1') {
      return Response.json({ data: listResponse(items.slice(0, 1), items.length) });
    }
    return Response.json({ data: listResponse(items) });
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
  for (let turn = 0; turn < 8; turn += 1) {
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
  }
}

function selectedTabName(): string | null | undefined {
  return container.querySelector('[role="tab"][aria-selected="true"]')?.textContent;
}

function sectionHeadings(): string[] {
  return [...container.querySelectorAll('.home-section h2')].map(
    (heading) => heading.textContent ?? '',
  );
}

describe('HomePage: nội dung theo catalog', () => {
  it('hiện menu, ô danh mục và một dải sản phẩm cho mỗi danh mục', async () => {
    stubApi({});
    await renderPage();

    const menuLinks = [...container.querySelectorAll('.home-category-menu a')];
    expect(menuLinks.map((link) => link.getAttribute('href'))).toEqual([
      '/c/dien-thoai',
      '/c/laptop',
      '/c/man-hinh',
      '/c/phu-kien',
      '/c/tai-nghe',
    ]);
    expect(sectionHeadings()).toEqual([
      'Danh mục nổi bật',
      'Điện thoại',
      'Laptop',
      'Màn hình',
      'Phụ kiện',
      'Tai nghe',
    ]);
    expect(container.querySelectorAll('.category-tile-count')[0]?.textContent).toBe('1 sản phẩm');
    expect(container.querySelectorAll('.rail .product-card')).toHaveLength(5);
  });

  it('banner hiện giá thấp nhất của danh mục', async () => {
    stubApi({
      railItems: { 'dien-thoai': [product('re', 3_490_000), product('dat', 9_990_000)] },
    });
    await renderPage();

    const activeSlide = container.querySelector('.hero-slide.is-active');
    expect(activeSlide?.textContent).toContain('Giá chỉ từ 3.490.000 đ');
  });

  it('bỏ banner và dải sản phẩm của danh mục không còn tồn tại', async () => {
    stubApi({ categories: allCategories.filter((node) => node.slug !== 'tai-nghe') });
    await renderPage();

    const tabNames = [...container.querySelectorAll('[role="tab"]')].map((tab) => tab.textContent);
    expect(tabNames).toEqual(['Điện thoại', 'Laptop']);
    expect(sectionHeadings()).not.toContain('Tai nghe');
  });

  it('ẩn dải sản phẩm của danh mục chưa có sản phẩm', async () => {
    stubApi({ railItems: { 'man-hinh': [] } });
    await renderPage();

    expect(sectionHeadings()).not.toContain('Màn hình');
    expect(container.querySelector('a[href="/c/man-hinh"].category-tile')).not.toBeNull();
  });

  it('báo lỗi trong dải sản phẩm tải hỏng, các dải khác vẫn hiện', async () => {
    stubApi({ railItems: { laptop: 'error' } });
    await renderPage();

    const laptopSection = container.querySelector('section[aria-labelledby="rail-laptop"]');
    expect(laptopSection?.querySelector('[role="alert"]')?.textContent).toContain(
      'Không tải được sản phẩm laptop.',
    );
    expect(container.querySelectorAll('.rail .product-card')).toHaveLength(4);
  });

  it('không hỏi giá sản phẩm khi cây danh mục còn đang tải', async () => {
    const fetchMock = stubApi({ categories: 'pending' });
    await renderPage();

    // Danh mục trong cấu hình banner có thể không tồn tại. Hỏi giá sớm sẽ nhận 404 từ API.
    const productRequests = fetchMock.mock.calls.filter(([url]) =>
      String(url).startsWith('/api/products'),
    );
    expect(productRequests).toHaveLength(0);
    expect(container.querySelectorAll('.side-banner-skeleton')).toHaveLength(2);
  });

  it('báo lỗi khi không tải được danh mục và cho thử lại', async () => {
    const fetchMock = stubApi({ categories: 'error' });
    await renderPage();

    expect(container.querySelector('[role="alert"]')?.textContent).toContain(
      'Không tải được danh mục sản phẩm.',
    );
    const categoryCallsBefore = fetchMock.mock.calls.filter(([url]) =>
      String(url).startsWith('/api/categories'),
    ).length;

    const retryButton = [...container.querySelectorAll('button')].find(
      (button) => button.textContent === 'Thử lại',
    );
    await act(async () => retryButton?.click());
    await settle();

    const categoryCallsAfter = fetchMock.mock.calls.filter(([url]) =>
      String(url).startsWith('/api/categories'),
    ).length;
    expect(categoryCallsAfter).toBe(categoryCallsBefore + 1);
  });
});

describe('HomePage: carousel banner', () => {
  it('tự chuyển banner sau mỗi khoảng thời gian', async () => {
    stubApi({});
    await renderPage();
    expect(selectedTabName()).toBe('Điện thoại');

    await act(async () => vi.advanceTimersByTime(AUTOPLAY_INTERVAL_MS));
    expect(selectedTabName()).toBe('Laptop');
  });

  it('nút dừng giữ nguyên banner hiện tại', async () => {
    stubApi({});
    await renderPage();

    const pauseButton = container.querySelector<HTMLButtonElement>('.hero-carousel-toggle');
    await act(async () => pauseButton?.click());
    // Click cũng đưa focus vào carousel. Rời focus để chỉ còn nút dừng giữ banner đứng yên.
    await act(async () => pauseButton?.blur());
    await act(async () => vi.advanceTimersByTime(AUTOPLAY_INTERVAL_MS * 2));

    expect(selectedTabName()).toBe('Điện thoại');
    expect(pauseButton?.getAttribute('aria-label')).toBe('Bật tự chuyển banner');
  });

  it('phím mũi tên chọn tab kế tiếp và đưa focus theo', async () => {
    stubApi({});
    await renderPage();

    const firstTab = container.querySelector<HTMLButtonElement>('[role="tab"]');
    await act(async () => {
      firstTab?.focus();
      firstTab?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    });

    expect(selectedTabName()).toBe('Tai nghe');
    expect(document.activeElement?.textContent).toBe('Tai nghe');
  });

  it('banner ẩn không nhận focus', async () => {
    stubApi({});
    await renderPage();

    const slides = [...container.querySelectorAll('.hero-slide')];
    expect(slides.map((slide) => slide.hasAttribute('inert'))).toEqual([false, true, true]);
  });
});
