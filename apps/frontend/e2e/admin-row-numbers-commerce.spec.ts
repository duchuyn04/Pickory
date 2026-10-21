import { randomInt } from 'node:crypto';
import { expect, test, type Locator, type Page } from '@playwright/test';
import { prepareShipping, seedProduct } from './admin-order-fixtures';
import { loginAdmin } from './catalog-admin-flow';
import {
  confirmAndShipOrderByApi,
  deliverShipmentByWebhook,
  loginSeededCustomer,
  openIsolatedSession,
  placeCodOrderByApi,
} from './commerce-order-fixtures';
import { e2eEnvironment } from './environment';

const accountNumber = '1017588888';
const webOriginHeaders = { Origin: e2eEnvironment.webOrigin };

// Trang không được rộng hơn khung nhìn. Bảng rộng phải cuộn trong khung riêng của nó.
async function expectNoHorizontalOverflow(page: Page) {
  const widths = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth,
  }));
  expect(widths.content).toBeLessThanOrEqual(widths.viewport);
}

// Chụp cả trang ở hai bề rộng. Mỗi lần kiểm trang không tràn ngang.
async function captureBothWidths(
  page: Page,
  table: string,
  outputPath: (name: string) => string,
  ready: Locator,
) {
  for (const width of [1280, 375]) {
    await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
    await expect(ready).toBeVisible();
    await expectNoHorizontalOverflow(page);
    await page.screenshot({ path: outputPath(`FLT-UI11-${table}-${width}.png`), fullPage: true });
  }
  await page.setViewportSize({ width: 1280, height: 800 });
}

// Đọc `text-align` đã tính của một ô, để phép kiểm căn phải dựa trên kiểu thật của trình duyệt.
async function textAlignOf(cell: Locator) {
  return cell.evaluate((element) => getComputedStyle(element).textAlign);
}

// Kiểm một bảng có cột đầu là STT ở 1280 px: tiêu đề `STT`, tiêu đề và ô đầu căn phải.
async function expectRowNumberColumn(table: Locator) {
  const headerCell = table.locator('thead th').first();
  await expect(headerCell).toHaveText('STT');
  expect(await textAlignOf(headerCell)).toBe('right');
  expect(await textAlignOf(table.locator('tbody tr').first().locator('td').first())).toBe('right');
  return headerCell;
}

// Bảng dùng chung DB với các spec khác, nên không cô lập được dòng riêng. Chỉ kiểm STT liền nhau từ 1 theo số dòng đang hiện.
async function expectConsecutiveRowNumbers(rows: Locator, minimumRows: number) {
  // Chờ dòng đầu hiện ra, vì chữ nạp có thể chưa kịp xuất hiện và bảng có thể còn rỗng.
  await expect(rows.first()).toBeVisible();
  const rowCount = await rows.count();
  expect(rowCount).toBeGreaterThanOrEqual(minimumRows);
  const expected = Array.from({ length: rowCount }, (_, index) => String(index + 1));
  await expect(rows.locator('td:first-child')).toHaveText(expected);
}

