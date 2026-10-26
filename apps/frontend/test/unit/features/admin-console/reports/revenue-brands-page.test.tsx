// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { RevenueBrandsReportPage } from '../../../../../src/features/admin-console/reports/revenue-brands-page';
import type { SalesReportData } from '../../../../../src/features/admin-console/reports/reports-api';
import type { ActorData } from '../../../../../src/features/identity/hooks/use-actor';
import {
  cleanupReportPage,
  findField,
  installReportClock,
  renderReportPage,
  selectOption,
  textOf,
  waitFor,
} from './page-test-kit';

const reportsActor: ActorData = {
  anonymousId: 'anonymous-report',
  userId: 'user-report',
  email: 'baocao@pickory.local',
  name: 'Người Xem Báo Cáo',
  kind: 'staff',
  roles: ['sales'],
  permissions: ['reports:read'],
  consent: { personalization: false, marketing: false },
};

const brandReport: SalesReportData = {
  from: '2026-10-01',
  to: '2026-10-31',
  buckets: [],
  brands: [
    { brandId: 'b1', brandName: 'Hãng Một', revenue: 1_234_567 },
    { brandId: 'b2', brandName: 'Hãng Hai', revenue: 234_567 },
  ],
};

const emptyMonth: SalesReportData = {
  from: '2026-09-01',
  to: '2026-09-30',
  buckets: [],
  brands: [],
};

// Trả dữ liệu theo tháng của request, giống route báo cáo thật.
function respond(url: string) {
  if (!url.startsWith('/api/admin/reports/sales')) return [];
  const params = new URL(url, 'http://localhost').searchParams;
  return params.get('month') === '2026-09' ? emptyMonth : brandReport;
}

beforeEach(installReportClock);
afterEach(cleanupReportPage);

describe('trang báo cáo doanh thu theo thương hiệu', () => {
  it('thiếu reports:read thì hiện thông báo không đủ quyền và không gọi API báo cáo', async () => {
    const harness = await renderReportPage(
      <RevenueBrandsReportPage />,
      '/admin/reports/revenue/brands',
      { ...reportsActor, permissions: ['products:read'] },
      () => [],
    );

    expect(textOf(harness.container)).toContain('Không có quyền truy cập');
    expect(harness.requestedUrls.some((url) => url.startsWith('/api/admin/reports/sales'))).toBe(
      false,
    );
  });

  it('chỉ có một biểu đồ, bảng thương hiệu bằng VND và không có bảng sản phẩm', async () => {
    const harness = await renderReportPage(
      <RevenueBrandsReportPage />,
      '/admin/reports/revenue/brands',
      reportsActor,
      respond,
    );

    await waitFor(() => textOf(harness.container).includes('Hãng Một'), 'bảng thương hiệu');
    expect(
      harness.requestedUrls.some(
        (url) => url.includes('range=month') && url.includes('month=2026-10'),
      ),
    ).toBe(true);
    expect(findField(harness.container, 'Tháng')?.value).toBe('2026-10');
    expect(textOf(harness.container)).toContain('1.234.567 đ');
    expect(textOf(harness.container)).toContain('84%');
    expect(harness.container.querySelectorAll('.report-legend-item')).toHaveLength(2);
    expect(harness.container.querySelectorAll('.report-table tbody tr')).toHaveLength(2);
    // Trang chỉ có biểu đồ và bảng số liệu, không còn bảng sản phẩm chi tiết.
    expect(harness.container.querySelector('.report-detail')).toBeNull();
  });

  it('tháng không có số liệu hiện thông báo thay vì biểu đồ rỗng', async () => {
    const harness = await renderReportPage(
      <RevenueBrandsReportPage />,
      '/admin/reports/revenue/brands',
      reportsActor,
      respond,
    );
    await waitFor(() => textOf(harness.container).includes('Hãng Một'), 'bảng thương hiệu');

    const monthSelect = findField(harness.container, 'Tháng');
    if (!monthSelect) throw new Error('Thiếu ô chọn tháng.');
    await selectOption(monthSelect, '2026-09');

    await waitFor(
      () => textOf(harness.container).includes('Chưa có dữ liệu trong tháng này'),
      'thông báo tháng trống',
    );
    expect(
      harness.requestedUrls.some(
        (url) => url.includes('range=month') && url.includes('month=2026-09'),
      ),
    ).toBe(true);
    expect(harness.searchParams().get('month')).toBe('2026-09');
    expect(harness.searchParams().has('range')).toBe(false);
    expect(harness.container.querySelector('.report-chart-legend')).toBeNull();
  });

  it('mở lại URL đã chọn tháng', async () => {
    const harness = await renderReportPage(
      <RevenueBrandsReportPage />,
      '/admin/reports/revenue/brands?month=2026-09',
      reportsActor,
      respond,
    );

    await waitFor(
      () => textOf(harness.container).includes('Chưa có dữ liệu trong tháng này'),
      'tháng trống từ URL',
    );
    expect(findField(harness.container, 'Tháng')?.value).toBe('2026-09');
    expect(
      harness.requestedUrls.some(
        (url) => url.includes('range=month') && url.includes('month=2026-09'),
      ),
    ).toBe(true);
  });
});
