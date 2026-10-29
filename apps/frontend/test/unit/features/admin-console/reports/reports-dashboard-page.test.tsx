// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ReportsDashboardPage } from '../../../../../src/features/admin-console/reports/reports-dashboard-page';
import type {
  SalesReportData,
  SalesReportTopProduct,
} from '../../../../../src/features/admin-console/reports/reports-api';
import type { ActorData } from '../../../../../src/features/identity/hooks/use-actor';
import {
  cleanupReportPage,
  clickButton,
  installReportClock,
  renderReportPage,
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
): SalesReportTopProduct {
  return { productId, name, brandName: 'Hãng Một', revenue, quantity };
}

// Số liệu tháng 10 đủ cho bốn thẻ: hai thương hiệu, hai phương thức, ba lý do hủy, bảy sản phẩm.
const monthReport: SalesReportData = {
  from: '2026-10-01',
  to: '2026-10-31',
  buckets: [],
  brands: [
    { brandId: 'b1', brandName: 'Hãng Một', revenue: 1_234_567 },
    { brandId: 'b2', brandName: 'Hãng Hai', revenue: 234_567 },
  ],
  totals: reportTotals({
    recognizedRevenue: 1_469_134,
    paidOrders: 6,
    soldQuantity: 280,
    paidByMethod: { cod: 5, sepay: 1 },
    cancellations: { byCustomer: 3, byShop: 1, paymentExpired: 2 },
  }),
  topProducts: [
    product('p1', 'Máy A', 70, 7_000_000),
    product('p2', 'Máy B', 60, 6_000_000),
    product('p3', 'Máy C', 50, 5_000_000),
    product('p4', 'Máy D', 40, 4_000_000),
    product('p5', 'Máy E', 30, 3_000_000),
    product('p6', 'Máy F', 20, 2_000_000),
    product('p7', 'Máy G', 10, 1_000_000),
  ],
};

beforeEach(installReportClock);
afterEach(cleanupReportPage);

// Đọc thẻ theo tiêu đề để kiểm số của đúng báo cáo.
function findCard(container: HTMLElement, title: string): Element {
  const card = Array.from(container.querySelectorAll('.report-card')).find((candidate) =>
    (candidate.querySelector('h2')?.textContent ?? '').includes(title),
  );
  if (!card) throw new Error(`Không thấy thẻ ${title}.`);
  return card;
}

describe('trang tổng quan báo cáo', () => {
  it('thiếu reports:read thì hiện thông báo không đủ quyền và không gọi API báo cáo', async () => {
    const harness = await renderReportPage(
      <ReportsDashboardPage />,
      '/admin/reports',
      { ...reportsActor, permissions: ['orders:read'] },
      () => [],
    );

    expect(textOf(harness.container)).toContain('Không có quyền truy cập');
    expect(harness.requestedUrls.some((url) => url.startsWith('/api/admin/reports/sales'))).toBe(
      false,
    );
  });

  it('có bốn thẻ, mỗi thẻ một biểu đồ tròn và tổng của tháng', async () => {
    const harness = await renderReportPage(
      <ReportsDashboardPage />,
      '/admin/reports',
      reportsActor,
      () => monthReport,
    );

    await waitFor(() => textOf(harness.container).includes('Máy A'), 'bốn thẻ đã tải');

    expect(harness.container.querySelectorAll('.report-card')).toHaveLength(4);
    expect(harness.container.querySelectorAll('.report-chart-legend')).toHaveLength(4);

    const revenueCard = findCard(harness.container, 'Doanh thu');
    expect(revenueCard.textContent).toContain('Tổng doanh thu tháng này');
    expect(revenueCard.textContent).toContain('1.469.134 đ');

    const ordersCard = findCard(harness.container, 'Đơn đã thanh toán');
    expect(ordersCard.textContent).toContain('Tổng đơn đã thanh toán tháng này');
    expect(ordersCard.querySelectorAll('.report-legend-item')).toHaveLength(2);

    const cancellationsCard = findCard(harness.container, 'Đơn hủy');
    expect(cancellationsCard.textContent).toContain('Tổng đơn hủy tháng này');
    expect(cancellationsCard.querySelectorAll('.report-legend-item')).toHaveLength(3);

    const productsCard = findCard(harness.container, 'Sản phẩm bán ra');
    expect(productsCard.textContent).toContain('Tổng sản phẩm bán ra tháng này');
    expect(productsCard.querySelectorAll('.report-legend-item')).toHaveLength(6);
  });

  it('mỗi thẻ có liên kết tới trang chi tiết kèm tháng đang xem', async () => {
    const harness = await renderReportPage(
      <ReportsDashboardPage />,
      '/admin/reports',
      reportsActor,
      () => monthReport,
    );
    await waitFor(() => textOf(harness.container).includes('Máy A'), 'bốn thẻ đã tải');

    const hrefs = Array.from(
      harness.container.querySelectorAll<HTMLAnchorElement>('.report-card h2 a'),
    ).map((link) => link.getAttribute('href'));

    expect(hrefs).toEqual([
      '/admin/reports/revenue/brands?month=2026-10',
      '/admin/reports/orders/payments?month=2026-10',
      '/admin/reports/cancellations/reasons?month=2026-10',
      '/admin/reports/products/top?month=2026-10',
    ]);
  });

  it('nhấn một lát mở trang chi tiết đã chọn sẵn lát đó', async () => {
    const harness = await renderReportPage(
      <ReportsDashboardPage />,
      '/admin/reports',
      reportsActor,
      () => monthReport,
    );
    await waitFor(() => textOf(harness.container).includes('Máy A'), 'bốn thẻ đã tải');

    await clickButton(findCard(harness.container, 'Đơn đã thanh toán') as HTMLElement, 'COD');

    await waitFor(
      () => harness.location().startsWith('/admin/reports/orders/payments'),
      'mở trang đơn đã thanh toán',
    );
    expect(harness.location()).toBe('/admin/reports/orders/payments?month=2026-10&method=cod');
  });

  it('một thẻ lỗi API thì chỉ thẻ đó hiện lỗi', async () => {
    let requestCount = 0;
    const harness = await renderReportPage(
      <ReportsDashboardPage />,
      '/admin/reports',
      reportsActor,
      () => {
        requestCount += 1;
        // Chỉ lần gọi đầu tiên lỗi, để kiểm một thẻ hỏng không kéo theo ba thẻ còn lại.
        return requestCount === 1 ? new Error('Không tải được báo cáo.') : monthReport;
      },
    );

    await waitFor(
      () => harness.container.querySelectorAll('[role="alert"]').length === 1,
      'một thẻ báo lỗi',
    );
    expect(harness.container.querySelectorAll('.report-chart-legend')).toHaveLength(3);
  });
});
