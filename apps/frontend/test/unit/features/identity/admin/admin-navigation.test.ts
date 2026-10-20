import { describe, expect, it } from 'vitest';
import {
  ADMIN_NAV_GROUPS,
  filterNavGroupsByPermission,
  isNavItemActive,
} from '../../../../../src/features/identity/admin/admin-navigation';
import type { ActorData } from '../../../../../src/features/identity/hooks/use-actor';

const baseActor: ActorData = {
  anonymousId: 'anon',
  userId: 'user-1',
  kind: 'staff',
  roles: ['sales'],
  permissions: [],
  consent: { personalization: false, marketing: false },
};

function visibleItems(actor: ActorData) {
  return filterNavGroupsByPermission(ADMIN_NAV_GROUPS, actor).flatMap((group) => group.items);
}

describe('menu back-office sau khi gỡ màn cấu hình AI', () => {
  it('không còn mục Cấu hình AI hay đường dẫn /admin/ai', () => {
    const items = ADMIN_NAV_GROUPS.flatMap((group) => group.items);

    expect(items.map((item) => item.label)).not.toContain('Cấu hình AI');
    expect(items.map((item) => item.path)).not.toContain('/admin/ai');
  });
});

describe('mục menu Cấu hình bán hàng', () => {
  it('chỉ hiện với người có quyền commerce-settings:manage', () => {
    const manager = { ...baseActor, permissions: ['commerce-settings:manage'] };

    const labels = visibleItems(manager).map((item) => item.label);

    expect(labels).toContain('Cấu hình bán hàng');
  });

  it('ẩn với người có quyền khác như shipping-settings:manage hoặc orders:read', () => {
    const other = { ...baseActor, permissions: ['shipping-settings:manage', 'orders:read'] };

    const labels = visibleItems(other).map((item) => item.label);

    expect(labels).not.toContain('Cấu hình bán hàng');
    expect(labels).toContain('Vận chuyển');
  });

  it('trỏ tới /admin/commerce/settings và nằm ngay sau mục Phí ship dự phòng', () => {
    const systemGroup = ADMIN_NAV_GROUPS.find((group) =>
      group.items.some((item) => item.label === 'Vận chuyển'),
    );
    const labels = systemGroup?.items.map((item) => item.label) ?? [];
    const item = systemGroup?.items.find((entry) => entry.label === 'Cấu hình bán hàng');

    expect(item?.path).toBe('/admin/commerce/settings');
    expect(item?.requiredPermission).toBe('commerce-settings:manage');
    expect(labels.indexOf('Cấu hình bán hàng')).toBe(labels.indexOf('Phí ship dự phòng') + 1);
  });

  it('sáng khi đang ở đúng đường dẫn và không sáng ở màn khác', () => {
    const item = ADMIN_NAV_GROUPS.flatMap((group) => group.items).find(
      (entry) => entry.label === 'Cấu hình bán hàng',
    );
    if (!item) throw new Error('Thiếu mục menu Cấu hình bán hàng');

    expect(isNavItemActive(item, '/admin/commerce/settings')).toBe(true);
    expect(isNavItemActive(item, '/admin/shipping/settings')).toBe(false);
  });
});

describe('mục menu Phí ship dự phòng', () => {
  it('chỉ hiện với người có quyền shipping-settings:manage', () => {
    const manager = { ...baseActor, permissions: ['shipping-settings:manage'] };

    expect(visibleItems(manager).map((item) => item.label)).toContain('Phí ship dự phòng');
  });

  it('ẩn với tài khoản support hoặc sales không có quyền shipping-settings:manage', () => {
    const support = { ...baseActor, roles: ['support'], permissions: ['conversations:read'] };
    const sales = { ...baseActor, roles: ['sales'], permissions: ['orders:read', 'products:read'] };

    expect(visibleItems(support).map((item) => item.label)).not.toContain('Phí ship dự phòng');
    expect(visibleItems(sales).map((item) => item.label)).not.toContain('Phí ship dự phòng');
  });

  it('trỏ tới /admin/shipping/fallback-fee và nằm ngay sau mục Vận chuyển', () => {
    const systemGroup = ADMIN_NAV_GROUPS.find((group) =>
      group.items.some((item) => item.label === 'Vận chuyển'),
    );
    const labels = systemGroup?.items.map((item) => item.label) ?? [];
    const item = systemGroup?.items.find((entry) => entry.label === 'Phí ship dự phòng');

    expect(item?.path).toBe('/admin/shipping/fallback-fee');
    expect(item?.requiredPermission).toBe('shipping-settings:manage');
    expect(labels.indexOf('Phí ship dự phòng')).toBe(labels.indexOf('Vận chuyển') + 1);
  });

  it('sáng khi đang ở đúng đường dẫn, và mục Vận chuyển không sáng theo', () => {
    const items = ADMIN_NAV_GROUPS.flatMap((group) => group.items);
    const fallback = items.find((entry) => entry.label === 'Phí ship dự phòng');
    const shipping = items.find((entry) => entry.label === 'Vận chuyển');
    if (!fallback || !shipping) throw new Error('Thiếu mục menu Vận chuyển hoặc Phí ship dự phòng');

    expect(isNavItemActive(fallback, '/admin/shipping/fallback-fee')).toBe(true);
    expect(isNavItemActive(fallback, '/admin/shipping/settings')).toBe(false);
    expect(isNavItemActive(shipping, '/admin/shipping/fallback-fee')).toBe(false);
  });
});

describe('mục menu Đối soát', () => {
  it('hiện với người có quyền payments:read', () => {
    const reader = { ...baseActor, permissions: ['payments:read'] };

    expect(visibleItems(reader).map((item) => item.label)).toContain('Đối soát');
  });

  it('ẩn với người chỉ có payments:reconcile hoặc orders:read', () => {
    const other = { ...baseActor, permissions: ['payments:reconcile', 'orders:read'] };

    expect(visibleItems(other).map((item) => item.label)).not.toContain('Đối soát');
  });

  it('trỏ tới /admin/payments và nằm ngay sau mục Đơn hàng trong nhóm Bán hàng', () => {
    const salesGroup = ADMIN_NAV_GROUPS.find((group) => group.label === 'Bán hàng');
    const labels = salesGroup?.items.map((item) => item.label) ?? [];
    const item = salesGroup?.items.find((entry) => entry.label === 'Đối soát');

    expect(item?.path).toBe('/admin/payments');
    expect(item?.requiredPermission).toBe('payments:read');
    expect(labels.indexOf('Đối soát')).toBe(labels.indexOf('Đơn hàng') + 1);
  });

  it('mục Đối soát đang mở ở /admin/payments và không mở ở /admin/orders', () => {
    const salesGroup = ADMIN_NAV_GROUPS.find((group) => group.label === 'Bán hàng')!;
    const item = salesGroup.items.find((entry) => entry.label === 'Đối soát')!;

    expect(isNavItemActive(item, '/admin/payments')).toBe(true);
    expect(isNavItemActive(item, '/admin/orders')).toBe(false);
  });
});
