import { expect, test, type BrowserContext, type Page, type TestInfo } from '@playwright/test';
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
  stubProductImages,
} from './commerce-order-fixtures';
import { e2eEnvironment } from './environment';
import { readOrderConfirmation } from './outbox';

const adminHeaders = { Origin: e2eEnvironment.webOrigin };
const FALLBACK_FEE_API = '/api/admin/shipping/fallback-fee';
const GHN_FAILURE_API = '/api/e2e/ghn/failures/calculate-fee';

// Phí dự phòng không phụ thuộc cân nặng: khối lượng gốc lớn nhất và giá bước 0.
// Cả ba tuyến cùng một phí, nên test không phụ thuộc cách chọn địa chỉ.
const FALLBACK_FEE = 33_000;

// Bảng phí dự phòng đang bật với đủ chín giá.
const enabledFallbackTable = {
  enabled: true,
  sameProvinceBaseFee: FALLBACK_FEE,
  sameProvinceBaseWeightGrams: 20_000,
  sameProvinceStepFee: 0,
  sameRegionBaseFee: FALLBACK_FEE,
  sameRegionBaseWeightGrams: 20_000,
  sameRegionStepFee: 0,
  crossRegionBaseFee: FALLBACK_FEE,
  crossRegionBaseWeightGrams: 20_000,
  crossRegionStepFee: 0,
};

// Bảng phí dự phòng tắt và trống, là trạng thái mà các spec khác mong đợi.
const disabledFallbackTable = {
  enabled: false,
  sameProvinceBaseFee: null,
  sameProvinceBaseWeightGrams: null,
  sameProvinceStepFee: null,
  sameRegionBaseFee: null,
  sameRegionBaseWeightGrams: null,
  sameRegionStepFee: null,
  crossRegionBaseFee: null,
  crossRegionBaseWeightGrams: null,
  crossRegionStepFee: null,
};

async function saveFallbackTable(
  page: Page,
  table: typeof enabledFallbackTable | typeof disabledFallbackTable,
) {
  const response = await page.request.put(FALLBACK_FEE_API, { headers: adminHeaders, data: table });
  expect(response.status(), await response.text()).toBe(200);
}

// Khách không được thấy chữ "dự phòng" ở bất kỳ trang nào của luồng đặt hàng.
async function expectNoFallbackWording(page: Page) {
  const pageText = (await page.locator('body').innerText()).toLowerCase();
  expect(pageText).not.toContain('dự phòng');
  expect(pageText).not.toContain('fallback');
}

