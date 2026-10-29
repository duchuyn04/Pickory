import { expect, test, type Page, type TestInfo } from '@playwright/test';
import { collectConsoleIssues, loginAdmin } from './catalog-admin-flow';
import { stubWebFonts } from './commerce-order-fixtures';
import { e2eEnvironment } from './environment';

// Chờ biểu đồ co lại theo khung nhìn rồi mới đo. ResizeObserver của Recharts cập nhật sau một nhịp.
async function expectNoOverflowAfterResize(page: Page, width: number) {
  await expect
    .poll(() => page.evaluate(() => document.documentElement.scrollWidth), {
      message: 'Trang rộng hơn khung nhìn',
      timeout: 3000,
    })
    .toBeLessThanOrEqual(width);
}

// Tháng hiện tại theo giờ Việt Nam. `offset` âm để lùi về quá khứ.
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

const currentMonth = vietnamMonth(0);
// Tháng cũ nhất trong danh sách chọn, dùng cho trường hợp tháng không có số liệu.
const emptyMonth = vietnamMonth(-12);

// Bảy sản phẩm bán trong tháng. Năm sản phẩm đầu hiện riêng, hai sản phẩm cuối vào nhóm Khác.
const topProducts = [
  { productId: 'p1', name: 'Máy A', brandName: 'Hãng Một', revenue: 7_000_000, quantity: 70 },
  { productId: 'p2', name: 'Máy B', brandName: 'Hãng Hai', revenue: 6_000_000, quantity: 60 },
  { productId: 'p3', name: 'Máy C', brandName: 'Hãng Một', revenue: 5_000_000, quantity: 50 },
  { productId: 'p4', name: 'Máy D', brandName: 'Hãng Hai', revenue: 4_000_000, quantity: 40 },
  { productId: 'p5', name: 'Máy E', brandName: 'Hãng Một', revenue: 3_000_000, quantity: 30 },
  { productId: 'p6', name: 'Máy F', brandName: 'Hãng Hai', revenue: 2_000_000, quantity: 20 },
  { productId: 'p7', name: 'Máy G', brandName: 'Hãng Một', revenue: 1_000_000, quantity: 10 },
];

// Số liệu tháng: hai ngày có đơn, đủ ba lý do hủy ở ngày thứ hai.
function monthReport(month: string) {
  return {
    from: `${month}-01`,
    to: `${month}-28`,
    buckets: [
      {
        key: `${month}-01`,
        from: `${month}-01`,
        to: `${month}-01`,
        recognizedRevenue: 1_000_000,
        paidOrders: 2,
        paidByMethod: { cod: 2, sepay: 0 },
        cancellations: { byCustomer: 1, byShop: 0, paymentExpired: 0 },
        soldQuantity: 10,
      },
      {
        key: `${month}-02`,
        from: `${month}-02`,
        to: `${month}-02`,
        recognizedRevenue: 3_000_000,
        paidOrders: 4,
        paidByMethod: { cod: 3, sepay: 1 },
        cancellations: { byCustomer: 2, byShop: 1, paymentExpired: 2 },
        soldQuantity: 20,
      },
    ],
    brands: [],
    totals: {
      recognizedRevenue: 4_000_000,
      paidOrders: 6,
      soldQuantity: 30,
      paidByMethod: { cod: 5, sepay: 1 },
      cancellations: { byCustomer: 3, byShop: 1, paymentExpired: 2 },
    },
    topProducts,
  };
}

// Sáu tháng gần nhất, mỗi tháng một cột với đủ ba bộ đếm.
function periodReport() {
  const buckets = Array.from({ length: 6 }, (_, index) => {
    const month = vietnamMonth(index - 5);
    return {
      key: `${month}-01`,
      from: `${month}-01`,
      to: `${month}-28`,
      recognizedRevenue: (index + 1) * 1_000_000,
      paidOrders: index + 4,
      paidByMethod: { cod: index + 3, sepay: 1 },
      cancellations: { byCustomer: index + 1, byShop: 1, paymentExpired: 0 },
      soldQuantity: (index + 1) * 10,
    };
  });
  return {
    from: buckets[0]!.from,
    to: buckets[5]!.to,
    buckets,
    brands: [],
    totals: {
      recognizedRevenue: 21_000_000,
      paidOrders: 0,
      soldQuantity: 0,
      paidByMethod: { cod: 0, sepay: 0 },
      cancellations: { byCustomer: 0, byShop: 0, paymentExpired: 0 },
    },
    topProducts: [],
  };
}

