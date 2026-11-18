import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, test, type Browser, type Page } from '@playwright/test';
import {
  expectNoHorizontalOverflow,
  placeCodOrder,
  prepareShipping,
  seedProduct,
} from './admin-order-fixtures';
import { collectConsoleIssues, loginAdmin } from './catalog-admin-flow';
import { e2eEnvironment } from './environment';

const EVIDENCE_DIRECTORY = fileURLToPath(
  new URL('../../../evidence/exports-printing/EXP-UI6', import.meta.url),
);

const ORDER_STATUS_LABELS: Record<string, string> = {
  pending_payment: 'Chờ thanh toán',
  pending_confirmation: 'Chờ xác nhận',
  confirmed: 'Đã xác nhận',
  shipping: 'Đang giao hàng',
  delivered: 'Đã giao hàng',
  cancelled: 'Đã hủy',
  refunded: 'Đã hoàn tiền',
};

interface OrderListItemDto {
  code: string;
  status: string;
}

interface OrderListResponseDto {
  data: {
    items: OrderListItemDto[];
    total: number;
    page: number;
    limit: number;
  };
}

// Đọc danh sách đơn hiện có và đặt bổ sung đơn thật nếu DB chưa đủ 3 đơn.
async function ensureAtLeastThreeOrders(
  adminPage: Page,
  browser: Browser,
): Promise<OrderListItemDto[]> {
  const initial = await readOrdersFromApi(adminPage);
  if (initial.items.length >= 3) {
    return initial.items;
  }

  const product = await seedProduct(adminPage);
  const address = await prepareShipping(adminPage);
  const missingCount = 3 - initial.items.length;

  for (let index = 0; index < missingCount; index += 1) {
    // Mở ngữ cảnh khách riêng để đặt đơn không mang cookie đăng nhập nhân viên.
    const guestContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    const guestPage = await guestContext.newPage();
    try {
      await placeCodOrder(guestPage, product, address);
    } finally {
      await guestContext.close();
    }
  }

  const updated = await readOrdersFromApi(adminPage);
  expect(updated.items.length).toBeGreaterThanOrEqual(3);
  return updated.items;
}

async function readOrdersFromApi(adminPage: Page) {
  const response = await adminPage.request.get('/api/admin/orders?limit=20');
  expect(response.status(), await response.text()).toBe(200);
  const body = (await response.json()) as OrderListResponseDto;
  return body.data;
}

function evidencePath(fileName: string): string {
  return path.join(EVIDENCE_DIRECTORY, fileName);
}

test('EXP-UI6: nút in ở chi tiết đơn và chọn nhiều đơn ở danh sách', async ({ browser, page }) => {
  test.setTimeout(180000);
  const consoleIssues = collectConsoleIssues(page);

  await loginAdmin(page);
  const initialOrders = await ensureAtLeastThreeOrders(page, browser);
  const firstOrder = initialOrders[0]!;
  const firstThreeCodes = initialOrders.slice(0, 3).map((item) => item.code);

  await test.step('Chi tiết đơn mở trang in trong tab mới và chụp ảnh 1280 px, 375 px', async () => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto(`/admin/orders/${encodeURIComponent(firstOrder.code)}`);
    await expect(
      page.getByRole('heading', { level: 1, name: `Đơn hàng ${firstOrder.code}` }),
    ).toBeVisible();

    const printLink = page.getByRole('link', { name: 'In hóa đơn', exact: true });
    await expect(printLink).toBeVisible();
    await expect(printLink).toHaveAttribute(
      'href',
      `/admin/orders/print?codes=${encodeURIComponent(firstOrder.code)}`,
    );
    await expectNoHorizontalOverflow(page);
    await page.screenshot({ path: evidencePath('01-chi-tiet-don-1280.png'), fullPage: true });

    // Mở tab mới và kiểm URL rồi đóng ngay vì route dữ liệu in thuộc task sau.
    const popupPromise = page.context().waitForEvent('page');
    await printLink.click();
    const popup = await popupPromise;
    await expect(popup).toHaveURL(
      `${e2eEnvironment.webOrigin}/admin/orders/print?codes=${encodeURIComponent(firstOrder.code)}`,
    );
    await popup.close();
    await expect(page).toHaveURL(
      `${e2eEnvironment.webOrigin}/admin/orders/${encodeURIComponent(firstOrder.code)}`,
    );

    await page.setViewportSize({ width: 375, height: 812 });
    await expect(printLink).toBeVisible();
    await expectNoHorizontalOverflow(page);
    await page.screenshot({ path: evidencePath('02-chi-tiet-don-375.png'), fullPage: true });
  });

  await test.step('Danh sách: chưa chọn thì nút khóa, chọn 3 đơn in đúng thứ tự và chụp ảnh', async () => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/admin/orders');
    await expect(page.getByRole('heading', { level: 1, name: 'Quản lý đơn hàng' })).toBeVisible();

    const selectionBar = page.locator('.order-selection-bar');
    const printButton = selectionBar.getByRole('button', { name: 'In hóa đơn', exact: true });
    await expect(selectionBar).toContainText('Đã chọn 0 đơn');
    await expect(printButton).toBeDisabled();
    await expectNoHorizontalOverflow(page);
    await page.screenshot({
      path: evidencePath('03-danh-sach-chua-chon-1280.png'),
      fullPage: true,
    });

    // Chọn lần lượt theo thứ tự ngược để kiểm URL vẫn giữ thứ tự dòng trên bảng.
    for (const code of [...firstThreeCodes].reverse()) {
      await page.getByRole('checkbox', { name: `Chọn đơn ${code}`, exact: true }).click();
    }
    await expect(selectionBar).toContainText('Đã chọn 3 đơn');
    await expect(printButton).toBeEnabled();
    await page.screenshot({
      path: evidencePath('04-danh-sach-da-chon-3-1280.png'),
      fullPage: true,
    });

    const expectedCodesQuery = firstThreeCodes.map((code) => encodeURIComponent(code)).join(',');
    const popupPromise = page.context().waitForEvent('page');
    await printButton.click();
    const popup = await popupPromise;
    await expect(popup).toHaveURL(
      `${e2eEnvironment.webOrigin}/admin/orders/print?codes=${expectedCodesQuery}`,
    );
    await popup.close();
  });

  await test.step('Đổi tab trạng thái thì danh sách bỏ chọn hết về 0 đơn', async () => {
    const statusLabel = ORDER_STATUS_LABELS[firstOrder.status];
    expect(statusLabel).toBeDefined();

    await page.getByRole('tab', { name: statusLabel, exact: true }).click();
    await expect(page.locator('.order-selection-bar')).toContainText('Đã chọn 0 đơn');
    await expect(
      page.locator('.order-selection-bar').getByRole('button', { name: 'In hóa đơn', exact: true }),
    ).toBeDisabled();
  });

  await test.step('Ở 375 px chọn đơn và nút in bấm được, trang không tràn ngang', async () => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto('/admin/orders');
    await expect(page.locator('.order-selection-bar')).toContainText('Đã chọn 0 đơn');

    const firstCheckbox = page.getByRole('checkbox', {
      name: `Chọn đơn ${firstThreeCodes[0]}`,
      exact: true,
    });
    await firstCheckbox.click();
    await expect(page.locator('.order-selection-bar')).toContainText('Đã chọn 1 đơn');

    const printButton = page
      .locator('.order-selection-bar')
      .getByRole('button', { name: 'In hóa đơn', exact: true });
    await expect(printButton).toBeEnabled();
    await expectNoHorizontalOverflow(page);
    await page.screenshot({ path: evidencePath('05-danh-sach-da-chon-375.png'), fullPage: true });
  });

  expect(consoleIssues).toEqual([]);
});

