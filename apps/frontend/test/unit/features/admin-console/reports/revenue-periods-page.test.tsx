// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { RevenuePeriodsReportPage } from '../../../../../src/features/admin-console/reports/revenue-periods-page';
import type { SalesReportData } from '../../../../../src/features/admin-console/reports/reports-api';
import type { ActorData } from '../../../../../src/features/identity/hooks/use-actor';
import {
  cleanupReportPage,
  findField,
  installReportClock,
  renderReportPage,
  reportBucket,
  reportTotals,
  selectOption,
  textOf,
  waitFor,
  type ReportPageHarness,
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

const periodReport: SalesReportData = {
  from: '2026-05-01',
  to: '2026-10-15',
  buckets: [
    reportBucket({
      key: '2026-05-01',
      from: '2026-05-01',
      to: '2026-05-31',
      recognizedRevenue: 500_000,
    }),
    reportBucket({
      key: '2026-10-01',
      from: '2026-10-01',
      to: '2026-10-31',
      recognizedRevenue: 1_500_000,
    }),
  ],
  brands: [],
  totals: reportTotals({ recognizedRevenue: 2_000_000 }),
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

const funnelSteps = [
  { metric: 'product_view', count: 120, conversionRate: null },
  { metric: 'add_to_cart', count: 30, conversionRate: 0.25 },
  { metric: 'order_placed', count: 6, conversionRate: 0.2 },
];

// Khối phễu đọc khoảng của báo cáo doanh thu, nên ngày trong response trùng báo cáo.
const funnelReport = { from: '2026-05-01', to: '2026-10-15', steps: funnelSteps };

// Bộ đếm rỗng: API trả ba bước với 0 lượt và tỉ lệ `null`.
const zeroFunnelReport = {
  from: '2026-05-01',
  to: '2026-10-15',
  steps: [
    { metric: 'product_view', count: 0, conversionRate: null },
    { metric: 'add_to_cart', count: 0, conversionRate: null },
    { metric: 'order_placed', count: 0, conversionRate: null },
  ],
};

const FUNNEL_SECTION = 'section[aria-labelledby="revenue-funnel-title"]';
const REVENUE_SECTION = 'section[aria-labelledby="revenue-period-title"]';

function respond(url: string) {
  if (url.startsWith('/api/admin/analytics/funnel')) return funnelReport;
  if (!url.startsWith('/api/admin/reports/sales')) return [];
  return periodReport;
}

beforeEach(installReportClock);
afterEach(cleanupReportPage);

describe('trang báo cáo doanh thu theo kỳ', () => {
  it('thiếu reports:read thì hiện thông báo không đủ quyền và không gọi API báo cáo', async () => {
    const harness = await renderReportPage(
      <RevenuePeriodsReportPage />,
      '/admin/reports/revenue/periods',
      { ...reportsActor, permissions: ['products:read'] },
      () => [],
    );

    expect(textOf(harness.container)).toContain('Không có quyền truy cập');
    expect(harness.requestedUrls.some((url) => url.startsWith('/api/admin/reports/sales'))).toBe(
      false,
    );
  });

  it('chỉ có một biểu đồ, mặc định 6 tháng gần nhất chia theo tháng', async () => {
    const harness = await renderReportPage(
      <RevenuePeriodsReportPage />,
      '/admin/reports/revenue/periods',
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
    expect(textOf(harness.container)).toContain('500.000 đ');
    expect(textOf(harness.container)).toContain('1.500.000 đ');
    expect(
      harness.container.querySelectorAll(`${REVENUE_SECTION} .report-table tbody tr`),
    ).toHaveLength(2);
  });

  it('đổi khoảng thời gian cập nhật URL và độ chia mặc định', async () => {
    const harness = await renderReportPage(
      <RevenuePeriodsReportPage />,
      '/admin/reports/revenue/periods',
      reportsActor,
      respond,
    );
    await waitFor(() => textOf(harness.container).includes('Tháng 5/2026'), 'bảng theo kỳ');

    const rangeSelect = findField(harness.container, 'Khoảng thời gian');
    if (!rangeSelect) throw new Error('Thiếu ô chọn khoảng thời gian.');
    await selectOption(rangeSelect, '30d');

    await waitFor(
      () =>
        harness.requestedUrls.some(
          (url) => url.includes('range=30d') && url.includes('granularity=day'),
        ),
      'truy vấn khoảng 30 ngày',
    );
    expect(findField(harness.container, 'Chia theo')?.value).toBe('day');
    expect(harness.searchParams().get('range')).toBe('30d');
    expect(harness.searchParams().get('granularity')).toBe('day');
    // Nhãn kỳ đổi theo độ chia mới.
    await waitFor(() => textOf(harness.container).includes('01/05/2026'), 'nhãn kỳ chia theo ngày');
  });

  it('mở lại URL đã chọn khoảng thời gian và độ chia', async () => {
    const harness = await renderReportPage(
      <RevenuePeriodsReportPage />,
      '/admin/reports/revenue/periods?range=30d&granularity=week',
      reportsActor,
      respond,
    );

    await waitFor(
      () => textOf(harness.container).includes('01/05/2026 - 31/05/2026'),
      'bảng theo tuần từ URL',
    );
    expect(findField(harness.container, 'Khoảng thời gian')?.value).toBe('30d');
    expect(findField(harness.container, 'Chia theo')?.value).toBe('week');
    expect(
      harness.requestedUrls.some(
        (url) => url.includes('range=30d') && url.includes('granularity=week'),
      ),
    ).toBe(true);
  });

  it('khoảng không có số liệu hiện thông báo thay vì biểu đồ rỗng', async () => {
    const harness = await renderReportPage(
      <RevenuePeriodsReportPage />,
      '/admin/reports/revenue/periods?range=7d',
      reportsActor,
      (url) => (url.startsWith('/api/admin/analytics/funnel') ? funnelReport : emptyPeriod),
    );

    await waitFor(
      () => textOf(harness.container).includes('Chưa có dữ liệu trong khoảng này'),
      'thông báo khoảng trống',
    );
    expect(
      harness.requestedUrls.some(
        (url) => url.includes('range=7d') && url.includes('granularity=day'),
      ),
    ).toBe(true);
    expect(harness.container.querySelector(`${REVENUE_SECTION} .report-table`)).toBeNull();
  });

  it('khối phễu hiện số lượt và tỉ lệ chuyển của ba bước theo khoảng của báo cáo', async () => {
    const harness = await renderReportPage(
      <RevenuePeriodsReportPage />,
      '/admin/reports/revenue/periods',
      reportsActor,
      respond,
    );

    const funnelTable = await waitForFunnelTable(harness);
    const rows = Array.from(funnelTable.querySelectorAll('tbody tr'));
    expect(rows.map((row) => row.textContent)).toEqual([
      'Xem sản phẩm120Bước đầu',
      'Thêm giỏ3025%',
      'Đặt đơn620%',
    ]);
    expect(funnelTable.querySelector('caption')?.textContent).toBe(
      'Bảng phễu chuyển đổi từ 01/05/2026 đến 15/10/2026',
    );
    // Biểu đồ có tên cho trình đọc màn hình, nằm trên bảng số liệu.
    expect(
      harness.container.querySelector(`${FUNNEL_SECTION} [role="img"]`)?.getAttribute('aria-label'),
    ).toContain('Phễu chuyển đổi');
    // Yêu cầu phễu dùng đúng khoảng mà báo cáo doanh thu trả về.
    expect(harness.requestedUrls).toContain(
      '/api/admin/analytics/funnel?from=2026-05-01&to=2026-10-15',
    );
  });

  it('bước trước không có lượt thì khối phễu ghi chưa có dữ liệu', async () => {
    const harness = await renderReportPage(
      <RevenuePeriodsReportPage />,
      '/admin/reports/revenue/periods',
      reportsActor,
      (url) => (url.startsWith('/api/admin/analytics/funnel') ? zeroFunnelReport : periodReport),
    );

    const funnelTable = await waitForFunnelTable(harness);
    const rows = Array.from(funnelTable.querySelectorAll('tbody tr'));
    expect(rows.map((row) => row.textContent)).toEqual([
      'Xem sản phẩm0Bước đầu',
      'Thêm giỏ0Chưa có dữ liệu',
      'Đặt đơn0Chưa có dữ liệu',
    ]);
  });

  it('đổi khoảng thời gian thì khối phễu tải lại theo khoảng mới', async () => {
    function respondByRange(url: string) {
      const parsed = new URL(url, 'http://localhost');
      if (parsed.pathname === '/api/admin/analytics/funnel') {
        return {
          from: parsed.searchParams.get('from') ?? '',
          to: parsed.searchParams.get('to') ?? '',
          steps: funnelSteps,
        };
      }
      if (parsed.searchParams.get('range') === '30d') {
        return { ...periodReport, from: '2026-09-18', to: '2026-10-17' };
      }
      return periodReport;
    }

    const harness = await renderReportPage(
      <RevenuePeriodsReportPage />,
      '/admin/reports/revenue/periods',
      reportsActor,
      respondByRange,
    );
    await waitFor(
      () =>
        harness.requestedUrls.some((url) =>
          url.startsWith('/api/admin/analytics/funnel?from=2026-05-01&to=2026-10-15'),
        ),
      'phễu của khoảng sáu tháng',
    );

    const rangeSelect = findField(harness.container, 'Khoảng thời gian');
    if (!rangeSelect) throw new Error('Thiếu ô chọn khoảng thời gian.');
    await selectOption(rangeSelect, '30d');

    await waitFor(
      () =>
        harness.requestedUrls.some((url) =>
          url.startsWith('/api/admin/analytics/funnel?from=2026-09-18&to=2026-10-17'),
        ),
      'phễu của khoảng ba mươi ngày',
    );
  });

  it('API phễu lỗi thì khối phễu hiện thông báo lỗi, bảng doanh thu vẫn hiện', async () => {
    const harness = await renderReportPage(
      <RevenuePeriodsReportPage />,
      '/admin/reports/revenue/periods',
      reportsActor,
      (url) => {
        if (url.startsWith('/api/admin/analytics/funnel')) {
          return new Error('Máy chủ phễu đang bận.');
        }
        return periodReport;
      },
    );

    await waitFor(
      () => textOf(harness.container).includes('Máy chủ phễu đang bận.'),
      'thông báo lỗi của phễu',
    );
    const funnelSection = harness.container.querySelector(FUNNEL_SECTION);
    expect(funnelSection?.querySelector('[role="alert"]')?.textContent).toContain(
      'Máy chủ phễu đang bận.',
    );
    expect(funnelSection?.querySelector('.report-table')).toBeNull();
    // Lỗi của khối phễu không thay thế biểu đồ doanh thu.
    expect(
      harness.container.querySelectorAll(`${REVENUE_SECTION} .report-table tbody tr`),
    ).toHaveLength(2);
  });
});

// Chờ khối phễu có bảng số liệu rồi trả bảng đó. Phễu chỉ tải sau khi báo cáo doanh thu có khoảng.
async function waitForFunnelTable(harness: ReportPageHarness): Promise<HTMLTableElement> {
  let table: HTMLTableElement | null = null;
  await waitFor(() => {
    table = harness.container.querySelector<HTMLTableElement>(`${FUNNEL_SECTION} .report-table`);
    return table !== null;
  }, 'bảng phễu chuyển đổi');
  if (!table) throw new Error('Thiếu bảng phễu chuyển đổi.');
  return table;
}
