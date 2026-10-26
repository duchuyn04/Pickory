import { expect, test, type Page } from '@playwright/test';
import { collectConsoleIssues, loginAdmin } from './catalog-admin-flow';
import { stubWebFonts } from './commerce-order-fixtures';
import { e2eEnvironment } from './environment';
import { activateStaffAccount, inviteStaff } from './staff-fixtures';

// Chờ biểu đồ co lại theo khung nhìn rồi mới đo. ResizeObserver của Recharts cập nhật sau một nhịp.
async function expectNoOverflowAfterResize(page: Page, width: number) {
  await expect
    .poll(() => page.evaluate(() => document.documentElement.scrollWidth), {
      message: 'Trang rộng hơn khung nhìn',
      timeout: 3000,
    })
    .toBeLessThanOrEqual(width);
}

// Tháng hiện tại theo giờ Việt Nam, cùng quy tắc với giao diện. `offset` âm để lùi về quá khứ.
function vietnamMonth(offset: number): string {
  const parts = new Intl.DateTimeFormat('en-GB', {
    timeZone: 'Asia/Ho_Chi_Minh',
    year: 'numeric',
    month: '2-digit',
  }).formatToParts(new Date());
  const year = Number(parts.find((part) => part.type === 'year')?.value);
  const month = Number(parts.find((part) => part.type === 'month')?.value);
  const index = year * 12 + (month - 1) + offset;
  return `${Math.floor(index / 12)}-${String((index % 12) + 1).padStart(2, '0')}`;
}

function monthLabel(month: string): string {
  return `Tháng ${Number(month.slice(5, 7))}/${month.slice(0, 4)}`;
}

const currentMonth = vietnamMonth(0);
// Tháng cũ nhất trong danh sách chọn, dùng cho trường hợp tháng không có số liệu.
const emptyMonth = vietnamMonth(-12);

const brandRows = [
  { name: 'Apple', revenue: 6_000_000 },
  { name: 'Samsung', revenue: 5_000_000 },
  { name: 'Xiaomi', revenue: 4_000_000 },
  { name: 'Sony', revenue: 3_000_000 },
  { name: 'Dell', revenue: 2_000_000 },
  { name: 'Logitech', revenue: 1_000_000 },
  { name: 'Asus', revenue: 500_000 },
];

function brandReport(month: string) {
  return {
    from: `${month}-01`,
    to: `${month}-28`,
    buckets: [],
    brands: brandRows.map((row, index) => ({
      brandId: `brand-${index + 1}`,
      brandName: row.name,
      revenue: row.revenue,
    })),
  };
}

function emptyMonthReport(month: string) {
  return { from: `${month}-01`, to: `${month}-28`, buckets: [], brands: [] };
}

// Sáu tháng gần nhất, mỗi tháng một cột.
function periodReport() {
  const buckets = Array.from({ length: 6 }, (_, index) => {
    const month = vietnamMonth(index - 5);
    return {
      key: `${month}-01`,
      from: `${month}-01`,
      to: `${month}-28`,
      recognizedRevenue: (index + 1) * 1_500_000,
    };
  });
  return {
    from: buckets[0]!.from,
    to: buckets[5]!.to,
    buckets,
    brands: [],
  };
}

function emptyPeriodReport() {
  return { from: `${currentMonth}-20`, to: `${currentMonth}-27`, buckets: [], brands: [] };
}

// Phễu dùng đúng khoảng ngày mà route doanh thu trả về. Khoảng trống trả ba bước 0 lượt và tỉ lệ rỗng,
// giống API thật của ACT-11.
function funnelReport(from: string, to: string) {
  const isEmpty = from === `${currentMonth}-20`;
  return {
    from,
    to,
    steps: [
      { metric: 'product_view', count: isEmpty ? 0 : 120, conversionRate: null },
      { metric: 'add_to_cart', count: isEmpty ? 0 : 30, conversionRate: isEmpty ? null : 0.25 },
      { metric: 'order_placed', count: isEmpty ? 0 : 6, conversionRate: isEmpty ? null : 0.2 },
    ],
  };
}

// Thay hai route báo cáo bằng số liệu cố định. Route thật đã có test tích hợp riêng;
// test này kiểm giao diện và URL của từng trang trong trình duyệt thật.
async function stubReportApi(page: Page) {
  await page.route(
    (url) => url.pathname === '/api/admin/reports/sales',
    async (route) => {
      const url = new URL(route.request().url());
      const range = url.searchParams.get('range');
      const requestedMonth = url.searchParams.get('month') ?? '';
      let data;
      if (range === 'month') {
        data =
          requestedMonth === emptyMonth
            ? emptyMonthReport(requestedMonth)
            : brandReport(requestedMonth);
      } else if (range === '7d') {
        data = emptyPeriodReport();
      } else {
        data = periodReport();
      }
      await route.fulfill({ json: { data } });
    },
  );
  await page.route(
    (url) => url.pathname === '/api/admin/analytics/funnel',
    async (route) => {
      const url = new URL(route.request().url());
      const from = url.searchParams.get('from') ?? '';
      const to = url.searchParams.get('to') ?? '';
      await route.fulfill({ json: { data: funnelReport(from, to) } });
    },
  );
}

