// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { CancellationsPeriodsReportPage } from '../../../../../src/features/admin-console/reports/cancellations-periods-page';
import { CancellationsReasonsReportPage } from '../../../../../src/features/admin-console/reports/cancellations-reasons-page';
import type { SalesReportData } from '../../../../../src/features/admin-console/reports/reports-api';
import type { ActorData } from '../../../../../src/features/identity/hooks/use-actor';
import {
  cleanupReportPage,
  clickButton,
  findField,
  installReportClock,
  renderReportPage,
  reportBucket,
  reportTotals,
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

// Hai ngày của tháng 10 có đơn hủy. Ngày 01 chỉ có đơn khách hủy, ngày 02 có cả ba lý do.
const monthReport: SalesReportData = {
  from: '2026-10-01',
  to: '2026-10-31',
  buckets: [
    reportBucket({
      key: '2026-10-01',
      from: '2026-10-01',
      to: '2026-10-01',
      cancellations: { byCustomer: 1, byShop: 0, paymentExpired: 0 },
    }),
    reportBucket({
      key: '2026-10-02',
      from: '2026-10-02',
      to: '2026-10-02',
      cancellations: { byCustomer: 2, byShop: 1, paymentExpired: 2 },
    }),
  ],
  brands: [],
  totals: reportTotals({
    cancellations: { byCustomer: 3, byShop: 1, paymentExpired: 2 },
  }),
  topProducts: [],
};

const emptyMonth: SalesReportData = {
  from: '2026-09-01',
  to: '2026-09-30',
  buckets: [],
  brands: [],
  totals: reportTotals(),
  topProducts: [],
};

// Hai kỳ của biểu đồ cột. Số đơn hủy mỗi kỳ là tổng ba lý do.
const periodReport: SalesReportData = {
  from: '2026-05-01',
  to: '2026-10-31',
  buckets: [
    reportBucket({
      key: '2026-05-01',
      from: '2026-05-01',
      to: '2026-05-31',
      cancellations: { byCustomer: 1, byShop: 0, paymentExpired: 0 },
    }),
    reportBucket({
      key: '2026-10-01',
      from: '2026-10-01',
      to: '2026-10-31',
      cancellations: { byCustomer: 2, byShop: 1, paymentExpired: 2 },
    }),
  ],
  brands: [],
  totals: reportTotals(),
  topProducts: [],
};

const emptyPeriod: SalesReportData = {
  from: '2026-10-20',
  to: '2026-10-27',
  buckets: [],
  brands: [],
  totals: reportTotals(),
  topProducts: [],
};

function respond(url: string) {
  if (!url.startsWith('/api/admin/reports/sales')) return [];
  const params = new URL(url, 'http://localhost').searchParams;
  if (params.get('range') === 'month') {
    return params.get('month') === '2026-09' ? emptyMonth : monthReport;
  }
  return params.get('range') === '7d' ? emptyPeriod : periodReport;
}

beforeEach(installReportClock);
afterEach(cleanupReportPage);

describe('trang đơn hủy theo lý do', () => {
  it('thiếu reports:read thì hiện thông báo không đủ quyền và không gọi API báo cáo', async () => {
    const harness = await renderReportPage(
      <CancellationsReasonsReportPage />,
      '/admin/reports/cancellations/reasons',
      { ...reportsActor, permissions: ['orders:read'] },
      () => [],
    );

    expect(textOf(harness.container)).toContain('Không có quyền truy cập');
    expect(harness.requestedUrls.some((url) => url.startsWith('/api/admin/reports/sales'))).toBe(
      false,
    );
  });

  it('biểu đồ tròn có ba lát theo lý do hủy của tháng', async () => {
    const harness = await renderReportPage(
      <CancellationsReasonsReportPage />,
      '/admin/reports/cancellations/reasons',
      reportsActor,
      respond,
    );

    await waitFor(() => textOf(harness.container).includes('Quá hạn thanh toán'), 'bảng lý do');
    expect(
      harness.requestedUrls.some(
        (url) =>
          url.includes('range=month') &&
          url.includes('month=2026-10') &&
          url.includes('granularity=day'),
      ),
    ).toBe(true);
    expect(harness.container.querySelectorAll('.report-legend-item')).toHaveLength(3);
    const rows = Array.from(harness.container.querySelectorAll('.report-table tbody tr'));
    expect(rows).toHaveLength(3);
    expect(rows[0]?.textContent).toContain('3');
    expect(rows[0]?.textContent).toContain('50%');
    expect(rows[1]?.textContent).toContain('1');
    expect(rows[1]?.textContent).toContain('17%');
    expect(rows[2]?.textContent).toContain('2');
    expect(rows[2]?.textContent).toContain('33%');
  });

  it('nhấn lát quá hạn thanh toán thì bảng chi tiết hiện số đơn theo từng ngày', async () => {
    const harness = await renderReportPage(
      <CancellationsReasonsReportPage />,
      '/admin/reports/cancellations/reasons',
      reportsActor,
      respond,
    );
    await waitFor(() => textOf(harness.container).includes('Quá hạn thanh toán'), 'bảng lý do');

    await clickButton(harness.container, 'Quá hạn thanh toán');

    await waitFor(
      () => harness.container.querySelector('.report-drilldown') !== null,
      'bảng chi tiết theo ngày',
    );
    const drilldown = harness.container.querySelector('.report-drilldown');
    expect(drilldown?.textContent).toContain(
      'Chi tiết Quá hạn thanh toán theo ngày, tháng 10/2026',
    );
    const rows = Array.from(drilldown?.querySelectorAll('tbody tr') ?? []);
    expect(rows).toHaveLength(2);
    expect(rows[0]?.textContent).toContain('01/10/2026');
    expect(rows[1]?.textContent).toContain('02/10/2026');
    expect(rows[1]?.textContent).toContain('2');
    expect(harness.searchParams().get('reason')).toBe('payment_expired');
  });

  it('mở lại liên kết có sẵn lý do thì bảng chi tiết mở sẵn', async () => {
    const harness = await renderReportPage(
      <CancellationsReasonsReportPage />,
      '/admin/reports/cancellations/reasons?reason=shop',
      reportsActor,
      respond,
    );

    await waitFor(
      () => harness.container.querySelector('.report-drilldown') !== null,
      'bảng chi tiết Shop hủy từ URL',
    );
    expect(textOf(harness.container)).toContain('Chi tiết Shop hủy theo ngày, tháng 10/2026');
    const rows = Array.from(harness.container.querySelectorAll('.report-drilldown tbody tr'));
    // Ngày 01 không có đơn shop hủy, ngày 02 có một đơn.
    expect(rows[0]?.textContent).toContain('0');
    expect(rows[1]?.textContent).toContain('1');
  });

  it('tháng không có đơn hủy hiện thông báo thay vì biểu đồ rỗng', async () => {
    const harness = await renderReportPage(
      <CancellationsReasonsReportPage />,
      '/admin/reports/cancellations/reasons?month=2026-09',
      reportsActor,
      respond,
    );

    await waitFor(
      () => textOf(harness.container).includes('Chưa có dữ liệu trong tháng này'),
      'thông báo tháng trống',
    );
    expect(harness.container.querySelector('.report-chart-legend')).toBeNull();
  });
});

describe('trang đơn hủy theo kỳ', () => {
  it('biểu đồ cột cộng số đơn hủy của cả ba lý do trong mỗi kỳ', async () => {
    const harness = await renderReportPage(
      <CancellationsPeriodsReportPage />,
      '/admin/reports/cancellations/periods',
      reportsActor,
      respond,
    );

    await waitFor(() => textOf(harness.container).includes('Tháng 5/2026'), 'bảng theo kỳ');
    const rows = Array.from(harness.container.querySelectorAll('.report-table tbody tr'));
    expect(rows).toHaveLength(2);
    expect(rows[0]?.textContent).toContain('1');
    expect(rows[1]?.textContent).toContain('5');
    expect(findField(harness.container, 'Khoảng thời gian')?.value).toBe('6m');
  });

  it('thiếu reports:read thì hiện thông báo không đủ quyền', async () => {
    const harness = await renderReportPage(
      <CancellationsPeriodsReportPage />,
      '/admin/reports/cancellations/periods',
      { ...reportsActor, permissions: [] },
      () => [],
    );

    expect(textOf(harness.container)).toContain('Không có quyền truy cập');
  });

  it('khoảng không có số liệu hiện thông báo thay vì biểu đồ rỗng', async () => {
    const harness = await renderReportPage(
      <CancellationsPeriodsReportPage />,
      '/admin/reports/cancellations/periods?range=7d&granularity=day',
      reportsActor,
      respond,
    );

    await waitFor(
      () => textOf(harness.container).includes('Chưa có dữ liệu trong khoảng này'),
      'thông báo khoảng trống',
    );
    expect(harness.container.querySelector('.report-chart-canvas')).toBeNull();
  });
});
