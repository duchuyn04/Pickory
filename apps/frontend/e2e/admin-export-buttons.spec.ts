import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, test, type Page } from '@playwright/test';
import { expectNoHorizontalOverflow } from './admin-order-fixtures';
import { collectConsoleIssues, loginAdmin } from './catalog-admin-flow';

const EXPORT_LABEL = 'Xuất Excel';

// Ảnh bằng chứng nằm ở thư mục gốc của repo, ngoài `apps/frontend`.
const EVIDENCE_DIRECTORY = fileURLToPath(
  new URL('../../../evidence/exports-printing/EXP-UI3', import.meta.url),
);

interface PageUnderTest {
  // Tên ngắn không dấu, dùng trong tên file ảnh.
  slug: string;
  // Số thứ tự của ảnh 1280 px. Ảnh 375 px lấy số kế tiếp.
  evidenceNumber: number;
  url: string;
  heading: string;
  exportPathname: string;
}

const ORDERS_PAGE: PageUnderTest = {
  slug: 'don-hang',
  evidenceNumber: 1,
  url: '/admin/orders',
  heading: 'Quản lý đơn hàng',
  exportPathname: '/api/admin/exports/orders',
};
const PRODUCTS_PAGE: PageUnderTest = {
  slug: 'san-pham',
  evidenceNumber: 3,
  url: '/admin/products',
  heading: 'Sản phẩm',
  exportPathname: '/api/admin/exports/products',
};
const CUSTOMERS_PAGE: PageUnderTest = {
  slug: 'khach-hang',
  evidenceNumber: 5,
  url: '/admin/customers',
  heading: 'Quản lý Khách hàng',
  exportPathname: '/api/admin/exports/customers',
};
const PAYMENTS_PAGE: PageUnderTest = {
  slug: 'giao-dich',
  evidenceNumber: 7,
  url: '/admin/payments',
  heading: 'Đối soát thanh toán',
  exportPathname: '/api/admin/exports/payment-transactions',
};

function exportButton(page: Page) {
  return page.getByRole('button', { name: EXPORT_LABEL, exact: true });
}

// Mở trang và chờ tiêu đề cùng nút xuất. Nút chỉ hiện sau khi actor nạp xong.
async function openPage(page: Page, target: PageUnderTest, query = '') {
  await page.goto(`${target.url}${query}`);
  await expect(page.getByRole('heading', { level: 1, name: target.heading })).toBeVisible();
  await expect(exportButton(page)).toBeVisible();
}

// Bấm nút và trả URL của request tải file. Nội dung file không cần kiểm ở đây.
async function clickExportAndReadUrl(page: Page, target: PageUnderTest): Promise<URL> {
  const requestPromise = page.waitForRequest(
    (request) => new URL(request.url()).pathname === target.exportPathname,
  );
  await exportButton(page).click();
  return new URL((await requestPromise).url());
}

// Chờ nút xuất quay về trạng thái rảnh. Lần bấm sau mới đo được request mới.
async function waitForExportIdle(page: Page) {
  await expect(exportButton(page)).toBeEnabled();
}

function queryOf(url: URL): Record<string, string> {
  return Object.fromEntries(url.searchParams.entries());
}

// Chụp ảnh 1280 px và 375 px của trang, rồi kiểm nút nằm trong đầu trang và trang không tràn ngang.
async function captureBothWidths(page: Page, target: PageUnderTest, query = '') {
  const widths = [
    { width: 1280, number: target.evidenceNumber },
    { width: 375, number: target.evidenceNumber + 1 },
  ];
  for (const { width, number } of widths) {
    await page.setViewportSize({ width, height: 800 });
    await openPage(page, target, query);
    await expect(
      page.locator('.page-head').getByRole('button', { name: EXPORT_LABEL }),
    ).toBeVisible();
    await expectNoHorizontalOverflow(page);
    const fileName = `${String(number).padStart(2, '0')}-${target.slug}-${width}.png`;
    await page.screenshot({ path: path.join(EVIDENCE_DIRECTORY, fileName), fullPage: true });
  }
}

