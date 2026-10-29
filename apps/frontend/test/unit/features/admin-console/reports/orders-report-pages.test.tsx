// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { OrdersPaymentsReportPage } from '../../../../../src/features/admin-console/reports/orders-payments-page';
import { OrdersPeriodsReportPage } from '../../../../../src/features/admin-console/reports/orders-periods-page';
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

// Hai ngày của tháng 10 có đơn COD và SePay. Ngày 01 chỉ có COD, ngày 02 có cả hai phương thức.
const monthReport: SalesReportData = {
  from: '2026-10-01',
  to: '2026-10-31',
  buckets: [
    reportBucket({
      key: '2026-10-01',
      from: '2026-10-01',
      to: '2026-10-01',
      paidOrders: 2,
      paidByMethod: { cod: 2, sepay: 0 },
    }),
    reportBucket({
      key: '2026-10-02',
      from: '2026-10-02',
      to: '2026-10-02',
      paidOrders: 4,
      paidByMethod: { cod: 3, sepay: 1 },
    }),
  ],
  brands: [],
  totals: reportTotals({
    recognizedRevenue: 6_000_000,
    paidOrders: 6,
    paidByMethod: { cod: 5, sepay: 1 },
  }),
  topProducts: [],
};

// Tháng không có đơn nào, nên cả hai phương thức đều bằng 0.
const emptyMonth: SalesReportData = {
  from: '2026-09-01',
  to: '2026-09-30',
  buckets: [],
  brands: [],
  totals: reportTotals(),
  topProducts: [],
};

