import {
  expect,
  test,
  type BrowserContext,
  type Locator,
  type Page,
  type Request,
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
import {
  collectConsoleIssues,
  dismissConsent,
  isAllowedLoadFailure,
  loginAdmin,
} from './catalog-admin-flow';
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

  test('khách thêm giỏ, đặt COD, đọc thư, mở link xem đơn, rồi admin không xóa được sản phẩm đã đặt', async ({
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

    await test.step('Admin xóa cứng sản phẩm đã đặt thì nhận lỗi PRODUCT_HAS_ORDERS', async () => {
      // Chỉ bỏ qua đúng một dòng lỗi 409, tức dòng "Failed to load resource" của request xóa bị từ chối. Mọi dòng khác vẫn là lỗi.
      const expectedDeleteFailure = {
        urlPattern: new RegExp(`/api/admin/products/${product.id}$`),
        status: 409,
      };
      const adminIssues = collectConsoleIssues(adminPage, [expectedDeleteFailure]);
      // Một hàm duy nhất nhận ra request xóa sản phẩm, để listener và `waitForResponse` dùng cùng luật.
      const isProductDeleteRequest = (request: Request) =>
        request.method() === 'DELETE' && expectedDeleteFailure.urlPattern.test(request.url());
      // Số request xóa chứng minh test chỉ gửi một request xóa. Đăng ký trước khi bấm để không bỏ sót request.
      let deleteRequestCount = 0;
      adminPage.on('request', (request) => {
        if (isProductDeleteRequest(request)) deleteRequestCount += 1;
      });
      // Số dòng bỏ qua chứng minh dòng lỗi 409 đã tới và được tính.
      let ignoredDeleteFailures = 0;
      adminPage.on('console', (message) => {
        if (isAllowedLoadFailure(message, expectedDeleteFailure)) ignoredDeleteFailures += 1;
      });

      await adminPage.goto(`/admin/products/${product.id}`);
      await adminPage.getByRole('button', { name: 'Xóa sản phẩm', exact: true }).click();
      const dialog = adminPage.getByRole('dialog');
      await expect(dialog).toContainText(product.name);

      const deleteResponse = adminPage.waitForResponse((response) =>
        isProductDeleteRequest(response.request()),
      );
      await dialog.getByRole('button', { name: 'Xóa vĩnh viễn', exact: true }).click();
      const response = await deleteResponse;
      // Sự kiện `request` đến trước response, nên số request xóa đã ổn định ở đây.
      expect(deleteRequestCount).toBe(1);
      expect(response.status()).toBe(409);
      expect((await response.json()).error.code).toBe('PRODUCT_HAS_ORDERS');

      // Hộp thoại còn mở và báo lý do bằng tiếng Việt, để admin biết vì sao không xóa được.
      await expect(dialog).toBeVisible();
      await expect(dialog.getByRole('alert')).toContainText(
        'Sản phẩm đã có đơn hàng nên không xóa được.',
      );
      await adminPage.screenshot({ path: testInfo.outputPath('delete-blocked.png') });

      // Sản phẩm vẫn còn: API admin trả 200 và trang khách vẫn mở được.
      const stillThere = await adminPage.request.get(`/api/admin/products/${product.id}`);
      expect(stillThere.status(), await stillThere.text()).toBe(200);
      await page.goto(`/p/${product.slug}`);
      await expect(page.getByRole('heading', { level: 1, name: product.name })).toBeVisible();

      // Sự kiện console đến không đồng bộ với response. Chờ tới khi dòng lỗi 409 được ghi nhận.
      await expect.poll(() => ignoredDeleteFailures).toBe(1);
      expect(adminIssues).toEqual([]);
    });

    expect(customerIssues).toEqual([]);
  });
});
