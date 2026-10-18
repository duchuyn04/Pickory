import { expect, test, type BrowserContext, type Page } from '@playwright/test';
import {
  chooseOption,
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
  sendSepayWebhook,
  stubProductImages,
  stubWebFonts,
  type CommerceSettings,
} from './commerce-order-fixtures';
import { e2eEnvironment } from './environment';
import { readOrderConfirmation } from './outbox';

const sepayAccountNumber = '1017588888';
const sepayBankCode = 'VCB';
// Hạn mức COD thấp hơn mọi tổng đơn của spec, nên đơn nào cũng vượt hạn mức.
const codMaxAmount = 100_000;
// Trang chờ hỏi lại API mỗi 3 giây, nên 5 giây là mức khách chấp nhận được sau khi tiền về.
const redirectAfterWebhookLimitMs = 5000;
const guestAddressLine = '25 Đường SePay';
// COD được chọn sẵn, nên preview đầu tiên trả 422 vì đơn vượt hạn mức rồi trang tự chuyển sang chuyển khoản.
// Trình duyệt ghi lỗi tải này vào console, nên test chỉ bỏ qua đúng lỗi này.
const expectedCodPreviewFailure = { urlPattern: /\/api\/checkout\/previews$/, status: 422 };

// Chữ trên trang đặt hàng viết hạn mức theo định dạng tiền của khách, nên spec dùng cùng định dạng.
const codLimitReason = `Đơn trên ${codMaxAmount.toLocaleString('vi-VN')} đ chỉ thanh toán chuyển khoản`;

// Điền địa chỉ và liên hệ của khách vãng lai. Địa chỉ đi qua `AddressPicker` như khách thật.
async function fillGuestCheckoutForm(page: Page, address: AddressChoice, guestEmail: string) {
  await chooseOption(page, 'checkout-address-province', address.provinceName);
  await chooseOption(page, 'checkout-address-ward', address.wardName);
  await page.locator('#checkout-address-line').fill(guestAddressLine);
  await page.getByLabel('Họ và tên', { exact: true }).fill('Phạm Thị Dung');
  await page.getByLabel('Số điện thoại', { exact: true }).fill('0912345678');
  await page.getByLabel('Email', { exact: true }).fill(guestEmail);
}