// Hai kỳ của biểu đồ cột, mỗi kỳ có số đơn đã thanh toán riêng.
const periodReport: SalesReportData = {
  from: '2026-05-01',
  to: '2026-10-31',
  buckets: [
    reportBucket({
      key: '2026-05-01',
      from: '2026-05-01',
      to: '2026-05-31',
      paidOrders: 4,
    }),
    reportBucket({
      key: '2026-10-01',
      from: '2026-10-01',
      to: '2026-10-31',
      paidOrders: 9,
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

// Trả dữ liệu theo tham số của request, giống route báo cáo thật.
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

describe('trang đơn đã thanh toán theo phương thức', () => {
  it('thiếu reports:read thì hiện thông báo không đủ quyền và không gọi API báo cáo', async () => {
    const harness = await renderReportPage(
      <OrdersPaymentsReportPage />,
      '/admin/reports/orders/payments',
      { ...reportsActor, permissions: ['products:read'] },
      () => [],
    );

    expect(textOf(harness.container)).toContain('Không có quyền truy cập');
    expect(harness.requestedUrls.some((url) => url.startsWith('/api/admin/reports/sales'))).toBe(
      false,
    );
  });

  it('chỉ có một biểu đồ tròn theo tháng với hai lát COD và SePay', async () => {
    const harness = await renderReportPage(
      <OrdersPaymentsReportPage />,
      '/admin/reports/orders/payments',
      reportsActor,
      respond,
    );

    await waitFor(() => textOf(harness.container).includes('SePay'), 'bảng phương thức');
    expect(
      harness.requestedUrls.some(
        (url) =>
          url.includes('range=month') &&
          url.includes('month=2026-10') &&
          url.includes('granularity=day'),
      ),
    ).toBe(true);
    expect(findField(harness.container, 'Tháng')?.value).toBe('2026-10');
    expect(harness.container.querySelectorAll('.report-legend-item')).toHaveLength(2);
    expect(harness.container.querySelectorAll('.report-table tbody tr')).toHaveLength(2);
    // Số đơn của tháng và tỉ lệ của từng phương thức nằm trong bảng.
    const rows = Array.from(harness.container.querySelectorAll('.report-table tbody tr'));
    expect(rows[0]?.textContent).toContain('5');
    expect(rows[0]?.textContent).toContain('83%');
    expect(rows[1]?.textContent).toContain('1');
    expect(rows[1]?.textContent).toContain('17%');
  });

  it('nhấn lát COD thì bảng chi tiết hiện số đơn COD theo từng ngày', async () => {
    const harness = await renderReportPage(
      <OrdersPaymentsReportPage />,
      '/admin/reports/orders/payments',
      reportsActor,
      respond,
    );
    await waitFor(() => textOf(harness.container).includes('COD'), 'bảng phương thức');
    expect(harness.container.querySelector('.report-drilldown')).toBeNull();

    await clickButton(harness.container, 'COD');

    await waitFor(
      () => harness.container.querySelector('.report-drilldown') !== null,
      'bảng chi tiết theo ngày',
    );
    const drilldown = harness.container.querySelector('.report-drilldown');
    expect(drilldown?.textContent).toContain('Chi tiết COD theo ngày, tháng 10/2026');
    const rows = Array.from(drilldown?.querySelectorAll('tbody tr') ?? []);
    expect(rows).toHaveLength(2);
    expect(rows[0]?.textContent).toContain('01/10/2026');
    expect(rows[1]?.textContent).toContain('02/10/2026');
    expect(rows[1]?.textContent).toContain('3');
    expect(harness.searchParams().get('method')).toBe('cod');
  });

  it('mở lại liên kết có sẵn phương thức thì bảng chi tiết mở sẵn', async () => {
    const harness = await renderReportPage(
      <OrdersPaymentsReportPage />,
      '/admin/reports/orders/payments?method=sepay',
      reportsActor,
      respond,
    );

    await waitFor(
      () => harness.container.querySelector('.report-drilldown') !== null,
      'bảng chi tiết SePay từ URL',
    );
    expect(textOf(harness.container)).toContain('Chi tiết SePay theo ngày, tháng 10/2026');
    const pressed = Array.from(
      harness.container.querySelectorAll('.report-legend-item button[aria-pressed="true"]'),
    );
    expect(pressed).toHaveLength(1);
    expect(pressed[0]?.textContent).toContain('SePay');
  });

  it('tháng không có đơn hiện thông báo thay vì biểu đồ rỗng', async () => {
    const harness = await renderReportPage(
      <OrdersPaymentsReportPage />,
      '/admin/reports/orders/payments?month=2026-09',
      reportsActor,
      respond,
    );

    await waitFor(
      () => textOf(harness.container).includes('Chưa có dữ liệu trong tháng này'),
      'thông báo tháng trống',
    );
    expect(harness.container.querySelector('.report-chart-legend')).toBeNull();
    expect(harness.container.querySelector('.report-drilldown')).toBeNull();
  });

  it('đổi tháng cập nhật URL và tải lại số liệu tháng mới', async () => {
    const harness = await renderReportPage(
      <OrdersPaymentsReportPage />,
      '/admin/reports/orders/payments',
      reportsActor,
      respond,
    );
    await waitFor(() => textOf(harness.container).includes('SePay'), 'bảng phương thức');

    const monthSelect = findField(harness.container, 'Tháng');
    if (!monthSelect) throw new Error('Thiếu ô chọn tháng.');
    await selectOption(monthSelect, '2026-09');

    await waitFor(
      () => textOf(harness.container).includes('Chưa có dữ liệu trong tháng này'),
      'tháng 9 trống',
    );
    expect(harness.searchParams().get('month')).toBe('2026-09');
    expect(
      harness.requestedUrls.some(
        (url) => url.includes('range=month') && url.includes('month=2026-09'),
      ),
    ).toBe(true);
  });
});

describe('trang đơn đã thanh toán theo kỳ', () => {
  it('thiếu reports:read thì hiện thông báo không đủ quyền', async () => {
    const harness = await renderReportPage(
      <OrdersPeriodsReportPage />,
      '/admin/reports/orders/periods',
      { ...reportsActor, permissions: [] },
      () => [],
    );

    expect(textOf(harness.container)).toContain('Không có quyền truy cập');
  });

  it('chỉ có một biểu đồ cột, mặc định sáu tháng gần nhất chia theo tháng', async () => {
    const harness = await renderReportPage(
      <OrdersPeriodsReportPage />,
      '/admin/reports/orders/periods',
      reportsActor,
      respond,
    );

    await waitFor(() => textOf(harness.container).includes('Tháng 5/2026'), 'bảng theo kỳ');
    expect(
      harness.requestedUrls.some(
        (url) => url.includes('range=6m') && url.includes('granularity=month'),
      ),
    ).toBe(true);
    expect(findField(harness.container, 'Khoảng thời gian')?.value).toBe('6m');
    expect(findField(harness.container, 'Chia theo')?.value).toBe('month');
    const rows = Array.from(harness.container.querySelectorAll('.report-table tbody tr'));
    expect(rows).toHaveLength(2);
    expect(rows[0]?.textContent).toContain('4');
    expect(rows[1]?.textContent).toContain('9');
    // Trang cột không mở bảng chi tiết khi nhấn.
    expect(harness.container.querySelector('.report-drilldown')).toBeNull();
  });

  it('khoảng không có số liệu hiện thông báo thay vì biểu đồ rỗng', async () => {
    const harness = await renderReportPage(
      <OrdersPeriodsReportPage />,
      '/admin/reports/orders/periods?range=7d&granularity=day',
      reportsActor,
      respond,
    );

    await waitFor(
      () => textOf(harness.container).includes('Chưa có dữ liệu trong khoảng này'),
      'thông báo khoảng trống',
    );
    expect(harness.container.querySelector('.report-chart-canvas')).toBeNull();
  });

  it('mở lại URL đã chọn khoảng thời gian và độ chia', async () => {
    const harness = await renderReportPage(
      <OrdersPeriodsReportPage />,
      '/admin/reports/orders/periods?range=30d&granularity=day',
      reportsActor,
      respond,
    );

    await waitFor(() => textOf(harness.container).includes('01/05/2026'), 'bảng theo ngày');
    expect(findField(harness.container, 'Khoảng thời gian')?.value).toBe('30d');
    expect(findField(harness.container, 'Chia theo')?.value).toBe('day');
    expect(
      harness.requestedUrls.some(
        (url) => url.includes('range=30d') && url.includes('granularity=day'),
      ),
    ).toBe(true);
  });
});