// Thay route báo cáo bằng số liệu cố định. Route thật đã có test tích hợp riêng;
// test này kiểm giao diện và URL của từng trang trong trình duyệt thật.
async function stubReportApi(page: Page) {
  await page.route(
    (url) => url.pathname === '/api/admin/reports/sales',
    async (route) => {
      const url = new URL(route.request().url());
      const requestedMonth = url.searchParams.get('month') ?? currentMonth;
      const data =
        url.searchParams.get('range') === 'month'
          ? requestedMonth === emptyMonth
            ? {
                from: `${requestedMonth}-01`,
                to: `${requestedMonth}-28`,
                buckets: [],
                brands: [],
                totals: {
                  recognizedRevenue: 0,
                  paidOrders: 0,
                  soldQuantity: 0,
                  paidByMethod: { cod: 0, sepay: 0 },
                  cancellations: { byCustomer: 0, byShop: 0, paymentExpired: 0 },
                },
                topProducts: [],
              }
            : monthReport(requestedMonth)
          : periodReport();
      await route.fulfill({ json: { data } });
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

// Nhãn ngày của bảng chi tiết, cùng mẫu `dd/MM/yyyy` với trang.
function dayLabel(month: string, day: string): string {
  return `${day}/${month.slice(5, 7)}/${month.slice(0, 4)}`;
}

test('ADM-UI10: đơn đã thanh toán chia trang tròn và trang cột', async ({ page }, testInfo) => {
  test.setTimeout(120000);
  const consoleIssues = collectConsoleIssues(page);
  await stubWebFonts(page);
  await stubReportApi(page);

  await loginAdmin(page);
  await page.goto('/admin/reports/orders');
  await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/reports/orders/payments`);
  await expect(
    page.getByRole('heading', { level: 1, name: 'Báo cáo đơn đã thanh toán theo phương thức' }),
  ).toBeVisible();
  await expect(page.getByRole('status')).toHaveCount(0);

  await expect(page.locator('.recharts-pie-sector path')).toHaveCount(2);
  await expect(page.locator('.report-legend-item')).toHaveCount(2);
  const paymentTable = page.locator('.report-table');
  await expect(paymentTable).toContainText('COD');
  await expect(paymentTable).toContainText('83%');
  await expect(paymentTable).toContainText('SePay');
  await expect(page.getByLabel('Tháng', { exact: true })).toHaveValue(currentMonth);

  await test.step('nhấn lát COD thì bảng chi tiết hiện số đơn theo ngày', async () => {
    // Gửi sự kiện trực tiếp lên lát để không phụ thuộc điểm nhấn có trúng vành khuyên hay không.
    await page.locator('.recharts-pie-sector path').first().dispatchEvent('click');
    const drilldown = page.locator('.report-drilldown');
    await expect(drilldown).toBeVisible();
    await expect(drilldown.getByRole('heading', { level: 3 })).toContainText(
      'Chi tiết COD theo ngày',
    );
    await expect(drilldown.locator('tbody tr')).toHaveCount(2);
    await expect(drilldown).toContainText(dayLabel(currentMonth, '02'));
    await expect(page).toHaveURL(/method=cod/);
  });

  await test.step('ảnh chụp 1280, 768 và 375 không tràn ngang', async () => {
    await captureWidths(page, 'orders-payments', [1280, 768, 375], testInfo);
  });

  await test.step('tháng không có số liệu hiện thông báo thay vì biểu đồ rỗng', async () => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.getByLabel('Tháng', { exact: true }).selectOption(emptyMonth);
    await expect(page.getByText('Chưa có dữ liệu trong tháng này')).toBeVisible();
    await expect(page.locator('.recharts-pie-sector')).toHaveCount(0);
    await expect(page.locator('.report-table')).toHaveCount(0);
  });

  await page.goto('/admin/reports/orders/periods');
  await expect(
    page.getByRole('heading', { level: 1, name: 'Báo cáo đơn đã thanh toán theo kỳ' }),
  ).toBeVisible();
  await expect(page.locator('.recharts-bar-rectangle path')).toHaveCount(6);
  await expect(page.getByLabel('Khoảng thời gian')).toHaveValue('6m');
  const ordersPeriodTable = page.locator('.report-table');
  await expect(ordersPeriodTable.locator('tbody tr')).toHaveCount(6);
  await expect(ordersPeriodTable).toContainText('9');

  await test.step('ảnh chụp trang cột ở 1280, 768 và 375', async () => {
    await captureWidths(page, 'orders-periods', [1280, 768, 375], testInfo);
  });

  expect(consoleIssues).toEqual([]);
});

test('ADM-UI10: đơn hủy chia trang tròn và trang cột', async ({ page }, testInfo) => {
  test.setTimeout(120000);
  const consoleIssues = collectConsoleIssues(page);
  await stubWebFonts(page);
  await stubReportApi(page);

  await loginAdmin(page);
  await page.goto('/admin/reports/cancellations');
  await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/reports/cancellations/reasons`);
  await expect(
    page.getByRole('heading', { level: 1, name: 'Báo cáo đơn hủy theo lý do' }),
  ).toBeVisible();
  await expect(page.getByRole('status')).toHaveCount(0);

  await expect(page.locator('.recharts-pie-sector path')).toHaveCount(3);
  const reasonTable = page.locator('.report-table');
  await expect(reasonTable).toContainText('Khách hủy');
  await expect(reasonTable).toContainText('50%');
  await expect(reasonTable).toContainText('Shop hủy');
  await expect(reasonTable).toContainText('Quá hạn thanh toán');

  await test.step('nhấn lát quá hạn thanh toán thì bảng chi tiết hiện số đơn theo ngày', async () => {
    await page.locator('.recharts-pie-sector path').nth(2).dispatchEvent('click');
    const drilldown = page.locator('.report-drilldown');
    await expect(drilldown).toBeVisible();
    await expect(drilldown.getByRole('heading', { level: 3 })).toContainText(
      'Chi tiết Quá hạn thanh toán theo ngày',
    );
    await expect(drilldown.locator('tbody tr')).toHaveCount(2);
    await expect(page).toHaveURL(/reason=payment_expired/);
  });

  await test.step('ảnh chụp 1280, 768 và 375 không tràn ngang', async () => {
    await captureWidths(page, 'cancellations-reasons', [1280, 768, 375], testInfo);
  });

  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto('/admin/reports/cancellations/periods');
  await expect(
    page.getByRole('heading', { level: 1, name: 'Báo cáo đơn hủy theo kỳ' }),
  ).toBeVisible();
  await expect(page.locator('.recharts-bar-rectangle path')).toHaveCount(6);
  const cancellationsPeriodTable = page.locator('.report-table');
  await expect(cancellationsPeriodTable.locator('tbody tr')).toHaveCount(6);
  // Kỳ đầu có một đơn khách hủy và một đơn shop hủy.
  await expect(cancellationsPeriodTable.locator('tbody tr').first()).toContainText('2');

  await test.step('ảnh chụp trang cột ở 1280, 768 và 375', async () => {
    await captureWidths(page, 'cancellations-periods', [1280, 768, 375], testInfo);
  });

  expect(consoleIssues).toEqual([]);
});

test('ADM-UI10: sản phẩm bán ra chia trang tròn và trang cột', async ({ page }, testInfo) => {
  test.setTimeout(120000);
  const consoleIssues = collectConsoleIssues(page);
  await stubWebFonts(page);
  await stubReportApi(page);

  await loginAdmin(page);
  await page.goto('/admin/reports/products');
  await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/reports/products/top`);
  await expect(
    page.getByRole('heading', { level: 1, name: 'Báo cáo sản phẩm bán ra theo sản phẩm' }),
  ).toBeVisible();
  await expect(page.getByRole('status')).toHaveCount(0);

  await expect(page.locator('.recharts-pie-sector path')).toHaveCount(6);
  const productTable = page.locator('.report-table');
  await expect(productTable).toContainText('Máy A');
  await expect(productTable).toContainText('Khác');

  await test.step('nhấn lát sản phẩm thì bảng chi tiết hiện thương hiệu, doanh thu và số lượng', async () => {
    await page.locator('.recharts-pie-sector path').first().dispatchEvent('click');
    const drilldown = page.locator('.report-drilldown');
    await expect(drilldown).toBeVisible();
    await expect(drilldown.getByRole('heading', { level: 3 })).toContainText('Chi tiết Máy A');
    await expect(drilldown).toContainText('Hãng Một');
    await expect(drilldown).toContainText('7.000.000 đ');
    await expect(page).toHaveURL(/product=p1/);
  });

  await test.step('nhấn lát Khác thì bảng liệt kê sản phẩm trong nhóm', async () => {
    await page.getByRole('button', { name: /^Khác/ }).click();
    const drilldown = page.locator('.report-drilldown');
    await expect(drilldown.getByRole('heading', { level: 3 })).toContainText(
      'Các sản phẩm trong nhóm Khác',
    );
    await expect(drilldown.locator('tbody tr')).toHaveCount(2);
    await expect(drilldown).toContainText('Máy F');
    await expect(drilldown).toContainText('Máy G');
  });

  await test.step('ảnh chụp 1280, 768 và 375 không tràn ngang', async () => {
    await captureWidths(page, 'products-top', [1280, 768, 375], testInfo);
  });

  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto('/admin/reports/products/periods');
  await expect(
    page.getByRole('heading', { level: 1, name: 'Báo cáo sản phẩm bán ra theo kỳ' }),
  ).toBeVisible();
  await expect(page.locator('.recharts-bar-rectangle path')).toHaveCount(6);
  const productsPeriodTable = page.locator('.report-table');
  await expect(productsPeriodTable.locator('tbody tr')).toHaveCount(6);
  await expect(productsPeriodTable).toContainText('60');

  await test.step('ảnh chụp trang cột ở 1280, 768 và 375', async () => {
    await captureWidths(page, 'products-periods', [1280, 768, 375], testInfo);
  });

  await expect(page.getByRole('status')).toHaveCount(0);
  expect(consoleIssues).toEqual([]);
});
