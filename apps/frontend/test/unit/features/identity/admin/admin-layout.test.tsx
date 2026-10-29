// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React, { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminLayout } from '../../../../../src/features/identity/admin/admin-layout';
import type { ActorData } from '../../../../../src/features/identity/hooks/use-actor';

let container: HTMLDivElement;
let root: Root;

const adminActor: ActorData = {
  anonymousId: 'anonymous-admin',
  userId: 'user-admin',
  email: 'admin@pickory.local',
  name: 'Nguyễn Quản Trị',
  kind: 'staff',
  roles: ['admin'],
  permissions: [],
  consent: { personalization: false, marketing: false },
};

// Sales chỉ đọc sản phẩm và khách hàng, không có quyền quản lý nhân viên hay role.
const salesActor: ActorData = {
  ...adminActor,
  userId: 'user-sales',
  name: 'Trần Bán Hàng',
  roles: ['sales'],
  permissions: ['products:read', 'customers:read', 'orders:read'],
};

beforeEach(() => {
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  window.localStorage.clear();
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

async function renderLayout(actor: ActorData, path = '/admin') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  // Đặt actor sẵn trong cache để khung không gọi `/api/me` trong jsdom.
  queryClient.setQueryData(['actor'], actor);
  await act(async () =>
    root.render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route path="/admin" element={<AdminLayout />}>
              <Route index element={<p>Trang tổng quan</p>} />
              <Route path="*" element={<p>Trang con</p>} />
            </Route>
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    ),
  );
}

function navLink(label: string): HTMLAnchorElement {
  const link = Array.from(container.querySelectorAll<HTMLAnchorElement>('nav a')).find(
    (anchor) => anchor.textContent?.trim() === label,
  );
  if (!link) throw new Error(`Không thấy mục menu ${label}`);
  return link;
}

function navLabels(): string[] {
  return Array.from(container.querySelectorAll('nav a')).map(
    (anchor) => anchor.textContent?.trim() ?? '',
  );
}

function button(name: string): HTMLButtonElement {
  const match = Array.from(container.querySelectorAll<HTMLButtonElement>('button')).find(
    (candidate) => (candidate.getAttribute('aria-label') ?? candidate.textContent?.trim()) === name,
  );
  if (!match) throw new Error(`Không thấy nút ${name}`);
  return match;
}

