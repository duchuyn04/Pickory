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

async function renderHeader(actor?: ActorData, url = '/', override?: FakeApiOverride) {
  const api = stubCartApi(buildCart([]), override);
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
  it('có link Tra cứu đơn tới trang /order-lookup trong nhóm điều hướng chính', async () => {
    stubCartApi(buildCart([]));
    await renderWithProviders(root, <Header />);

    const navigation = container.querySelector('nav[aria-label="Điều hướng chính"]');
    const lookupLink = Array.from(navigation?.querySelectorAll('a') ?? []).find(
      (link) => link.textContent === 'Tra cứu đơn',
    );
    expect(lookupLink?.getAttribute('href')).toBe('/order-lookup');
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
      expectedLinks: ['Trang chủ', 'Tra cứu đơn', 'Quyền riêng tư', 'Đăng nhập', 'Đăng ký'],
      unexpectedTexts: ['Tài khoản', 'Đăng xuất', 'Back-Office'],
    },
    {
      actor: customerActor,
      description: 'khách đăng nhập',
      expectedLinks: ['Trang chủ', 'Tra cứu đơn', 'Quyền riêng tư', 'Tài khoản'],
      unexpectedTexts: ['Đăng nhập', 'Đăng ký', 'Back-Office'],
    },
    {
      actor: staffActor,
      description: 'nhân viên',
      expectedLinks: ['Trang chủ', 'Tra cứu đơn', 'Quyền riêng tư', 'Back-Office'],
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
