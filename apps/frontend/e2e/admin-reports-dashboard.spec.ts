import { expect, test, type Page, type TestInfo } from '@playwright/test';
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

// Tháng hiện tại theo giờ Việt Nam, cùng quy tắc với giao diện.
function vietnamMonth(): string {
  const parts = new Intl.DateTimeFormat('en-GB', {
    timeZone: 'Asia/Ho_Chi_Minh',
    year: 'numeric',
    month: '2-digit',
  }).formatToParts(new Date());
  const year = parts.find((part) => part.type === 'year')?.value ?? '';
  const month = parts.find((part) => part.type === 'month')?.value ?? '';
  return `${year}-${month}`;
}

const currentMonth = vietnamMonth();

// Số liệu tháng đủ cho bốn thẻ, cùng bộ số với trang chi tiết.
const topProducts = [
  { productId: 'p1', name: 'Máy A', brandName: 'Hãng Một', revenue: 7_000_000, quantity: 70 },
  { productId: 'p2', name: 'Máy B', brandName: 'Hãng Hai', revenue: 6_000_000, quantity: 60 },
  { productId: 'p3', name: 'Máy C', brandName: 'Hãng Một', revenue: 5_000_000, quantity: 50 },
  { productId: 'p4', name: 'Máy D', brandName: 'Hãng Hai', revenue: 4_000_000, quantity: 40 },
  { productId: 'p5', name: 'Máy E', brandName: 'Hãng Một', revenue: 3_000_000, quantity: 30 },
  { productId: 'p6', name: 'Máy F', brandName: 'Hãng Hai', revenue: 2_000_000, quantity: 20 },
  { productId: 'p7', name: 'Máy G', brandName: 'Hãng Một', revenue: 1_000_000, quantity: 10 },
];

function monthReport(month: string) {
  return {
    from: `${month}-01`,
    to: `${month}-28`,
    buckets: [],
    brands: [
      { brandId: 'b1', brandName: 'Hãng Một', revenue: 1_234_567 },
      { brandId: 'b2', brandName: 'Hãng Hai', revenue: 234_567 },
    ],
    totals: {
      recognizedRevenue: 1_469_134,
      paidOrders: 6,
      soldQuantity: 280,
      paidByMethod: { cod: 5, sepay: 1 },
      cancellations: { byCustomer: 3, byShop: 1, paymentExpired: 2 },
    },
    topProducts,
  };
}

// Thay route báo cáo bằng số liệu cố định. Mỗi thẻ gọi route này một lần.
async function stubReportApi(page: Page) {
  await page.route(
    (url) => url.pathname === '/api/admin/reports/sales',
    async (route) => {
      const url = new URL(route.request().url());
      const requestedMonth = url.searchParams.get('month') ?? currentMonth;
      await route.fulfill({ json: { data: monthReport(requestedMonth) } });
    },
  );
}

async function captureWidths(page: Page, name: string, widths: number[], testInfo: TestInfo) {
  for (const width of widths) {
    await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
    await expectNoOverflowAfterResize(page, width);
    await page.screenshot({ path: testInfo.outputPath(`${name}-${width}.png`), fullPage: true });
  }
}

