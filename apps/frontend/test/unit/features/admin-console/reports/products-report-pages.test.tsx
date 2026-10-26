// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ProductsPeriodsReportPage } from '../../../../../src/features/admin-console/reports/products-periods-page';
import { ProductsTopReportPage } from '../../../../../src/features/admin-console/reports/products-top-page';
import type {
  SalesReportData,
  SalesReportTopProduct,
} from '../../../../../src/features/admin-console/reports/reports-api';
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

function product(
  productId: string,
  name: string,
  quantity: number,
  revenue: number,
  brandName = 'Hãng Một',
): SalesReportTopProduct {
  return { productId, name, brandName, revenue, quantity };
}

// Bảy sản phẩm bán trong tháng 10. Năm sản phẩm đầu bán nhiều nhất, hai sản phẩm cuối vào nhóm Khác.
const topProducts: SalesReportTopProduct[] = [
  product('p1', 'Máy A', 70, 7_000_000, 'Hãng Một'),
  product('p2', 'Máy B', 60, 6_000_000, 'Hãng Hai'),
  product('p3', 'Máy C', 50, 5_000_000, 'Hãng Một'),
  product('p4', 'Máy D', 40, 4_000_000, 'Hãng Hai'),
  product('p5', 'Máy E', 30, 3_000_000, 'Hãng Một'),
  product('p6', 'Máy F', 20, 2_000_000, 'Hãng Hai'),
  product('p7', 'Máy G', 10, 1_000_000, 'Hãng Một'),
];

const monthReport: SalesReportData = {
  from: '2026-10-01',
  to: '2026-10-31',
  buckets: [],
  brands: [],
  totals: reportTotals({ soldQuantity: 280 }),
  topProducts,
};

const emptyMonth: SalesReportData = {
  from: '2026-09-01',
  to: '2026-09-30',
  buckets: [],
  brands: [],
  totals: reportTotals(),
  topProducts: [],
};