test('FLT-UI11: màn đơn hàng, đối soát và đánh giá có cột STT', async ({ browser }, testInfo) => {
  // Test dựng ba đơn, giao hàng, đánh giá và giao dịch SePay rồi đi qua bốn bảng, nên cần nhiều thời gian hơn mặc định.
  test.setTimeout(300000);
  const admin = await openIsolatedSession(browser);
  const customerA = await openIsolatedSession(browser);
  const customerB = await openIsolatedSession(browser);
  const outputPath = testInfo.outputPath.bind(testInfo);
  const suffix = Date.now().toString(36);

  // Webhook SePay chỉ ghi giao dịch chưa khớp khi số tài khoản trùng cấu hình. Lưu cấu hình cũ để khôi phục.
  await loginAdmin(admin.page);
  const previousResponse = await admin.page.request.get('/api/admin/commerce/settings');
  expect(previousResponse.status()).toBe(200);
  const previous = (await previousResponse.json()).data as {
    freeShippingThreshold: number;
    codMaxAmount: number;
    maxQuantityPerSku: number;
    maxCartLines: number;
    sepayAccountNumber: string | null;
    sepayBankCode: string | null;
  };
  // Response có thêm trường như `updatedAt` mà route ghi từ chối, nên chỉ gửi đúng sáu trường cấu hình.
  const editableSettings = {
    freeShippingThreshold: previous.freeShippingThreshold,
    codMaxAmount: previous.codMaxAmount,
    maxQuantityPerSku: previous.maxQuantityPerSku,
    maxCartLines: previous.maxCartLines,
    sepayAccountNumber: previous.sepayAccountNumber,
    sepayBankCode: previous.sepayBankCode,
  };

  try {
    const withSepay = await admin.page.request.put('/api/admin/commerce/settings', {
      headers: webOriginHeaders,
      data: { ...editableSettings, sepayAccountNumber: accountNumber, sepayBankCode: 'VCB' },
    });
    expect(withSepay.status(), await withSepay.text()).toBe(200);

    let firstOrderCode = '';
    await test.step('Dựng dữ liệu: ba đơn COD đã giao, hai đánh giá, hai giao dịch SePay', async () => {
      const product = await seedProduct(admin.page);
      await prepareShipping(admin.page);

      // Khách A đặt hai đơn, khách B đặt một đơn. Mỗi khách có context riêng vì giỏ hàng gắn với tài khoản đang đăng nhập.
      await loginSeededCustomer(customerA.page, e2eEnvironment.reviewsVisibleCustomerEmail);
      await loginSeededCustomer(customerB.page, e2eEnvironment.reviewsHiddenCustomerEmail);
      const placements = [customerA, customerA, customerB];
      for (const [index, session] of placements.entries()) {
        const code = await placeCodOrderByApi(session.page, product);
        if (index === 0) firstOrderCode = code;
        const trackingCode = await confirmAndShipOrderByApi(admin.page, code);
        await deliverShipmentByWebhook(admin.page, trackingCode);
      }

      // Mỗi khách đã nhận hàng viết được một đánh giá cho sản phẩm.
      for (const [session, body] of [
        [customerA, `Chuột êm ${suffix}`],
        [customerB, `Chuột trễ ${suffix}`],
      ] as const) {
        const review = await session.page.request.post(`/api/products/${product.id}/reviews`, {
          headers: webOriginHeaders,
          data: { rating: 4, body },
        });
        expect(review.status(), await review.text()).toBe(201);
      }

      // Nội dung không chứa mã đơn nào, nên webhook ghi hai giao dịch ở trạng thái chưa khớp.
      for (const letter of ['A', 'B']) {
        const webhook = await admin.page.request.post('/api/webhooks/sepay', {
          headers: { Authorization: `Apikey ${e2eEnvironment.sepayWebhookApiKey}` },
          data: {
            id: randomInt(100000000, 999999999),
            gateway: 'Vietcombank',
            transactionDate: '2026-10-12 10:00:00',
            accountNumber,
            subAccount: '',
            code: null,
            content: `E2E STT ${letter} ${suffix}`,
            transferType: 'in',
            description: '',
            transferAmount: 123000,
            accumulated: 105000000,
            referenceCode: `E2E-STT-${letter}-${suffix}`,
          },
        });
        expect(webhook.status(), await webhook.text()).toBe(200);
      }
    });

    const page = admin.page;
    await page.setViewportSize({ width: 1280, height: 800 });

    await test.step('Màn đơn hàng: lọc theo mã đơn của spec thì STT 1, không lọc thì STT liền nhau', async () => {
      await page.goto(`/admin/orders?search=${encodeURIComponent(firstOrderCode)}`);
      const table = page.locator('table.order-table');
      const rows = table.locator('tbody tr');
      await expect(rows).toHaveCount(1);
      await expect(rows.first()).toContainText(firstOrderCode);
      const headerCell = await expectRowNumberColumn(table);
      await expect(rows.first().locator('td').first()).toHaveText('1');

      // Không lọc: spec đã tạo ba đơn nên bảng có ít nhất ba dòng.
      await page.goto('/admin/orders');
      await expectConsecutiveRowNumbers(rows, 3);

      await captureBothWidths(page, 'orders', outputPath, headerCell);
    });

    await test.step('Đối soát, tab SePay: STT liền nhau từ 1', async () => {
      await page.goto('/admin/payments');
      const table = page.locator('table.payment-table');
      const rows = table.locator('tbody tr');
      await expectConsecutiveRowNumbers(rows, 2);
      const headerCell = await expectRowNumberColumn(table);

      await captureBothWidths(page, 'payments-sepay', outputPath, headerCell);
    });

    await test.step('Đối soát, tab COD: STT liền nhau từ 1', async () => {
      await page.goto('/admin/payments?tab=cod');
      const table = page.locator('table.cod-table');
      const rows = table.locator('tbody tr');
      await expectConsecutiveRowNumbers(rows, 3);
      const headerCell = await expectRowNumberColumn(table);

      await captureBothWidths(page, 'payments-cod', outputPath, headerCell);
    });

    await test.step('Màn đánh giá: STT liền nhau từ 1, dạng thẻ ở 375 px có nhãn STT', async () => {
      await page.goto('/admin/reviews');
      const table = page.locator('table.review-table');
      const rows = table.locator('tbody tr');
      await expectConsecutiveRowNumbers(rows, 2);
      const headerCell = await expectRowNumberColumn(table);

      await captureBothWidths(page, 'reviews', outputPath, headerCell);

      // Ở 375 px bảng đổi sang dạng thẻ, và `::before` của ô lấy nhãn từ `data-label`.
      await page.setViewportSize({ width: 375, height: 812 });
      const numberCell = rows.first().locator('td').first();
      await expect(numberCell).toHaveAttribute('data-label', 'STT');
      const label = await numberCell.evaluate(
        (element) => getComputedStyle(element, '::before').content,
      );
      expect(label).toBe('"STT"');
      // Ô STT rộng như ô tiếp theo của thẻ, nghĩa là rule `width: 1%` của cột STT đã bị bỏ ở dạng thẻ.
      const numberBox = (await numberCell.boundingBox())!;
      const nextBox = (await rows.first().locator('th').first().boundingBox())!;
      expect(Math.abs(numberBox.width - nextBox.width)).toBeLessThanOrEqual(1);
    });

    const sessions = [admin, customerA, customerB];
    for (const session of sessions) expect(session.consoleIssues).toEqual([]);
  } finally {
    // Khôi phục cấu hình kể cả khi bước nào đó thất bại, để các spec khác không bị ảnh hưởng.
    const restored = await admin.page.request.put('/api/admin/commerce/settings', {
      headers: webOriginHeaders,
      data: editableSettings,
    });
    expect(restored.status(), await restored.text()).toBe(200);
    await admin.context.close();
    await customerA.context.close();
    await customerB.context.close();
  }
});
