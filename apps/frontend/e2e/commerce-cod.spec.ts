import {
  expect,
  test,
  type BrowserContext,
  type Locator,
  type Page,
  type TestInfo,
} from '@playwright/test';
import {
  chooseOption,
  expectNoHorizontalOverflow,
  prepareShipping,
  seedProduct,
  type AddressChoice,
  type SeededProduct,
} from './admin-order-fixtures';
import { collectConsoleIssues, dismissConsent, loginAdmin } from './catalog-admin-flow';
import {
  addToCartAndOpenCheckout,
  checkoutRowFor,
  readAmount,
  readCommerceSettings,
  saveCommerceSettings,
  stubProductImages,
  type CommerceSettings,
} from './commerce-order-fixtures';
import { e2eEnvironment } from './environment';
import { readOrderConfirmation } from './outbox';

// Ngưỡng miễn phí vận chuyển thấp hơn `product.price` một khoảng `thresholdMarginBelowPrice`, nên tạm tính của đơn luôn vượt ngưỡng.
const thresholdMarginBelowPrice = 50_000;

// Các dòng tiền khách thấy ở một trang. `shippingDiscount` bằng 0 khi trang không có dòng "Miễn phí vận chuyển".
interface MoneyBreakdown {
  subtotal: number;
  shippingFee: number;
  shippingDiscount: number;
  total: number;
}

// Đọc bốn dòng tiền của một trang. `rowFor` tìm dòng theo nhãn, vì trang đặt hàng và trang đơn dùng cấu trúc khác nhau.
async function readMoneyBreakdown(
  rowFor: (label: string) => Locator,
  totalLabel: string,
): Promise<MoneyBreakdown> {
  // Chờ dòng tổng xuất hiện. Các dòng tiền cùng hiện một lượt với dòng tổng, nên đọc sau đó không bị lệch.
  await expect(rowFor(totalLabel)).toBeVisible();
  const discountRow = rowFor('Miễn phí vận chuyển');
  // Trang không có dòng phần miễn phí vận chuyển khi tạm tính chưa vượt ngưỡng. Khi đó phần miễn phí vận chuyển tính là 0.
  const shippingDiscount = (await discountRow.count()) === 0 ? 0 : await readAmount(discountRow);
  return {
    subtotal: await readAmount(rowFor('Tạm tính')),
    shippingFee: await readAmount(rowFor('Phí vận chuyển')),
    shippingDiscount,
    total: await readAmount(rowFor(totalLabel)),
  };
}

// Trang thành công và trang xem đơn dùng chung các dòng `.order-summary-row`.
const orderRowFor = (page: Page) => (label: string) =>
  page.locator('.order-summary-row').filter({ has: page.getByText(label, { exact: true }) });

// Trang không được rộng hơn khung nhìn ở máy tính và ở điện thoại. Sau đó trả khung về 1280 px.
async function expectNoOverflowAtBothWidths(page: Page) {
  for (const width of [375, 1280]) {
    await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
    await expectNoHorizontalOverflow(page);
  }
}

