// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Footer } from '../../../src/shared/footer';
import type { CategoryNode } from '../../../src/features/catalog/customer/catalog-api';
import {
  newTestQueryClient,
  renderWithProviders,
  settle,
} from '../features/commerce/customer/cart-test-utils';

let container: HTMLDivElement;
let root: Root;

const sixCategories: CategoryNode[] = [
  { id: 'cat-1', parentId: null, name: 'Điện thoại', slug: 'dien-thoai', children: [] },
  { id: 'cat-2', parentId: null, name: 'Laptop', slug: 'laptop', children: [] },
  { id: 'cat-3', parentId: null, name: 'Màn hình', slug: 'man-hinh', children: [] },
  { id: 'cat-4', parentId: null, name: 'Phụ kiện', slug: 'phu-kien', children: [] },
  { id: 'cat-5', parentId: null, name: 'Tai nghe', slug: 'tai-nghe', children: [] },
  { id: 'cat-6', parentId: null, name: 'Máy tính bảng', slug: 'may-tinh-bang', children: [] },
];

// Giả lập endpoint danh mục để test chân trang tách biệt khỏi máy chủ thật.
function stubCategoriesApi(reply: () => Response | Promise<Response>) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      if (String(input) === '/api/categories') {
        return reply();
      }
      return Response.json(
        { error: { code: 'NOT_FOUND', message: 'Không tìm thấy.' } },
        { status: 404 },
      );
    }),
  );
}

async function renderFooter(reply: () => Response | Promise<Response>) {
  stubCategoriesApi(reply);
  const queryClient = newTestQueryClient();
  await renderWithProviders(root, <Footer />, '/', queryClient);
  return queryClient;
}

function headings(): string[] {
  return Array.from(container.querySelectorAll('h1, h2, h3, h4, h5, h6')).map(
    (heading) => heading.textContent?.trim() ?? '',
  );
}

function linkByName(name: string): HTMLAnchorElement {
  const link = Array.from(container.querySelectorAll('a')).find(
    (item) => item.textContent?.trim() === name,
  );
  if (!link) throw new Error(`Thiếu liên kết "${name}".`);
  return link;
}

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

describe('Footer', () => {
  it('hiển thị bốn cột với tiêu đề, mô tả và dòng bản quyền đúng thiết kế', async () => {
    await renderFooter(() => Response.json({ data: sixCategories.slice(0, 5) }));

    const footer = container.querySelector('footer');
    expect(footer).not.toBeNull();
    expect(headings()).toEqual(['Pickory', 'Hỗ trợ khách hàng', 'Chính sách', 'Danh mục']);

    const text = footer?.textContent ?? '';
    expect(text).toContain(
      'Điện thoại, laptop, màn hình, phụ kiện và tai nghe chính hãng. Bảo hành và phí giao hàng ghi rõ trước khi đặt.',
    );
    expect(text).toContain('© 2026 Pickory');
    expect(text).toContain('Bảo hành ghi rõ trên từng sản phẩm');
  });

  it('gắn đúng đường dẫn cho các liên kết hỗ trợ khách hàng và chính sách', async () => {
    await renderFooter(() => Response.json({ data: sixCategories.slice(0, 5) }));

    expect(linkByName('Tra cứu đơn hàng').getAttribute('href')).toBe('/order-lookup');
    expect(linkByName('Phí giao hàng').getAttribute('href')).toBe('/policy/giao-hang');
    expect(linkByName('Liên hệ CSKH').getAttribute('href')).toBe('/support');

    expect(linkByName('Quyền riêng tư').getAttribute('href')).toBe('/privacy');
    expect(linkByName('Chính sách bảo hành').getAttribute('href')).toBe('/privacy');
    expect(linkByName('Điều khoản sử dụng').getAttribute('href')).toBe('/privacy');
  });

  it('hiển thị tối đa năm danh mục gốc từ API với đường dẫn đúng', async () => {
    await renderFooter(() => Response.json({ data: sixCategories }));

    const categoryNav = container.querySelector('nav[aria-label="Danh mục"]');
    expect(categoryNav).not.toBeNull();

    const items = Array.from(categoryNav?.querySelectorAll('a') ?? []).map((link) => ({
      name: link.textContent?.trim(),
      href: link.getAttribute('href'),
    }));

    expect(items).toEqual([
      { name: 'Điện thoại', href: '/c/dien-thoai' },
      { name: 'Laptop', href: '/c/laptop' },
      { name: 'Màn hình', href: '/c/man-hinh' },
      { name: 'Phụ kiện', href: '/c/phu-kien' },
      { name: 'Tai nghe', href: '/c/tai-nghe' },
    ]);
    expect(container.textContent).not.toContain('Máy tính bảng');
  });

  it('giữ nguyên khung bốn cột khi danh mục đang tải, rỗng hoặc lỗi API', async () => {
    let resolveCategories!: (value: Response) => void;
    const pendingPromise = new Promise<Response>((resolve) => {
      resolveCategories = resolve;
    });

    await renderFooter(() => pendingPromise);
    expect(headings()).toEqual(['Pickory', 'Hỗ trợ khách hàng', 'Chính sách', 'Danh mục']);
    expect(container.querySelector('[role="status"]')?.textContent).toContain('Đang tải danh mục');
    expect(container.textContent).toContain('© 2026 Pickory');

    // Hoàn tất request rỗng để kiểm tra trạng thái không có danh mục.
    await act(async () => {
      resolveCategories(Response.json({ data: [] }));
    });
    await settle();
    expect(container.textContent).toContain('Chưa có danh mục');

    // Kiểm tra lại khi API trả lỗi máy chủ: chân trang không văng lỗi và giữ đủ các cột khác.
    await act(async () => root.unmount());
    root = createRoot(container);
    await renderFooter(() =>
      Response.json({ error: { code: 'INTERNAL_ERROR', message: 'Lỗi' } }, { status: 500 }),
    );
    expect(headings()).toEqual(['Pickory', 'Hỗ trợ khách hàng', 'Chính sách', 'Danh mục']);
    expect(container.textContent).toContain('Chưa tải được danh mục');
    expect(linkByName('Tra cứu đơn hàng').getAttribute('href')).toBe('/order-lookup');
  });
});