// Hai kỳ của biểu đồ cột, mỗi kỳ có số lượng sản phẩm bán ra riêng.
const periodReport: SalesReportData = {
  from: '2026-05-01',
  to: '2026-10-31',
  buckets: [
    reportBucket({
      key: '2026-05-01',
      from: '2026-05-01',
      to: '2026-05-31',
      soldQuantity: 120,
    }),
    reportBucket({
      key: '2026-10-01',
      from: '2026-10-01',
      to: '2026-10-31',
      soldQuantity: 45,
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

describe('trang sản phẩm bán ra theo sản phẩm', () => {
  it('thiếu reports:read thì hiện thông báo không đủ quyền và không gọi API báo cáo', async () => {
    const harness = await renderReportPage(
      <ProductsTopReportPage />,
      '/admin/reports/products/top',
      { ...reportsActor, permissions: ['products:read'] },
      () => [],
    );

    expect(textOf(harness.container)).toContain('Không có quyền truy cập');
    expect(harness.requestedUrls.some((url) => url.startsWith('/api/admin/reports/sales'))).toBe(
      false,
    );
  });

  it('biểu đồ tròn có năm sản phẩm bán nhiều nhất và lát Khác', async () => {
    const harness = await renderReportPage(
      <ProductsTopReportPage />,
      '/admin/reports/products/top',
      reportsActor,
      respond,
    );

    await waitFor(() => textOf(harness.container).includes('Máy A'), 'bảng sản phẩm');
    expect(harness.container.querySelectorAll('.report-legend-item')).toHaveLength(6);
    const rows = Array.from(harness.container.querySelectorAll('.report-table tbody tr'));
    expect(rows).toHaveLength(6);
    expect(rows[0]?.textContent).toContain('Máy A');
    expect(rows[0]?.textContent).toContain('70');
    expect(rows[5]?.textContent).toContain('Khác');
    // Nhóm Khác cộng số lượng của hai sản phẩm còn lại.
    expect(rows[5]?.textContent).toContain('30');
  });

  it('nhấn lát một sản phẩm thì bảng chi tiết hiện thương hiệu, doanh thu và số lượng', async () => {
    const harness = await renderReportPage(
      <ProductsTopReportPage />,
      '/admin/reports/products/top',
      reportsActor,
      respond,
    );
    await waitFor(() => textOf(harness.container).includes('Máy A'), 'bảng sản phẩm');

    await clickButton(harness.container, 'Máy A');

    await waitFor(
      () => harness.container.querySelector('.report-drilldown') !== null,
      'bảng chi tiết sản phẩm',
    );
    const drilldown = harness.container.querySelector('.report-drilldown');
    expect(drilldown?.textContent).toContain('Chi tiết Máy A');
    const rows = Array.from(drilldown?.querySelectorAll('tbody tr') ?? []);
    expect(rows).toHaveLength(1);
    expect(rows[0]?.textContent).toContain('Hãng Một');
    expect(rows[0]?.textContent).toContain('7.000.000 đ');
    expect(rows[0]?.textContent).toContain('70');
  });

  it('nhấn lát Khác thì bảng chi tiết liệt kê các sản phẩm trong nhóm', async () => {
    const harness = await renderReportPage(
      <ProductsTopReportPage />,
      '/admin/reports/products/top',
      reportsActor,
      respond,
    );
    await waitFor(() => textOf(harness.container).includes('Máy A'), 'bảng sản phẩm');

    await clickButton(harness.container, 'Khác');

    await waitFor(
      () => harness.container.querySelector('.report-drilldown') !== null,
      'danh sách sản phẩm nhóm Khác',
    );
    const drilldown = harness.container.querySelector('.report-drilldown');
    expect(drilldown?.textContent).toContain('Các sản phẩm trong nhóm Khác');
    const rows = Array.from(drilldown?.querySelectorAll('tbody tr') ?? []);
    expect(rows).toHaveLength(2);
    expect(rows[0]?.textContent).toContain('Máy F');
    expect(rows[1]?.textContent).toContain('Máy G');
    expect(harness.searchParams().get('product')).toBe('other');
  });

  it('mở lại liên kết có sẵn sản phẩm thì bảng chi tiết mở sẵn', async () => {
    const harness = await renderReportPage(
      <ProductsTopReportPage />,
      '/admin/reports/products/top?product=p3',
      reportsActor,
      respond,
    );

    await waitFor(
      () => harness.container.querySelector('.report-drilldown') !== null,
      'bảng chi tiết sản phẩm từ URL',
    );
    expect(textOf(harness.container)).toContain('Chi tiết Máy C');
    const rows = Array.from(harness.container.querySelectorAll('.report-drilldown tbody tr'));
    expect(rows).toHaveLength(1);
    expect(rows[0]?.textContent).toContain('5.000.000 đ');
  });

  it('tháng không có sản phẩm bán ra hiện thông báo thay vì biểu đồ rỗng', async () => {
    const harness = await renderReportPage(
      <ProductsTopReportPage />,
      '/admin/reports/products/top?month=2026-09',
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

describe('trang sản phẩm bán ra theo kỳ', () => {
  it('biểu đồ cột hiện số lượng sản phẩm bán ra mỗi kỳ', async () => {
    const harness = await renderReportPage(
      <ProductsPeriodsReportPage />,
      '/admin/reports/products/periods',
      reportsActor,
      respond,
    );

    await waitFor(() => textOf(harness.container).includes('Tháng 5/2026'), 'bảng theo kỳ');
    const rows = Array.from(harness.container.querySelectorAll('.report-table tbody tr'));
    expect(rows).toHaveLength(2);
    expect(rows[0]?.textContent).toContain('120');
    expect(rows[1]?.textContent).toContain('45');
    expect(findField(harness.container, 'Chia theo')?.value).toBe('month');
  });

  it('thiếu reports:read thì hiện thông báo không đủ quyền', async () => {
    const harness = await renderReportPage(
      <ProductsPeriodsReportPage />,
      '/admin/reports/products/periods',
      { ...reportsActor, permissions: [] },
      () => [],
    );

    expect(textOf(harness.container)).toContain('Không có quyền truy cập');
  });

  it('khoảng không có số liệu hiện thông báo thay vì biểu đồ rỗng', async () => {
    const harness = await renderReportPage(
      <ProductsPeriodsReportPage />,
      '/admin/reports/products/periods?range=7d&granularity=day',
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