describe('AdminLayout', () => {
  it('nhóm mục menu theo khu vực và hiện tên Pickory', async () => {
    await renderLayout(adminActor);
    expect(container.textContent).toContain('Pickory');
    expect(container.textContent).not.toContain('Hải Đăng');
    const groupLabels = Array.from(container.querySelectorAll('nav [data-nav-group-label]')).map(
      (label) => label.textContent,
    );
    expect(groupLabels).toEqual(['Bán hàng', 'Sản phẩm', 'Báo cáo', 'Hệ thống']);
    expect(navLabels()).toEqual([
      'Tổng quan',
      'Đơn hàng',
      'Đối soát',
      'Khách hàng',
      'CSKH',
      'Đánh giá',
      'Sản phẩm',
      'Danh mục',
      'Thương hiệu',
      'Báo cáo tổng quan',
      'Doanh thu thương hiệu',
      'Doanh thu theo kỳ',
      'Nhân viên',
      'Quản lý Role',
      'Vận chuyển',
      'Phí ship dự phòng',
      'Cấu hình bán hàng',
      'Nhật ký thao tác',
    ]);
  });

  it('mục Vận chuyển chỉ hiện khi có quyền shipping-settings:manage', async () => {
    await renderLayout({
      ...salesActor,
      permissions: [...salesActor.permissions, 'shipping-settings:manage'],
    });
    expect(navLabels()).toContain('Vận chuyển');
    expect(navLink('Vận chuyển').getAttribute('href')).toBe('/admin/shipping/settings');
  });

  it('nhân viên thiếu quyền shipping-settings:manage không thấy mục Vận chuyển', async () => {
    await renderLayout(salesActor);
    expect(navLabels()).not.toContain('Vận chuyển');
  });

  it('mục Phí ship dự phòng chỉ hiện khi có quyền shipping-settings:manage', async () => {
    await renderLayout({
      ...salesActor,
      permissions: [...salesActor.permissions, 'shipping-settings:manage'],
    });
    expect(navLabels()).toContain('Phí ship dự phòng');
    expect(navLink('Phí ship dự phòng').getAttribute('href')).toBe('/admin/shipping/fallback-fee');
  });

  it('nhân viên thiếu quyền shipping-settings:manage không thấy mục Phí ship dự phòng', async () => {
    await renderLayout(salesActor);
    expect(navLabels()).not.toContain('Phí ship dự phòng');
  });

  it('ẩn mục thiếu quyền và ẩn nhãn nhóm không còn mục nào', async () => {
    await renderLayout(salesActor);
    expect(navLabels()).toEqual([
      'Tổng quan',
      'Đơn hàng',
      'Khách hàng',
      'Sản phẩm',
      'Danh mục',
      'Thương hiệu',
    ]);
    const groupLabels = Array.from(container.querySelectorAll('nav [data-nav-group-label]')).map(
      (label) => label.textContent,
    );
    expect(groupLabels).toEqual(['Bán hàng', 'Sản phẩm']);
  });

  it('đánh dấu mục của trang đang mở bằng aria-current, kể cả trang con', async () => {
    await renderLayout(adminActor, '/admin/products/abc');
    expect(navLink('Sản phẩm').getAttribute('aria-current')).toBe('page');
    expect(navLink('Tổng quan').getAttribute('aria-current')).toBeNull();
    expect(navLink('Danh mục').getAttribute('aria-current')).toBeNull();
  });

  it('chỉ đánh dấu Tổng quan khi đang ở trang tổng quan', async () => {
    await renderLayout(adminActor, '/admin');
    expect(navLink('Tổng quan').getAttribute('aria-current')).toBe('page');
  });

  it('đánh dấu đúng mục báo cáo đang mở', async () => {
    await renderLayout(adminActor, '/admin/reports/revenue/brands');
    expect(navLink('Doanh thu thương hiệu').getAttribute('aria-current')).toBe('page');
    expect(navLink('Doanh thu theo kỳ').getAttribute('aria-current')).toBeNull();
  });

  it('khối người dùng hiện tên, role và nút đăng xuất có nhãn truy cập', async () => {
    await renderLayout(salesActor);
    expect(container.textContent).toContain('Trần Bán Hàng');
    expect(container.textContent).toContain('sales');
    expect(button('Đăng xuất')).toBeTruthy();
  });

  it('icon trong menu bị ẩn khỏi trình đọc màn hình', async () => {
    await renderLayout(adminActor);
    const icons = Array.from(container.querySelectorAll('nav svg'));
    expect(icons.length).toBe(navLabels().length);
    for (const icon of icons) expect(icon.getAttribute('aria-hidden')).toBe('true');
  });

  it('nút thu gọn đổi trạng thái, giữ tên mục menu và nhớ lựa chọn khi mở lại', async () => {
    await renderLayout(adminActor);
    const collapseButton = button('Thu gọn menu');
    expect(collapseButton.getAttribute('aria-pressed')).toBe('false');
    await act(async () => collapseButton.click());
    expect(collapseButton.getAttribute('aria-pressed')).toBe('true');
    expect(container.querySelector('[data-collapsed="true"]')).not.toBeNull();
    // Nhãn chỉ ẩn bằng mắt. Link vẫn giữ tên để trình đọc màn hình đọc được.
    expect(navLink('Danh mục').textContent?.trim()).toBe('Danh mục');
    expect(window.localStorage.getItem('pickory.admin.sidebarCollapsed')).toBe('true');

    await act(async () => root.unmount());
    root = createRoot(container);
    await renderLayout(adminActor);
    expect(button('Thu gọn menu').getAttribute('aria-pressed')).toBe('true');
  });

  it('vẫn hiện khung khi trình duyệt chặn localStorage', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    await renderLayout(adminActor);
    expect(button('Thu gọn menu').getAttribute('aria-pressed')).toBe('false');
    vi.restoreAllMocks();
  });

  it('nút Menu mở ngăn kéo, phím Escape đóng và trả focus về nút Menu', async () => {
    await renderLayout(adminActor);
    const menuButton = button('Menu');
    const sidebarId = menuButton.getAttribute('aria-controls');
    expect(sidebarId).toBeTruthy();
    expect(container.querySelector(`#${sidebarId}`)).not.toBeNull();
    expect(menuButton.getAttribute('aria-expanded')).toBe('false');

    await act(async () => menuButton.click());
    expect(menuButton.getAttribute('aria-expanded')).toBe('true');
    expect(container.querySelector(`#${sidebarId}`)?.getAttribute('data-open')).toBe('true');

    await act(async () => {
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    });
    expect(menuButton.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(menuButton);
  });

  it('bấm lớp phủ hoặc chọn một trang thì đóng ngăn kéo', async () => {
    await renderLayout(adminActor);
    const menuButton = button('Menu');
    await act(async () => menuButton.click());
    const scrim = container.querySelector<HTMLElement>('[data-admin-scrim]');
    expect(scrim).not.toBeNull();
    await act(async () => scrim!.click());
    expect(menuButton.getAttribute('aria-expanded')).toBe('false');

    await act(async () => menuButton.click());
    await act(async () => navLink('Danh mục').click());
    expect(menuButton.getAttribute('aria-expanded')).toBe('false');
    expect(container.textContent).toContain('Trang con');
  });
});