test('ADM-UI11: dashboard có bốn thẻ, mở đúng báo cáo chi tiết', async ({ page }, testInfo) => {
  test.setTimeout(120000);
  const consoleIssues = collectConsoleIssues(page);
  await stubWebFonts(page);
  await stubReportApi(page);

  await loginAdmin(page);
  // Người có reports:read thấy mục tổng quan báo cáo trong menu.
  await expect(
    page.locator('nav').getByRole('link', { name: 'Báo cáo tổng quan', exact: true }),
  ).toBeVisible();

  await page.goto('/admin/reports');
  await expect(page.getByRole('heading', { level: 1, name: 'Báo cáo' })).toBeVisible();
  await expect(page.getByRole('status')).toHaveCount(0);

  const cards = page.locator('.report-card');
  await expect(cards).toHaveCount(4);
  await expect(page.locator('.report-chart-legend')).toHaveCount(4);

  const revenueCard = page.locator('.report-card').filter({ hasText: 'Doanh thu' }).first();
  await expect(revenueCard).toContainText('Tổng doanh thu tháng này');
  await expect(revenueCard).toContainText('1.469.134 đ');

  const ordersCard = page.locator('.report-card').filter({ hasText: 'Đơn đã thanh toán' });
  await expect(ordersCard).toContainText('Tổng đơn đã thanh toán tháng này');
  await expect(ordersCard.locator('.report-legend-item')).toHaveCount(2);

  const cancellationsCard = page.locator('.report-card').filter({ hasText: 'Đơn hủy' });
  await expect(cancellationsCard.locator('.report-legend-item')).toHaveCount(3);

  const productsCard = page.locator('.report-card').filter({ hasText: 'Sản phẩm bán ra' });
  await expect(productsCard.locator('.report-legend-item')).toHaveCount(6);

  await test.step('ảnh chụp 1280, 768 và 375 không tràn ngang', async () => {
    await captureWidths(page, 'dashboard', [1280, 768, 375], testInfo);
  });

  await test.step('ở 375 px bốn thẻ xếp một cột', async () => {
    const stacked = await page.evaluate(() => {
      const boxes = Array.from(document.querySelectorAll('.report-card')).map((card) =>
        card.getBoundingClientRect(),
      );
      const distinctLeftEdges = new Set(boxes.map((box) => Math.round(box.x)));
      // Mọi thẻ cùng mép trái nghĩa là xếp thành một cột.
      return boxes.length === 4 && distinctLeftEdges.size === 1;
    });
    expect(stacked).toBe(true);
  });

  await page.setViewportSize({ width: 1280, height: 800 });

  await test.step('nhấn thẻ mở trang chi tiết của báo cáo', async () => {
    await page.getByRole('link', { name: 'Đơn hủy', exact: true }).click();
    await expect(page).toHaveURL(
      `${e2eEnvironment.webOrigin}/admin/reports/cancellations/reasons?month=${currentMonth}`,
    );
    await expect(
      page.getByRole('heading', { level: 1, name: 'Báo cáo đơn hủy theo lý do' }),
    ).toBeVisible();
  });

  await test.step('nhấn một lát mở trang chi tiết đã chọn sẵn lát đó', async () => {
    await page.goto('/admin/reports');
    await expect(cards).toHaveCount(4);
    await page.getByRole('button', { name: /^SePay/ }).click();
    await expect(page).toHaveURL(
      `${e2eEnvironment.webOrigin}/admin/reports/orders/payments?month=${currentMonth}&method=sepay`,
    );
    await expect(page.locator('.report-drilldown')).toBeVisible();
    await expect(page.locator('.report-drilldown')).toContainText('Chi tiết SePay theo ngày');
  });

  expect(consoleIssues).toEqual([]);
});

test('ADM-UI11: thẻ báo cáo lỗi API hiện thông báo lỗi, trang không trắng', async ({ page }) => {
  test.setTimeout(120000);
  const consoleIssues = collectConsoleIssues(page);
  await stubWebFonts(page);

  // Mọi lần gọi báo cáo đều lỗi. Ứng dụng thử lại ba lần cho lỗi 5xx trước khi hiện thông báo.
  await page.route(
    (url) => url.pathname === '/api/admin/reports/sales',
    async (route) => {
      await route.fulfill({
        status: 500,
        json: { error: { code: 'UNEXPECTED_ERROR', message: 'Không tải được báo cáo.' } },
      });
    },
  );

  await loginAdmin(page);
  await page.goto('/admin/reports');

  await expect(page.getByRole('heading', { level: 1, name: 'Báo cáo' })).toBeVisible();
  await expect(page.locator('.report-card')).toHaveCount(4);
  // Mỗi thẻ tự hiện lỗi của mình, nên một API lỗi không làm trắng cả trang.
  await expect(page.getByText('Không tải được báo cáo.')).toHaveCount(4, { timeout: 30000 });
  await expect(page.locator('.report-chart-legend')).toHaveCount(0);

  // Route giả cố ý trả 500 nên Chromium tự ghi lỗi tải tài nguyên. Bỏ đúng các dòng đó, lỗi khác vẫn làm test đỏ.
  const unexpectedIssues = consoleIssues.filter(
    (issue) =>
      !issue.includes('Failed to load resource: the server responded with a status of 500'),
  );
  expect(unexpectedIssues).toEqual([]);
});

test('ADM-UI11: nhân viên thiếu reports:read không thấy menu và thấy trang không đủ quyền', async ({
  browser,
}, testInfo) => {
  test.setTimeout(180000);
  const adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
  const adminPage = await adminContext.newPage();
  const adminIssues = collectConsoleIssues(adminPage);
  await stubWebFonts(adminPage);

  const staffEmail = `e2e-report-dashboard-${Date.now().toString(36)}@pickory.local`;
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
      await expect(
        staffPage.locator('nav').getByRole('link', { name: 'Báo cáo tổng quan', exact: true }),
      ).toHaveCount(0);
      await expect(staffPage.locator('nav').getByText('Báo cáo', { exact: true })).toHaveCount(0);
    });

    await test.step('mở thẳng URL thấy trang không đủ quyền', async () => {
      await staffPage.goto('/admin/reports');
      await expect(
        staffPage.getByRole('heading', { name: '403 - Không có quyền truy cập' }),
      ).toBeVisible({ timeout: 15000 });
      await staffPage.screenshot({
        path: testInfo.outputPath('reports-dashboard-forbidden-support.png'),
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