test('ADM-UI5: doanh thu theo thương hiệu là một trang riêng, không tràn ngang', async ({
  page,
}, testInfo) => {
  test.setTimeout(120000);
  const consoleIssues = collectConsoleIssues(page);
  await stubWebFonts(page);
  await stubReportApi(page);

  await loginAdmin(page);
  // Người có reports:read thấy hai mục báo cáo trong menu.
  await expect(
    page.locator('nav').getByRole('link', { name: 'Doanh thu thương hiệu', exact: true }),
  ).toBeVisible();
  await expect(
    page.locator('nav').getByRole('link', { name: 'Doanh thu theo kỳ', exact: true }),
  ).toBeVisible();

  await test.step('đường dẫn cũ chuyển về trang thương hiệu và giữ tháng', async () => {
    await page.goto(`/admin/reports/revenue?month=${currentMonth}&range=30d`);
    await expect(page).toHaveURL(
      `${e2eEnvironment.webOrigin}/admin/reports/revenue/brands?month=${currentMonth}`,
    );
    await expect(
      page.getByRole('heading', { level: 1, name: 'Báo cáo doanh thu theo thương hiệu' }),
    ).toBeVisible();
  });

  await expect(page.getByRole('status')).toHaveCount(0);
  const slices = page.locator('.recharts-pie-sector path');
  await expect(slices).toHaveCount(7);
  await expect(page.locator('.report-chart-label').first()).toHaveText(/%$/);
  await expect(page.locator('.report-legend-item')).toHaveCount(7);
  await expect(page.getByLabel('Tháng', { exact: true })).toHaveValue(currentMonth);

  const brandTable = page.locator('.report-table');
  await expect(brandTable).toContainText('6.000.000 đ');
  await expect(brandTable.getByRole('row', { name: /Khác/ })).toContainText('500.000 đ');
  // Trang chỉ có biểu đồ tròn và bảng thương hiệu, không có bảng sản phẩm.
  await expect(page.locator('.report-detail')).toHaveCount(0);

  await test.step('ảnh chụp 1280, 768 và 375 không tràn ngang', async () => {
    for (const width of [1280, 768, 375]) {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      await expectNoOverflowAfterResize(page, width);
      await page.screenshot({
        path: testInfo.outputPath(`brands-${width}.png`),
        fullPage: true,
      });
    }
  });

  await test.step('bộ chọn tháng dùng được bằng bàn phím', async () => {
    await page.setViewportSize({ width: 1280, height: 800 });
    const monthSelect = page.getByLabel('Tháng', { exact: true });
    await monthSelect.focus();
    await expect(monthSelect).toBeFocused();
  });

  await test.step('tháng không có số liệu hiện thông báo thay vì biểu đồ rỗng', async () => {
    await page.getByLabel('Tháng', { exact: true }).selectOption(emptyMonth);
    await expect(page.getByText('Chưa có dữ liệu trong tháng này')).toBeVisible();
    await expect(page.locator('.recharts-pie-sector')).toHaveCount(0);
    await expect(page.locator('.report-chart-legend')).toHaveCount(0);
    await expect(page.locator('.report-table')).toHaveCount(0);

    // Mở lại URL cho cùng tháng trống.
    await page.reload();
    await expect(page.getByLabel('Tháng', { exact: true })).toHaveValue(emptyMonth);
    await expect(page.getByText('Chưa có dữ liệu trong tháng này')).toBeVisible();
  });

  expect(consoleIssues).toEqual([]);
});

