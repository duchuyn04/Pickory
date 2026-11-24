// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Header } from '../../../src/shared/header';
import type { ActorData } from '../../../src/features/identity/hooks/use-actor';
import { buildCart } from '../features/commerce/customer/cart-fixtures';
import {
  newTestQueryClient,
  renderWithProviders,
  settle,
  stubCartApi,
  type FakeApiOverride,
} from '../features/commerce/customer/cart-test-utils';

let container: HTMLDivElement;
let root: Root;

const customerActor: ActorData = {
  anonymousId: 'anon-customer',
  userId: 'user-customer',
  kind: 'customer',
  roles: ['customer'],
  permissions: [],
  consent: { personalization: false, marketing: false },
};

const staffActor: ActorData = {
  ...customerActor,
  anonymousId: 'anon-staff',
  userId: 'user-staff',
  kind: 'staff',
  roles: ['admin'],
};

const sampleCategories = [
  {
    id: 'cat-1',
    parentId: null,
    name: 'Điện thoại',
    slug: 'dien-thoai',
    children: [{ id: 'cat-1-1', parentId: 'cat-1', name: 'iPhone', slug: 'iphone', children: [] }],
  },
  {
    id: 'cat-2',
    parentId: null,
    name: 'Laptop',
    slug: 'laptop',
    children: [],
  },
];

async function renderHeader(actor?: ActorData, url = '/', override?: FakeApiOverride) {
  const api = stubCartApi(buildCart([]), (call) => {
    const customReply = override?.(call);
    if (customReply) return customReply;
    // Trả danh mục mẫu để test nút Danh mục hoạt động mà không cần backend thật.
    if (call.path === '/api/categories' && call.method === 'GET') {
      return { status: 200, body: { data: sampleCategories } };
    }
    return null;
  });
  const queryClient = newTestQueryClient();
  // Đặt phiên vào cache để test không phụ thuộc một response `/api/me` riêng cho từng loại khách.
  if (actor) queryClient.setQueryData(['actor'], actor);
  await renderWithProviders(root, <Header />, url, queryClient);
  return api;
}

function menuButton(): HTMLButtonElement {
  const button = container.querySelector<HTMLButtonElement>('button[aria-label="Menu"]');
  if (!button) throw new Error('Thiếu nút Menu.');
  return button;
}

function categoryButton(): HTMLButtonElement {
  const button = Array.from(container.querySelectorAll<HTMLButtonElement>('button')).find((item) =>
    item.textContent?.includes('Danh mục'),
  );
  if (!button) throw new Error('Thiếu nút Danh mục.');
  return button;
}

function searchForm(): HTMLFormElement {
  const form = container.querySelector<HTMLFormElement>('form[role="search"]');
  if (!form) throw new Error('Thiếu biểu mẫu tìm kiếm.');
  return form;
}

function searchInput(): HTMLInputElement {
  const input = container.querySelector<HTMLInputElement>(
    'input[type="search"][aria-label="Tìm kiếm sản phẩm"]',
  );
  if (!input) throw new Error('Thiếu ô nhập tìm kiếm.');
  return input;
}

async function openMenu(): Promise<HTMLButtonElement> {
  const button = menuButton();
  button.focus();
  await act(async () => button.click());
  return button;
}