test('EXP-UI6: giới hạn 50 đơn khóa các ô chưa chọn và hiện chú thích', async ({
  browser,
  page,
}) => {
  test.setTimeout(180000);
  const consoleIssues = collectConsoleIssues(page);

  await loginAdmin(page);
  await ensureAtLeastThreeOrders(page, browser);

  await page.setViewportSize({ width: 1280, height: 800 });

  // Chỉ chặn đúng route danh sách đơn đã có trong backend để tạo trang giả 60 dòng.
  await page.route('**/api/admin/orders**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    if (request.method() !== 'GET' || url.pathname !== '/api/admin/orders') {
      await route.continue();
      return;
    }

    const response = await route.fetch();
    const original = (await response.json()) as {
      data: { items: Record<string, unknown>[]; total: number; page: number; limit: number };
    };
    const baseItem = original.data.items[0];
    if (!baseItem) {
      throw new Error('Không có đơn mẫu trong phản hồi của danh sách đơn hàng.');
    }
    const sixtyItems = Array.from({ length: 60 }, (_, index) => ({
      ...baseItem,
      code: `PK60-${String(index + 1).padStart(2, '0')}`,
    }));

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        data: {
          items: sixtyItems,
          total: 60,
          page: 1,
          limit: 60,
        },
      }),
    });
  });

  await page.goto('/admin/orders');
  await expect(page.locator('table.order-table tbody tr')).toHaveCount(60);

  await page.getByRole('checkbox', { name: 'Chọn tất cả đơn trong trang', exact: true }).click();
  await expect(page.locator('.order-selection-bar')).toContainText('Đã chọn 50 đơn');
  await expect(page.locator('.order-selection-bar')).toContainText('Mỗi lần in tối đa 50 đơn');

  // Kiểm tra các ô đã chọn vẫn mở và các ô từ dòng 51 trở đi bị khóa.
  await expect(page.getByRole('checkbox', { name: 'Chọn đơn PK60-01', exact: true })).toBeEnabled();
  await expect(page.getByRole('checkbox', { name: 'Chọn đơn PK60-50', exact: true })).toBeEnabled();
  await expect(
    page.getByRole('checkbox', { name: 'Chọn đơn PK60-51', exact: true }),
  ).toBeDisabled();
  await expect(
    page.getByRole('checkbox', { name: 'Chọn đơn PK60-60', exact: true }),
  ).toBeDisabled();
  await expectNoHorizontalOverflow(page);
  await page.screenshot({ path: evidencePath('06-danh-sach-chon-50-1280.png'), fullPage: true });

  expect(consoleIssues).toEqual([]);
});