test('ADM-UI5: doanh thu theo kỳ là một trang riêng, không tràn ngang', async ({
  page,
}, testInfo) => {
  test.setTimeout(120000);
  const consoleIssues = collectConsoleIssues(page);
  await stubWebFonts(page);
  await stubReportApi(page);

  await loginAdmin(page);
  await page.goto('/admin/reports/revenue/periods');
  await expect(
    page.getByRole('heading', { level: 1, name: 'Báo cáo doanh thu theo kỳ' }),
  ).toBeVisible();
  await expect(page.getByRole('status')).toHaveCount(0);

  const revenuePanel = page.locator('.report-panel').first();
  const funnelPanel = page.locator('.report-panel').nth(1);
  await expect(revenuePanel.locator('.recharts-bar-rectangle path')).toHaveCount(6);
  await expect(page.getByLabel('Khoảng thời gian')).toHaveValue('6m');
  await expect(page.getByLabel('Chia theo')).toHaveValue('month');
  const revenueTable = revenuePanel.locator('.report-table');
  await expect(revenueTable).toContainText(monthLabel(vietnamMonth(-5)));
  await expect(revenueTable).toContainText('9.000.000 đ');

  await test.step('khối phễu hiện ba bước với lượt và tỉ lệ chuyển', async () => {
    await expect(funnelPanel.getByRole('heading', { name: 'Phễu chuyển đổi' })).toBeVisible();
    await expect(funnelPanel.locator('.recharts-bar-rectangle path')).toHaveCount(3);
    const funnelTable = funnelPanel.locator('.report-table');
    await expect(funnelTable).toContainText('Xem sản phẩm');
    await expect(funnelTable).toContainText('Thêm giỏ');
    await expect(funnelTable).toContainText('Đặt đơn');
    // Tỉ lệ chuyển so với bước trước nằm trong bảng, đọc được khi người xem không nhìn thanh.
    await expect(funnelTable).toContainText('25%');
    await expect(funnelTable).toContainText('20%');
  });

  await test.step('ảnh chụp 1280, 768 và 375 không tràn ngang', async () => {
    for (const width of [1280, 768, 375]) {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      await expectNoOverflowAfterResize(page, width);
      await page.screenshot({
        path: testInfo.outputPath(`periods-${width}.png`),
        fullPage: true,
      });
    }
  });

  await test.step('đổi khoảng thời gian cập nhật URL và độ chia', async () => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.getByLabel('Khoảng thời gian').selectOption('30d');
    await expect(page).toHaveURL(/range=30d/);
    await expect(page).toHaveURL(/granularity=day/);
    await expect(page.getByLabel('Chia theo')).toHaveValue('day');
    await expect(revenueTable).toContainText('01/05/2026');

    // Mở lại URL cho cùng khoảng thời gian.
    await page.reload();
    await expect(page.getByLabel('Khoảng thời gian')).toHaveValue('30d');
    await expect(page.getByLabel('Chia theo')).toHaveValue('day');
    await expect(page.locator('.report-panel').first().locator('.report-table')).toContainText(
      '01/05/2026',
    );
  });

  await test.step('khoảng không có số liệu hiện thông báo thay vì biểu đồ rỗng', async () => {
    await page.getByLabel('Khoảng thời gian').selectOption('7d');
    await expect(page.getByText('Chưa có dữ liệu trong khoảng này')).toBeVisible();
    await expect(revenuePanel.locator('.recharts-bar-rectangle')).toHaveCount(0);
    await expect(revenuePanel.locator('.report-table')).toHaveCount(0);
    // Phễu vẫn liệt kê ba bước, nhưng bước trước 0 lượt nên không có tỉ lệ chuyển.
    await expect(funnelPanel.locator('.report-table')).toContainText('Chưa có dữ liệu');
  });

  expect(consoleIssues).toEqual([]);
});

test('ADM-UI5: nhân viên thiếu reports:read không thấy menu và thấy trang không đủ quyền', async ({
  browser,
}, testInfo) => {
  test.setTimeout(180000);
  const adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
  const adminPage = await adminContext.newPage();
  const adminIssues = collectConsoleIssues(adminPage);
  await stubWebFonts(adminPage);

  const staffEmail = `e2e-report-support-${Date.now().toString(36)}@pickory.local`;
  const staffContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
  const staffPage = await staffContext.newPage();
  const staffIssues = collectConsoleIssues(staffPage);
  await stubWebFonts(staffPage);

  try {
    await loginAdmin(adminPage);
    await inviteStaff(adminPage, {
      email: staffEmail,
      fullName: 'Người Xem Không Quyền',
      roleCode: 'support',
    });
    await activateStaffAccount(staffPage, staffEmail);

    await test.step('menu không có mục báo cáo', async () => {
      await expect(staffPage).toHaveURL(`${e2eEnvironment.webOrigin}/admin`);
      for (const label of ['Doanh thu thương hiệu', 'Doanh thu theo kỳ']) {
        await expect(
          staffPage.locator('nav').getByRole('link', { name: label, exact: true }),
        ).toHaveCount(0);
      }
      await expect(staffPage.locator('nav').getByText('Báo cáo', { exact: true })).toHaveCount(0);
    });

    await test.step('mở thẳng URL thấy trang không đủ quyền', async () => {
      for (const path of ['/admin/reports/revenue/brands', '/admin/reports/revenue/periods']) {
        await staffPage.goto(path);
        await expect(
          staffPage.getByRole('heading', { name: '403 - Không có quyền truy cập' }),
        ).toBeVisible({ timeout: 15000 });
      }
      await staffPage.screenshot({
        path: testInfo.outputPath('reports-forbidden-support.png'),
        fullPage: true,
      });
    });

    expect(adminIssues).toEqual([]);
    expect(staffIssues).toEqual([]);
  } finally {
    await staffContext.close();
    await adminContext.close();
  }
});