test.describe('COM-27: đơn dùng phí vận chuyển dự phòng', () => {
  let product: SeededProduct;
  let address: AddressChoice;
  let adminContext: BrowserContext;
  let adminPage: Page;

  test.beforeAll(async ({ browser }) => {
    test.setTimeout(180000);
    adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    adminPage = await adminContext.newPage();
    await stubProductImages(adminPage);
    await loginAdmin(adminPage);
    // Giá riêng để khóa phí đã lưu của đơn này không trùng đơn của spec khác. Nhờ vậy nguồn phí chắc chắn là phí dự phòng.
    product = await seedProduct(adminPage, 451_000 + (Date.now() % 997));
    address = await prepareShipping(adminPage);
  });

  test.afterAll(async () => {
    try {
      // Tắt lỗi báo phí của GHN giả và đưa bảng phí dự phòng về tắt, kể cả khi test dừng giữa chừng.
      // Playwright chạy một worker, nên spec sau dùng chung API và không được thấy GHN giả đang lỗi.
      const failureOff = await adminPage.request.delete(GHN_FAILURE_API, { headers: adminHeaders });
      expect(failureOff.status()).toBe(204);
    } finally {
      try {
        await saveFallbackTable(adminPage, disabledFallbackTable);
      } finally {
        await adminContext?.close();
      }
    }
  });

  test('GHN không phản hồi lúc báo phí: khách đặt COD bình thường, admin thấy nhãn Phí ship dự phòng', async ({
    page,
    browser,
  }, testInfo: TestInfo) => {
    test.setTimeout(180000);
    const guestEmail = `khach-phi-${Date.now().toString(36)}@example.com`;
    const customerIssues = collectConsoleIssues(page);
    await stubProductImages(page);
    let orderCode = '';

    await test.step('Admin bật phí dự phòng và đặt GHN giả không phản hồi lúc báo phí', async () => {
      await saveFallbackTable(adminPage, enabledFallbackTable);
      const failureOn = await adminPage.request.put(GHN_FAILURE_API, { headers: adminHeaders });
      expect(failureOn.status(), await failureOn.text()).toBe(204);
    });

    await test.step('Khách thấy phí vận chuyển như bình thường ở trang đặt hàng, không có chữ dự phòng', async () => {
      await addToCartAndOpenCheckout(page, product);
      await chooseOption(page, 'checkout-address-province', address.provinceName);
      await chooseOption(page, 'checkout-address-ward', address.wardName);
      await page.locator('#checkout-address-line').fill('27 Đường Số Bảy');
      await page.getByLabel('Họ và tên', { exact: true }).fill('Phạm Thị Dung');
      await page.getByLabel('Số điện thoại', { exact: true }).fill('0912345678');
      await page.getByLabel('Email', { exact: true }).fill(guestEmail);
      await expect(page.getByLabel('Thanh toán khi nhận hàng (COD)')).toBeChecked();

      // Phí hiện ra bằng phí của bảng, và trang không có thông báo lỗi shipping.
      const feeRow = checkoutRowFor(page)('Phí vận chuyển');
      await expect(feeRow).toBeVisible();
      expect(await readAmount(feeRow)).toBe(FALLBACK_FEE);
      await expect(page.getByRole('alert')).toHaveCount(0);
      await expectNoFallbackWording(page);
    });

    await test.step('Khách đặt đơn COD thành công và trang thành công không có chữ dự phòng', async () => {
      await page.getByRole('button', { name: /Đặt hàng$/ }).click();
      await expect(page).toHaveURL(/\/orders\/[^/]+\/success$/);
      await expect(
        page.getByRole('heading', { level: 1, name: 'Đặt hàng thành công' }),
      ).toBeVisible();
      orderCode = (await page.locator('.order-success-code').innerText()).trim();
      expect(orderCode).toMatch(/^PK/);
      await expectNoFallbackWording(page);
    });

    await test.step('Trang xem đơn của khách (link trong thư) không có chữ dự phòng', async () => {
      const confirmation = await readOrderConfirmation(guestEmail, orderCode);
      // Context mới không có cookie của khách. Chỉ token trong link được dùng để xem đơn.
      const linkContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
      try {
        const linkPage = await linkContext.newPage();
        const linkIssues = collectConsoleIssues(linkPage);
        await stubProductImages(linkPage);
        await linkPage.goto(confirmation.link);
        await dismissConsent(linkPage);
        await expect(
          linkPage.getByRole('heading', { level: 1, name: `Đơn hàng #${orderCode}` }),
        ).toBeVisible();
        await expectNoFallbackWording(linkPage);
        expect(linkIssues).toEqual([]);
      } finally {
        await linkContext.close();
      }
    });

    await test.step('Admin mở trang đơn và thấy nhãn Phí ship dự phòng cạnh phí vận chuyển', async () => {
      // Phí đã lưu không thể có, vì giá sản phẩm riêng. Nguồn phí là bảng dự phòng.
      for (const width of [1280, 375]) {
        await adminPage.setViewportSize({ width, height: width === 375 ? 812 : 800 });
        await adminPage.goto(`/admin/orders/${orderCode}`);
        await expect(
          adminPage.getByRole('heading', { level: 1, name: `Đơn hàng ${orderCode}` }),
        ).toBeVisible();

        const paymentCard = adminPage.locator('section.card').filter({
          has: adminPage.getByRole('heading', { name: 'Thanh toán', exact: true }),
        });
        const feeLine = paymentCard
          .locator('div')
          .filter({ hasText: /^Phí vận chuyển/ })
          .first();
        await expect(feeLine.getByText('Phí ship dự phòng', { exact: true })).toBeVisible();
        await expect(feeLine.getByText('Theo bảng phí dự phòng', { exact: true })).toBeVisible();
        await expectNoHorizontalOverflow(adminPage);
        // Cuộn lên đầu để thanh menu cố định trên màn hẹp không đè lên tiêu đề trong ảnh chụp cả trang.
        await adminPage.evaluate(() => window.scrollTo(0, 0));
        await adminPage.screenshot({
          path: testInfo.outputPath(`admin-order-fallback-${width}.png`),
          fullPage: true,
        });
      }
    });

    expect(customerIssues).toEqual([]);
  });
});