test.describe('COM-E2E1: khách vãng lai mua COD', () => {
  let product: SeededProduct;
  let address: AddressChoice;
  let adminContext: BrowserContext;
  let adminPage: Page;
  let previousCommerceSettings: CommerceSettings | undefined;

  test.beforeAll(async ({ browser }) => {
    // Tạo sản phẩm và cấu hình kho cần nhiều lệnh API, nên cho thời gian dài hơn mặc định.
    test.setTimeout(180000);
    adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    adminPage = await adminContext.newPage();
    // Trang sửa sản phẩm vẽ ảnh Cloudinary. Chặn để test không phụ thuộc mạng ngoài và console không có lỗi tải ảnh.
    await stubProductImages(adminPage);
    await loginAdmin(adminPage);
    // Giữ cấu hình hiện tại để trả lại sau khi test đổi ngưỡng miễn phí vận chuyển.
    previousCommerceSettings = await readCommerceSettings(adminPage);
    product = await seedProduct(adminPage);
    address = await prepareShipping(adminPage);
  });

  test.afterAll(async () => {
    try {
      // Trả đúng cấu hình cũ kể cả khi test dừng giữa chừng. Spec khác không được nhận ngưỡng thử nghiệm.
      if (previousCommerceSettings) await saveCommerceSettings(adminPage, previousCommerceSettings);
    } finally {
      await adminContext?.close();
    }
  });

  test('khách thêm giỏ, đặt COD, đọc thư, mở link xem đơn, rồi admin xóa mềm và khôi phục sản phẩm đã đặt', async ({
    page,
    browser,
  }, testInfo: TestInfo) => {
    // Test chạy khoảng 35 giây khi khởi động lạnh, vượt mức mặc định 30 giây. Mức 180 giây để dư khi máy bận.
    test.setTimeout(180000);
    // Outbox dùng chung giữa các spec. Email riêng giúp `readOrderConfirmation` không đọc nhầm thư của spec khác.
    const guestEmail = `cod-guest-${Date.now().toString(36)}@example.com`;
    const customerIssues = collectConsoleIssues(page);
    await stubProductImages(page);

    let checkoutMoney: MoneyBreakdown;
    let orderCode = '';

    await test.step('Admin đặt ngưỡng miễn phí vận chuyển thấp hơn tạm tính', async () => {
      // Đặt rõ mọi giới hạn mà luồng COD cần. Cấu hình còn sót từ spec khác không được làm tắt COD hay chặn giỏ.
      await saveCommerceSettings(adminPage, {
        // Kẹp về 0 khi `product.price` nhỏ hơn `thresholdMarginBelowPrice`. API từ chối ngưỡng âm.
        freeShippingThreshold: Math.max(0, product.price - thresholdMarginBelowPrice),
        codMaxAmount: 30_000_000,
        maxQuantityPerSku: 5,
        maxCartLines: 20,
        sepayAccountNumber: previousCommerceSettings!.sepayAccountNumber,
        sepayBankCode: previousCommerceSettings!.sepayBankCode,
      });
    });

    await test.step('Khách thêm giỏ và được miễn phí vận chuyển ở trang đặt hàng', async () => {
      await addToCartAndOpenCheckout(page, product);
      await chooseOption(page, 'checkout-address-province', address.provinceName);
      await chooseOption(page, 'checkout-address-ward', address.wardName);
      await page.locator('#checkout-address-line').fill('25 Đường COD');
      await page.getByLabel('Họ và tên', { exact: true }).fill('Lê Văn Cường');
      await page.getByLabel('Số điện thoại', { exact: true }).fill('0912345678');
      await page.getByLabel('Email', { exact: true }).fill(guestEmail);
      await expect(page.getByLabel('Thanh toán khi nhận hàng (COD)')).toBeChecked();

      checkoutMoney = await readMoneyBreakdown(checkoutRowFor(page), 'Tổng tiền');
      // Phí vận chuyển lớn hơn 0 và phần miễn phí vận chuyển bù đúng phí đó, nên tổng tiền bằng tạm tính.
      expect(checkoutMoney.shippingFee).toBeGreaterThan(0);
      expect(checkoutMoney).toEqual({
        subtotal: product.price,
        shippingFee: checkoutMoney.shippingFee,
        shippingDiscount: checkoutMoney.shippingFee,
        total: product.price,
      });
    });

    await test.step('Khách đặt đơn COD và được miễn phí vận chuyển ở trang thành công', async () => {
      await page.getByRole('button', { name: /Đặt hàng$/ }).click();
      await expect(page).toHaveURL(/\/orders\/[^/]+\/success$/);
      await expect(
        page.getByRole('heading', { level: 1, name: 'Đặt hàng thành công' }),
      ).toBeVisible();
      // Mã đơn có tiền tố `PK`. Thư xác nhận và trang xem đơn tra theo đúng mã này.
      orderCode = (await page.locator('.order-success-code').innerText()).trim();
      expect(orderCode).toMatch(/^PK/);
      await expect(page.locator('.order-item-row').filter({ hasText: product.name })).toBeVisible();

      // Tiền trên trang thành công phải trùng tiền khách đã thấy trước khi đặt.
      expect(await readMoneyBreakdown(orderRowFor(page), 'Tổng cộng')).toEqual(checkoutMoney);
      await expectNoOverflowAtBothWidths(page);
      await page.screenshot({ path: testInfo.outputPath('success-page.png'), fullPage: true });
      // Giỏ đã được xóa sau khi đặt đơn.
      await expect(page.getByRole('button', { name: 'Giỏ hàng, trống' })).toBeVisible();
    });

    let emailLink = '';
    await test.step('Thư xác nhận trong outbox có link xem đơn kèm token', async () => {
      const confirmation = await readOrderConfirmation(guestEmail, orderCode);
      const linkUrl = new URL(confirmation.link);
      expect(linkUrl.origin).toBe(e2eEnvironment.webOrigin);
      expect(linkUrl.pathname).toBe(`/orders/${orderCode}`);
      expect(linkUrl.searchParams.get('t')).toBeTruthy();
      emailLink = confirmation.link;
    });

    await test.step('Mở link trong thư ở context mới thì thấy đúng đơn và được miễn phí vận chuyển', async () => {
      // Context mới không có cookie hay router state của khách. Chỉ token trong link được dùng để xem đơn.
      const linkContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
      try {
        const linkPage = await linkContext.newPage();
        const linkIssues = collectConsoleIssues(linkPage);
        await stubProductImages(linkPage);
        // Mở đúng link lấy từ thư, không dựng lại link từ mã đơn.
        await linkPage.goto(emailLink);
        await dismissConsent(linkPage);

        await expect(
          linkPage.getByRole('heading', { level: 1, name: `Đơn hàng #${orderCode}` }),
        ).toBeVisible();
        await expect(linkPage.getByText(product.name)).toBeVisible();
        await expect(
          linkPage.locator('.order-badge').filter({ hasText: 'Chờ xác nhận' }),
        ).toBeVisible();
        await expect(linkPage.getByText('Thanh toán khi nhận hàng (COD)')).toBeVisible();
        // Tiền trên trang xem đơn phải trùng trang thành công, gồm cả phần miễn phí vận chuyển.
        expect(await readMoneyBreakdown(orderRowFor(linkPage), 'Tổng cộng')).toEqual(checkoutMoney);
        await expectNoOverflowAtBothWidths(linkPage);
        await linkPage.screenshot({
          path: testInfo.outputPath('order-from-email.png'),
          fullPage: true,
        });

        expect(linkIssues).toEqual([]);
      } finally {
        await linkContext.close();
      }
    });

    await test.step('Admin xóa mềm sản phẩm đã đặt rồi khôi phục', async () => {
      const adminIssues = collectConsoleIssues(adminPage);

      await adminPage.goto(`/admin/products/${product.id}`);
      await adminPage.getByRole('button', { name: 'Xóa sản phẩm', exact: true }).click();
      const dialog = adminPage.getByRole('dialog');
      await expect(dialog).toContainText(product.name);

      const deleteResponse = adminPage.waitForResponse(
        (response) =>
          response.request().method() === 'DELETE' &&
          response.url().endsWith(`/api/admin/products/${product.id}`),
      );
      await dialog.getByRole('button', { name: 'Xác nhận xóa', exact: true }).click();
      const softDeleted = await deleteResponse;
      expect(softDeleted.status()).toBe(200);
      const deletionBody = await softDeleted.json();
      expect(deletionBody.data.outcome).toBe('soft_deleted');
      expect(deletionBody.data.product.deletedAt).not.toBeNull();

      // Xóa mềm đóng hộp thoại, khóa form và chỉ cho khôi phục.
      await expect(dialog).toBeHidden();
      await expect(adminPage.locator('.status-current .badge')).toHaveText('Đã xóa mềm');
      await expect(adminPage.locator('#product-name')).toBeDisabled();
      await expect(
        adminPage.getByRole('button', { name: 'Khôi phục sản phẩm', exact: true }),
      ).toBeVisible();

      // Dữ liệu còn trong admin nhưng khách không tải được sản phẩm qua API công khai.
      const softDetail = await adminPage.request.get(`/api/admin/products/${product.id}`);
      expect(softDetail.status(), await softDetail.text()).toBe(200);
      expect((await softDetail.json()).data.deletedAt).not.toBeNull();
      expect((await page.request.get(`/api/products/${product.slug}`)).status()).toBe(404);

      const restoreResponse = adminPage.waitForResponse(
        (response) =>
          response.request().method() === 'POST' &&
          response.url().endsWith(`/api/admin/products/${product.id}/restoration`),
      );
      await adminPage.getByRole('button', { name: 'Khôi phục sản phẩm', exact: true }).click();
      const restored = await restoreResponse;
      expect(restored.status()).toBe(200);
      expect((await restored.json()).data.deletedAt).toBeNull();

      // Khôi phục trả sản phẩm về trạng thái trước khi xóa và mở lại trang khách.
      await expect(adminPage.locator('.status-current .badge')).toHaveText('Đang bán');
      await expect(
        adminPage.getByRole('button', { name: 'Khôi phục sản phẩm', exact: true }),
      ).toHaveCount(0);
      expect((await page.request.get(`/api/products/${product.slug}`)).status()).toBe(200);
      await page.goto(`/p/${product.slug}`);
      await expect(page.getByRole('heading', { level: 1, name: product.name })).toBeVisible();

      expect(adminIssues).toEqual([]);
    });

    expect(customerIssues).toEqual([]);
  });
});