function navigationDialog(): HTMLElement {
  const dialog = container.querySelector<HTMLElement>(
    '[role="dialog"][aria-label="Menu điều hướng"]',
  );
  if (!dialog) throw new Error('Ngăn điều hướng chưa mở.');
  return dialog;
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

describe('Header', () => {
  it('thanh tiện ích có hai cam kết bên trái và bốn liên kết bên phải', async () => {
    await renderHeader();

    const headerText = container.querySelector('header')?.textContent ?? '';
    expect(headerText).toContain('Bảo hành ghi rõ trên từng sản phẩm');
    expect(headerText).toContain('Phí giao hàng tính theo địa chỉ nhận');

    const navigation = container.querySelector('nav[aria-label="Điều hướng chính"]');
    const links = Array.from(navigation?.querySelectorAll('a') ?? []).map((link) => ({
      text: link.textContent?.trim(),
      href: link.getAttribute('href'),
    }));
    expect(links).toEqual([
      { text: 'Trang chủ', href: '/' },
      { text: 'Tra cứu đơn hàng', href: '/order-lookup' },
      { text: 'Phí giao hàng', href: '/policy/giao-hang' },
      { text: 'Quyền riêng tư', href: '/privacy' },
    ]);
  });

  it('có logo Pickory, nút Danh mục mở cây danh mục và ô tìm kiếm không điều hướng khi gửi', async () => {
    await renderHeader();

    const logoLink = container.querySelector<HTMLAnchorElement>('a[aria-label="Pickory"]');
    expect(logoLink?.getAttribute('href')).toBe('/');

    const form = searchForm();
    expect(form.getAttribute('role')).toBe('search');
    const input = searchInput();
    expect(input.getAttribute('aria-label')).toBe('Tìm kiếm sản phẩm');
    expect(input.placeholder.length).toBeGreaterThan(0);

    // Gửi biểu mẫu tìm kiếm không được đổi đường dẫn vì backend chưa có route tìm từ khóa.
    const submitEvent = new Event('submit', { bubbles: true, cancelable: true });
    await act(async () => {
      form.dispatchEvent(submitEvent);
    });
    expect(submitEvent.defaultPrevented).toBe(true);
    expect(container.querySelector('[data-testid="location"]')?.textContent).toBe('/');

    const catButton = categoryButton();
    expect(catButton.getAttribute('aria-expanded')).toBe('false');
    await act(async () => catButton.click());
    await settle();

    expect(catButton.getAttribute('aria-expanded')).toBe('true');
    const panelId = catButton.getAttribute('aria-controls');
    const panel = panelId ? container.querySelector(`#${panelId}`) : null;
    expect(panel).not.toBeNull();
    expect(panel?.querySelector('a[href="/c/dien-thoai"]')?.textContent).toBe('Điện thoại');
    expect(panel?.querySelector('a[href="/c/iphone"]')?.textContent).toBe('iPhone');

    // Bấm Escape đóng bảng danh mục và trả tiêu điểm về nút Danh mục.
    await act(async () => {
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    });
    expect(catButton.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(catButton);
  });

  it('bấm liên kết trong bảng Danh mục đóng bảng ngay cả khi đang ở đúng đường dẫn đó', async () => {
    // Khởi tạo ở đúng `/c/iphone` để kiểm `onClick` trên liên kết, không nhờ đổi `pathname`.
    await renderHeader(undefined, '/c/iphone');
    const catButton = categoryButton();
    await act(async () => catButton.click());
    await settle();

    const panelId = catButton.getAttribute('aria-controls');
    const panel = panelId ? container.querySelector(`#${panelId}`) : null;
    const iphoneLink = panel?.querySelector<HTMLAnchorElement>('a[href="/c/iphone"]');
    expect(iphoneLink).not.toBeNull();

    await act(async () => iphoneLink?.click());
    expect(catButton.getAttribute('aria-expanded')).toBe('false');
    expect(panelId ? container.querySelector(`#${panelId}`) : null).toBeNull();
  });

  it('tự đóng ngăn menu khi cửa sổ mở rộng vượt ngưỡng 1200px', async () => {
    let changeListener: ((event: MediaQueryListEvent) => void) | null = null;
    const fakeMediaQuery = {
      matches: false,
      media: '(min-width: 1200px)',
      addEventListener: (_type: string, listener: (event: MediaQueryListEvent) => void) => {
        changeListener = listener;
      },
      removeEventListener: () => {
        changeListener = null;
      },
    };
    vi.stubGlobal(
      'matchMedia',
      vi.fn(() => fakeMediaQuery),
    );

    await renderHeader();
    await openMenu();
    expect(navigationDialog()).not.toBeNull();

    // Giả lập đổi kích thước cửa sổ sang màn hình máy tính để kiểm `setIsMenuOpen(false)`.
    await act(async () => {
      changeListener?.({ matches: true } as MediaQueryListEvent);
    });
    expect(menuButton().getAttribute('aria-expanded')).toBe('false');
    expect(container.querySelector('[role="dialog"][aria-label="Menu điều hướng"]')).toBeNull();
  });

  it('nút Menu có nhãn truy cập, aria-controls và aria-expanded', async () => {
    await renderHeader();
    const button = menuButton();
    const panelId = button.getAttribute('aria-controls');
    expect(panelId).toBeTruthy();
    expect(container.querySelector(`#${panelId}`)).not.toBeNull();
    expect(button.getAttribute('aria-expanded')).toBe('false');

    await openMenu();
    expect(button.getAttribute('aria-expanded')).toBe('true');
    expect(navigationDialog()).not.toBeNull();
  });

  it('mở ngăn menu đưa focus vào trong ngăn, phím Escape đóng và trả focus về nút Menu', async () => {
    await renderHeader();
    const button = await openMenu();
    const dialog = navigationDialog();
    // Tiêu điểm chuyển vào trong ngăn menu ngay khi ngăn mở.
    expect(dialog.contains(document.activeElement)).toBe(true);

    await act(async () => {
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    });
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(container.querySelector('[role="dialog"][aria-label="Menu điều hướng"]')).toBeNull();
    expect(document.activeElement).toBe(button);
  });

  it('bấm nền mờ đóng ngăn menu', async () => {
    await renderHeader();
    await openMenu();
    const scrim = container.querySelector<HTMLElement>('[data-site-menu-scrim]');
    expect(scrim).not.toBeNull();

    await act(async () => scrim?.click());
    expect(menuButton().getAttribute('aria-expanded')).toBe('false');
    expect(container.querySelector('[role="dialog"][aria-label="Menu điều hướng"]')).toBeNull();
  });

  it('bấm liên kết trong ngăn đóng ngăn menu', async () => {
    await renderHeader();
    await openMenu();
    const dialog = navigationDialog();
    const privacyLink = Array.from(dialog.querySelectorAll('a')).find(
      (link) => link.textContent?.trim() === 'Quyền riêng tư',
    );
    expect(privacyLink).toBeTruthy();

    await act(async () => privacyLink?.click());
    expect(menuButton().getAttribute('aria-expanded')).toBe('false');
    expect(container.querySelector('[role="dialog"][aria-label="Menu điều hướng"]')).toBeNull();
  });

  it.each([
    {
      actor: undefined,
      description: 'khách vãng lai',
      expectedLinks: [
        'Trang chủ',
        'Tra cứu đơn hàng',
        'Phí giao hàng',
        'Quyền riêng tư',
        'Đăng nhập',
        'Đăng ký',
      ],
      unexpectedTexts: ['Tài khoản', 'Đăng xuất', 'Back-Office'],
    },
    {
      actor: customerActor,
      description: 'khách đăng nhập',
      expectedLinks: [
        'Trang chủ',
        'Tra cứu đơn hàng',
        'Phí giao hàng',
        'Quyền riêng tư',
        'Tài khoản',
      ],
      unexpectedTexts: ['Đăng nhập', 'Đăng ký', 'Back-Office'],
    },
    {
      actor: staffActor,
      description: 'nhân viên',
      expectedLinks: [
        'Trang chủ',
        'Tra cứu đơn hàng',
        'Phí giao hàng',
        'Quyền riêng tư',
        'Back-Office',
      ],
      unexpectedTexts: ['Đăng nhập', 'Đăng ký', 'Tài khoản'],
    },
  ])(
    'ngăn menu chứa đủ liên kết cho $description',
    async ({ actor, expectedLinks, unexpectedTexts }) => {
      await renderHeader(actor);
      await openMenu();
      const dialog = navigationDialog();
      const linkTexts = Array.from(dialog.querySelectorAll('a')).map((a) => a.textContent?.trim());
      for (const expected of expectedLinks) {
        expect(linkTexts).toContain(expected);
      }
      const allText = dialog.textContent ?? '';
      for (const unexpected of unexpectedTexts) {
        expect(allText).not.toContain(unexpected);
      }
      if (actor) {
        // Khách đăng nhập hoặc nhân viên thấy nút Đăng xuất trong ngăn menu.
        const logoutButton = Array.from(dialog.querySelectorAll('button')).find(
          (btn) => btn.textContent?.trim() === 'Đăng xuất',
        );
        expect(logoutButton).toBeTruthy();
      }
    },
  );

  it('đăng xuất lỗi trong ngăn menu thì đóng ngăn và hiện lỗi dưới header', async () => {
    await renderHeader(customerActor, '/me', (call) => {
      if (call.path === '/api/auth/sessions/current' && call.method === 'DELETE') {
        return { status: 500, body: { error: { message: 'Lỗi máy chủ khi đăng xuất.' } } };
      }
      return null;
    });

    await openMenu();
    const dialog = navigationDialog();
    const logoutButton = Array.from(dialog.querySelectorAll('button')).find(
      (btn) => btn.textContent?.trim() === 'Đăng xuất',
    );
    if (!logoutButton) throw new Error('Thiếu nút Đăng xuất trong ngăn.');

    await act(async () => logoutButton.click());
    await settle();

    // Ngăn kéo đóng khi bấm đăng xuất.
    expect(container.querySelector('[role="dialog"][aria-label="Menu điều hướng"]')).toBeNull();
    const alert = container.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('Lỗi máy chủ khi đăng xuất.');
  });
});