test.describe('COM-E2E2: khách mua SePay', () => {
  let product: SeededProduct;
  let address: AddressChoice;
  let adminContext: BrowserContext;
  let adminPage: Page;
  let adminIssues: string[];
  let previousCommerceSettings: CommerceSettings | undefined;

  test.beforeAll(async ({ browser }) => {
    // Tạo sản phẩm và cấu hình kho cần nhiều lệnh API, nên cho thời gian dài hơn mặc định.
    test.setTimeout(180000);
    adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    adminPage = await adminContext.newPage();
    adminIssues = collectConsoleIssues(adminPage);
    // Trang sửa sản phẩm vẽ ảnh Cloudinary. Chặn để test không phụ thuộc mạng ngoài.
    await stubProductImages(adminPage);
    await stubWebFonts(adminPage);
    await loginAdmin(adminPage);
    // Giữ cấu hình hiện tại để trả lại sau khi test đổi hạn mức COD và tài khoản nhận tiền.
    previousCommerceSettings = await readCommerceSettings(adminPage);
    product = await seedProduct(adminPage);
    address = await prepareShipping(adminPage);
    // Đặt rõ mọi giới hạn mà luồng cần. Cấu hình còn sót từ spec khác không được làm đổi kết quả.
    await saveCommerceSettings(adminPage, {
      // Ngưỡng cao hơn tạm tính, nên đơn luôn phải trả phí vận chuyển và tổng đơn lớn hơn giá sản phẩm.
      freeShippingThreshold: product.price + 1_000_000,
      codMaxAmount,
      maxQuantityPerSku: 5,
      maxCartLines: 20,
      sepayAccountNumber,
      sepayBankCode,
    });
  });

  test.afterAll(async () => {
    try {
      // Trả đúng cấu hình cũ kể cả khi test dừng giữa chừng. Spec khác không được nhận hạn mức thử nghiệm.
      if (previousCommerceSettings) await saveCommerceSettings(adminPage, previousCommerceSettings);
    } finally {
      await adminContext?.close();
    }
  });

  test('đơn trên hạn mức COD không chọn được COD, và API từ chối đơn COD', async ({ page }) => {
    // Test chạy khoảng 20 giây khi khởi động lạnh. Mức 120 giây để dư khi máy bận.
    test.setTimeout(120000);
    const customerIssues = collectConsoleIssues(page, [expectedCodPreviewFailure]);
    await stubProductImages(page);
    await stubWebFonts(page);
    const guestEmail = `sepay-limit-${Date.now().toString(36)}@example.com`;

    await addToCartAndOpenCheckout(page, product);
    // Đăng ký chờ trước khi điền địa chỉ, vì preview được gửi ngay khi địa chỉ đủ. Lấy mã tỉnh và mã phường từ chính request này.
    const previewRequest = page.waitForRequest(
      (request) =>
        request.method() === 'POST' &&
        request.url().endsWith('/api/checkout/previews') &&
        request.postDataJSON()?.address?.line === guestAddressLine,
    );
    await fillGuestCheckoutForm(page, address, guestEmail);
    const previewAddress = (await previewRequest).postDataJSON().address as {
      provinceCode: string;
      wardCode: string;
      line: string;
    };

    await test.step('Trang đặt hàng tắt COD, nêu hạn mức và chọn chuyển khoản', async () => {
      const cod = page.getByLabel('Thanh toán khi nhận hàng (COD)');
      await expect(cod).toBeDisabled();
      await expect(page.locator('#checkout-cod-reason')).toHaveText(codLimitReason);
      await expect(page.getByLabel('Chuyển khoản (VietQR)')).toBeChecked();
    });

    await test.step('API đặt đơn trả 422 COD_NOT_ALLOWED cho đơn COD', async () => {
      // Tổng đơn khách đang thấy. Backend kiểm hạn mức COD trước khi so tổng dự kiến, nên giá trị này không làm đổi lỗi.
      const expectedTotal = await readAmount(checkoutRowFor(page)('Tổng tiền'));
      expect(expectedTotal).toBeGreaterThan(codMaxAmount);

      // `page.request` dùng cùng cookie của khách, nên server đọc đúng giỏ vừa thêm. Header `Origin` để qua kiểm CSRF.
      const response = await page.request.post('/api/orders', {
        headers: {
          Origin: e2eEnvironment.webOrigin,
          'Idempotency-Key': `e2e-cod-limit-${Date.now().toString(36)}-0000`,
        },
        data: {
          contact: { name: 'Phạm Thị Dung', phone: '0912345678', email: guestEmail },
          address: previewAddress,
          note: null,
          paymentMethod: 'cod',
          invoice: null,
          expectedTotal,
        },
      });
      expect(response.status(), await response.text()).toBe(422);
      expect((await response.json()).error.code).toBe('COD_NOT_ALLOWED');
    });

    // Không có đơn nào được tạo, nên khách vẫn ở trang đặt hàng.
    await expect(page).toHaveURL(/\/checkout$/);
    expect(customerIssues).toEqual([]);
    expect(adminIssues).toEqual([]);
  });

  test('khách đặt đơn SePay, nhận thư, và trang chờ chuyển sang thành công trong 5 giây sau webhook', async ({
    page,
    browser,
  }) => {
    // Test chạy khoảng 30 giây khi khởi động lạnh, vượt mức mặc định 30 giây. Mức 180 giây để dư khi máy bận.
    test.setTimeout(180000);
    const customerIssues = collectConsoleIssues(page, [expectedCodPreviewFailure]);
    await stubProductImages(page);
    await stubWebFonts(page);
    // Outbox dùng chung giữa các spec. Email riêng giúp `readOrderConfirmation` không đọc nhầm thư của spec khác.
    const guestEmail = `sepay-guest-${Date.now().toString(36)}@example.com`;

    let orderCode = '';
    let accessToken = '';
    let checkoutTotal = 0;

    await test.step('Khách đặt đơn SePay và vào trang chờ thanh toán có QR đúng số tiền', async () => {
      await addToCartAndOpenCheckout(page, product);
      await fillGuestCheckoutForm(page, address, guestEmail);
      // Hạn mức thấp nên giao diện đã chuyển sang chuyển khoản. Bước này khẳng định lại để test không phụ thuộc bước tự chuyển.
      await expect(page.getByLabel('Chuyển khoản (VietQR)')).toBeChecked();
      checkoutTotal = await readAmount(checkoutRowFor(page)('Tổng tiền'));
      expect(checkoutTotal).toBeGreaterThan(product.price);

      await page.getByRole('button', { name: /Đặt hàng$/ }).click();
      await expect(page).toHaveURL(/\/orders\/[^/]+\/payment\?t=.+/);
      const paymentUrl = new URL(page.url());
      orderCode = paymentUrl.pathname.split('/')[2]!;
      accessToken = paymentUrl.searchParams.get('t')!;
      expect(orderCode).toMatch(/^PK/);

      // QR phải mang đúng số tiền khách đã thấy khi đặt hàng, tài khoản nhận và nội dung là mã đơn.
      const qrImage = page.getByRole('img', { name: `Mã QR chuyển khoản cho đơn ${orderCode}` });
      await expect(qrImage).toBeVisible();
      const qrUrl = new URL((await qrImage.getAttribute('src'))!);
      expect(qrUrl.origin + qrUrl.pathname).toBe('https://vietqr.app/img');
      expect(Object.fromEntries(qrUrl.searchParams)).toEqual({
        acc: sepayAccountNumber,
        bank: sepayBankCode,
        amount: String(checkoutTotal),
        des: orderCode,
      });
    });

    let emailLink = '';
    await test.step('Thư xác nhận trong outbox có mã đơn và link xem đơn', async () => {
      // Thư gửi ngay khi tạo đơn, không chờ tiền về.
      const confirmation = await readOrderConfirmation(guestEmail, orderCode);
      expect(confirmation.text).toContain(orderCode);
      const linkUrl = new URL(confirmation.link);
      expect(linkUrl.origin).toBe(e2eEnvironment.webOrigin);
      expect(linkUrl.pathname).toBe(`/orders/${orderCode}`);
      expect(linkUrl.searchParams.get('t')).toBeTruthy();
      emailLink = confirmation.link;
    });

    await test.step('Webhook báo đủ tiền thì trang chờ sang trang thành công trong 5 giây', async () => {
      // Lấy tổng từ API thanh toán. Số tiền webhook phải là số server tính, không phải số giao diện đã đọc.
      const paymentResponse = await adminPage.request.get(
        `/api/orders/${orderCode}/payment?t=${encodeURIComponent(accessToken)}`,
      );
      expect(paymentResponse.status(), await paymentResponse.text()).toBe(200);
      const paymentTotal = (await paymentResponse.json()).data.total as number;
      expect(paymentTotal).toBe(checkoutTotal);

      const webhook = await sendSepayWebhook(adminPage, {
        accountNumber: sepayAccountNumber,
        orderCode,
        amount: paymentTotal,
      });
      expect(webhook.status(), await webhook.text()).toBe(200);
      // Đồng hồ chạy từ lúc webhook trả 200, vì từ lúc đó tiền đã được ghi nhận.
      const webhookAcceptedAt = performance.now();

      await expect(page).toHaveURL(new RegExp(`/orders/${orderCode}/success$`), {
        timeout: redirectAfterWebhookLimitMs,
      });
      const redirectDelayMs = Math.round(performance.now() - webhookAcceptedAt);
      // Ghi số đo vào báo cáo của Playwright để người duyệt thấy khoảng cách tới giới hạn 5 giây.
      test.info().annotations.push({
        type: 'redirect-delay-ms',
        description: String(redirectDelayMs),
      });
      expect(redirectDelayMs).toBeLessThanOrEqual(redirectAfterWebhookLimitMs);

      await expect(page.getByText('Shop đã nhận tiền chuyển khoản.')).toBeVisible();
    });

    await test.step('Mở link trong thư ở context mới thì đơn đã thanh toán', async () => {
      // Context mới không có cookie hay router state của khách. Chỉ token trong link được dùng để xem đơn.
      const linkContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
      try {
        const linkPage = await linkContext.newPage();
        const linkIssues = collectConsoleIssues(linkPage);
        await stubProductImages(linkPage);
        await stubWebFonts(linkPage);
        // Mở đúng link lấy từ thư, không dựng lại link từ mã đơn.
        await linkPage.goto(emailLink);
        await dismissConsent(linkPage);

        await expect(
          linkPage.getByRole('heading', { level: 1, name: `Đơn hàng #${orderCode}` }),
        ).toBeVisible();
        const paymentStatusItem = linkPage
          .locator('.order-meta-item')
          .filter({ hasText: 'Trạng thái thanh toán' });
        await expect(paymentStatusItem.locator('.order-badge')).toHaveText('Đã thanh toán');
        await expect(linkPage.getByText('Chuyển khoản ngân hàng')).toBeVisible();

        expect(linkIssues).toEqual([]);
      } finally {
        await linkContext.close();
      }
    });

    expect(customerIssues).toEqual([]);
    expect(adminIssues).toEqual([]);
  });
});