test('EXP-UI3: trang đơn hàng gọi route xuất với bộ lọc của danh sách', async ({ page }) => {
  test.setTimeout(120000);
  const consoleIssues = collectConsoleIssues(page);
  await loginAdmin(page);

  await test.step('Nút gửi status của tab và từ khóa trên URL', async () => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await openPage(page, ORDERS_PAGE, '?status=delivered&search=PK');
    const url = await clickExportAndReadUrl(page, ORDERS_PAGE);
    expect(queryOf(url)).toEqual({ status: 'delivered', search: 'PK' });
    await waitForExportIdle(page);
  });

  await test.step('Đổi bộ lọc rồi bấm lại thì request mang bộ lọc mới', async () => {
    await waitForExportIdle(page);
    await page.getByLabel('Phương thức', { exact: true }).selectOption('cod');
    const url = await clickExportAndReadUrl(page, ORDERS_PAGE);
    expect(queryOf(url)).toEqual({ status: 'delivered', search: 'PK', paymentMethod: 'cod' });
    await waitForExportIdle(page);
  });

  await test.step('Ảnh 1280 px và 375 px, nút trong đầu trang, không tràn ngang', async () => {
    await captureBothWidths(page, ORDERS_PAGE);
  });

  expect(consoleIssues).toEqual([]);
});

test('EXP-UI3: trang sản phẩm gửi lowStock=true và không gửi page', async ({ page }) => {
  test.setTimeout(120000);
  const consoleIssues = collectConsoleIssues(page);
  await loginAdmin(page);

  await test.step('Nút gửi lowStock=true, không gửi page của danh sách', async () => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await openPage(page, PRODUCTS_PAGE, '?lowStock=true&page=2');
    const url = await clickExportAndReadUrl(page, PRODUCTS_PAGE);
    expect(queryOf(url)).toEqual({ lowStock: 'true' });
    await waitForExportIdle(page);
  });

  await test.step('Ảnh 1280 px và 375 px, nút trong đầu trang, không tràn ngang', async () => {
    await captureBothWidths(page, PRODUCTS_PAGE);
  });

  expect(consoleIssues).toEqual([]);
});

test('EXP-UI3: trang khách hàng gọi route xuất với bộ lọc của danh sách', async ({ page }) => {
  test.setTimeout(120000);
  const consoleIssues = collectConsoleIssues(page);
  await loginAdmin(page);

  await test.step('Nút gửi từ khóa trên URL', async () => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await openPage(page, CUSTOMERS_PAGE, '?search=an');
    const url = await clickExportAndReadUrl(page, CUSTOMERS_PAGE);
    expect(queryOf(url)).toEqual({ search: 'an' });
    await waitForExportIdle(page);
  });

  await test.step('Ảnh 1280 px và 375 px, nút trong đầu trang, không tràn ngang', async () => {
    await captureBothWidths(page, CUSTOMERS_PAGE);
  });

  expect(consoleIssues).toEqual([]);
});

test('EXP-UI3: trang giao dịch gửi unmatched mặc định và ẩn nút ở tab COD', async ({ page }) => {
  test.setTimeout(120000);
  const consoleIssues = collectConsoleIssues(page);
  await loginAdmin(page);

  await test.step('URL không có matchStatus thì nút gửi matchStatus=unmatched', async () => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await openPage(page, PAYMENTS_PAGE);
    const url = await clickExportAndReadUrl(page, PAYMENTS_PAGE);
    expect(queryOf(url)).toEqual({ matchStatus: 'unmatched' });
    await waitForExportIdle(page);
  });

  await test.step('Chọn Tất cả và ghi chú thì request bỏ matchStatus và mang orderFlag', async () => {
    await waitForExportIdle(page);
    await page.getByLabel('Trạng thái khớp', { exact: true }).selectOption('all');
    await page.getByLabel('Ghi chú', { exact: true }).selectOption('underpaid');
    const url = await clickExportAndReadUrl(page, PAYMENTS_PAGE);
    expect(queryOf(url)).toEqual({ orderFlag: 'underpaid' });
    await waitForExportIdle(page);
  });

  await test.step('Tab COD không có nút xuất', async () => {
    await page.getByRole('tab', { name: 'COD', exact: true }).click();
    await expect(page.getByRole('tab', { name: 'COD', exact: true })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    await expect(exportButton(page)).toHaveCount(0);
  });

  await test.step('Ảnh 1280 px và 375 px, nút trong đầu trang, không tràn ngang', async () => {
    await captureBothWidths(page, PAYMENTS_PAGE);
  });

  expect(consoleIssues).toEqual([]);
});
