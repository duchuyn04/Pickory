import { expect, test, type BrowserContext, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';
import { readOrderConfirmation } from './outbox';

// Ảnh PNG 1x1 trả cho mọi yêu cầu ảnh sản phẩm, để trình duyệt không ghi lỗi tải ảnh vào console.
const tinyPng = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64',
);

// Thư mục ảnh chụp để người duyệt xem lại. Ngoài repo nên không lẫn vào Git.
const screenshotDirectory =
  process.env.COM_UI2_SHOTS_DIR ??
  'C:/Users/juven/AppData/Local/Temp/claude/C--Users-juven-Desktop-pickory/c0628d6e-6470-493e-8673-a94b5488add7/scratchpad/com-ui2-shots';

const adminHeaders = { Origin: e2eEnvironment.webOrigin };

// Cấu hình bán hàng nền. Mỗi test ghi đè đúng giá trị nó cần, nên test không phụ thuộc thứ tự chạy.
const baselineCommerceSettings = {
  freeShippingThreshold: 50_000_000,
  codMaxAmount: 30_000_000,
  maxQuantityPerSku: 5,
  maxCartLines: 20,
  sepayAccountNumber: null,
  sepayBankCode: null,
};

interface CommerceSettings {
  freeShippingThreshold: number;
  codMaxAmount: number;
  maxQuantityPerSku: number;
  maxCartLines: number;
  sepayAccountNumber: string | null;
  sepayBankCode: string | null;
}

interface SeededProduct {
  name: string;
  slug: string;
  skuId: string;
  price: number;
}

interface SeededCatalog {
  laptop: SeededProduct;
  mouse: SeededProduct;
}

interface AddressChoice {
  provinceName: string;
  wardName: string;
}

async function loginAdmin(page: Page) {
  await page.goto('/admin/login');
  const consent = page.getByRole('button', { name: 'Chỉ cần thiết', exact: true });
  if (await consent.isVisible()) await consent.click();
  await page.getByLabel(/^Email/).fill(e2eEnvironment.adminEmail);
  await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.adminPassword);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin`);
}

// Một lỗi tải mà test chủ động gây ra: trình duyệt ghi dòng "Failed to load resource" cho phản hồi đó.
interface AllowedLoadFailure {
  urlPattern: RegExp;
  status: number;
}

const previewUrl = /\/api\/checkout\/previews$/;

// Ghi lỗi console và lỗi trang, để mỗi test kết thúc với 0 lỗi và 0 cảnh báo.
// Chỉ lỗi tải có URL và mã trạng thái nằm trong `allowedFailures` được bỏ qua.
function collectConsoleIssues(page: Page, allowedFailures: AllowedLoadFailure[] = []): string[] {
  const issues: string[] = [];
  page.on('console', (message) => {
    // Chromium có giao diện tự gọi `favicon.ico`, còn ứng dụng chưa có favicon.
    if (message.location().url.endsWith('/favicon.ico')) return;
    const isAllowedFailure = allowedFailures.some(
      (failure) =>
        failure.urlPattern.test(message.location().url) &&
        message.text().includes(`status of ${failure.status}`),
    );
    if (message.type() === 'error' && isAllowedFailure) return;
    if (message.type() === 'error' || message.type() === 'warning') {
      issues.push(`${message.type()}: ${message.text()}`);
    }
  });
  page.on('pageerror', (error) => issues.push(`pageerror: ${error.message}`));
  return issues;
}

async function stubProductImages(page: Page) {
  await page.route('https://res.cloudinary.com/**', (route) =>
    route.fulfill({ body: tinyPng, contentType: 'image/png' }),
  );
}

async function dismissConsent(page: Page) {
  const consent = page.getByRole('button', { name: 'Chỉ cần thiết' });
  if (await consent.isVisible()) await consent.click();
}

async function expectNoHorizontalOverflow(page: Page) {
  const widths = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(widths.scrollWidth).toBeLessThanOrEqual(widths.clientWidth);
}

async function capture(page: Page, name: string) {
  // Cuộn lên đầu để thanh menu cố định trên màn hẹp không đè lên tiêu đề trong ảnh chụp cả trang.
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: `${screenshotDirectory}/${name}.png`, fullPage: true });
}

// Chụp và đo tràn ngang ở cả hai độ rộng, rồi trả viewport về 1280 px cho bước sau.
async function checkLayoutAtBothWidths(page: Page, shotName: string) {
  for (const width of [1280, 375]) {
    await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
    await expectNoHorizontalOverflow(page);
    await capture(page, `${shotName}-${width}`);
  }
  await page.setViewportSize({ width: 1280, height: 800 });
}

async function createProduct(
  page: Page,
  input: { categoryId: string; brandId: string; name: string; price: number; stock: number },
  suffix: string,
  imageKey: string,
): Promise<SeededProduct> {
  const created = await page.request.post('/api/admin/products', {
    headers: adminHeaders,
    data: {
      categoryId: input.categoryId,
      brandId: input.brandId,
      name: input.name,
      description: { type: 'doc', content: [{ type: 'paragraph' }] },
      warrantyMonths: 12,
      specs: {},
      options: [],
      variants: [
        {
          optionValues: {},
          price: input.price,
          compareAtPrice: null,
          enabled: true,
          weightGrams: 500,
          lengthCm: 20,
          widthCm: 10,
          heightCm: 5,
        },
      ],
      images: [
        { publicId: `pickory/products/e2e-checkout-${suffix}-${imageKey}`, alt: input.name },
      ],
    },
  });
  expect(created.status(), await created.text()).toBe(201);
  const product = (await created.json()).data as {
    id: string;
    slug: string;
    variants: { id: string }[];
  };
  const skuId = product.variants[0]!.id;

  const stocked = await page.request.post(`/api/admin/skus/${skuId}/movements`, {
    headers: adminHeaders,
    data: { delta: input.stock, reason: 'Nhập kho E2E đặt hàng' },
  });
  expect(stocked.status(), await stocked.text()).toBe(201);

  // Sản phẩm đi qua `active` mới hiện cho khách.
  const published = await page.request.put(`/api/admin/products/${product.id}/status`, {
    headers: adminHeaders,
    data: { status: 'active' },
  });
  expect(published.status(), await published.text()).toBe(200);

  return { name: input.name, slug: product.slug, skuId, price: input.price };
}

async function seedCatalog(page: Page): Promise<SeededCatalog> {
  const suffix = Date.now().toString(36);
  const category = await page.request.post('/api/admin/categories', {
    headers: adminHeaders,
    data: { name: `Đặt hàng ${suffix}`, parentId: null, position: 0, attributes: [] },
  });
  expect(category.status(), await category.text()).toBe(201);
  const categoryId = (await category.json()).data.id as string;

  const brand = await page.request.post('/api/admin/brands', {
    headers: adminHeaders,
    data: { name: `Hãng đặt ${suffix}` },
  });
  expect(brand.status()).toBe(201);
  const brandId = (await brand.json()).data.id as string;

  const base = { categoryId, brandId };
  return {
    laptop: await createProduct(
      page,
      { ...base, name: `Laptop Đặt ${suffix}`, price: 20_000_000, stock: 100 },
      suffix,
      'laptop',
    ),
    mouse: await createProduct(
      page,
      { ...base, name: `Chuột Đặt ${suffix}`, price: 500_000, stock: 100 },
      suffix,
      'mouse',
    ),
  };
}

// Lấy tỉnh đầu tiên và phường đầu tiên của tỉnh đó từ danh mục công khai.
// Spec không phụ thuộc dữ liệu cố định của danh mục.
async function pickAddress(
  page: Page,
): Promise<{ choice: AddressChoice; provinceCode: string; wardCode: string }> {
  const provincesResponse = await page.request.get('/api/addresses/provinces');
  const provinces = (await provincesResponse.json()).data as { code: string; name: string }[];
  const province = provinces[0]!;
  const wardsResponse = await page.request.get(
    `/api/addresses/wards?provinceCode=${encodeURIComponent(province.code)}`,
  );
  const ward = ((await wardsResponse.json()).data as { code: string; name: string }[])[0]!;
  return {
    choice: { provinceName: province.name, wardName: ward.name },
    provinceCode: province.code,
    wardCode: ward.code,
  };
}

async function configureShipping(page: Page, provinceCode: string, wardCode: string) {
  const saved = await page.request.put('/api/admin/shipping/settings', {
    headers: adminHeaders,
    data: {
      senderName: 'Kho Pickory E2E',
      senderPhone: '0912345678',
      provinceCode,
      wardCode,
      line: '12 Phố Kho',
      defaultServiceTypeId: 2,
    },
  });
  expect(saved.status(), await saved.text()).toBe(200);
}

async function applyCommerceSettings(
  page: Page,
  overrides: Partial<typeof baselineCommerceSettings>,
) {
  const saved = await page.request.put('/api/admin/commerce/settings', {
    headers: adminHeaders,
    data: { ...baselineCommerceSettings, ...overrides },
  });
  expect(saved.status(), await saved.text()).toBe(200);
}

// Đếm số lần giữ hàng của SKU. Mỗi đơn tạo đúng một dòng `order_reserve` cho mỗi SKU.
async function countOrderReservations(adminPage: Page, skuId: string): Promise<number> {
  const response = await adminPage.request.get(`/api/admin/skus/${skuId}/movements?limit=100`);
  expect(response.status(), await response.text()).toBe(200);
  const movements = (await response.json()).data.items as { type: string }[];
  return movements.filter((movement) => movement.type === 'order_reserve').length;
}

async function addToCartFromProductPage(page: Page, product: SeededProduct) {
  await page.goto(`/p/${product.slug}`);
  await dismissConsent(page);
  await page.getByRole('button', { name: 'Thêm vào giỏ' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Đã thêm vào giỏ' })).toBeVisible();
}

// Đi từ mini cart sang trang giỏ rồi bấm "Tiến hành đặt hàng", như khách thật.
async function openCheckoutFromCart(page: Page) {
  await page.getByRole('button', { name: /^Giỏ hàng, / }).click();
  await page.getByRole('link', { name: 'Xem giỏ hàng' }).click();
  await expect(page).toHaveURL(/\/cart$/);
  await page.getByRole('link', { name: 'Tiến hành đặt hàng' }).click();
  await expect(page).toHaveURL(/\/checkout$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Đặt hàng' })).toBeVisible();
}

async function chooseOption(page: Page, inputId: string, name: string) {
  const input = page.locator(`#${inputId}`);
  await expect(input).toBeEnabled();
  await input.fill(name);
  await page.getByRole('option', { name, exact: true }).click();
  await expect(input).toHaveValue(name);
}

async function fillAddress(page: Page, address: AddressChoice, line = '25 Đường Test') {
  await chooseOption(page, 'checkout-address-province', address.provinceName);
  await chooseOption(page, 'checkout-address-ward', address.wardName);
  await page.locator('#checkout-address-line').fill(line);
}

async function fillContact(page: Page) {
  await page.getByLabel('Họ và tên', { exact: true }).fill('Nguyễn Văn An');
  await page.getByLabel('Số điện thoại', { exact: true }).fill('0912345678');
  await page.getByLabel('Email', { exact: true }).fill('an@example.com');
}

const summaryOf = (page: Page) => page.getByRole('complementary', { name: 'Tóm tắt đơn hàng' });
const placeOrderButton = (page: Page) =>
  page.getByRole('button', { name: /Đặt hàng$|Đang đặt hàng/ });

test.describe('COM-UI2: trang đặt hàng của khách', () => {
  let catalog: SeededCatalog;
  let address: AddressChoice;
  let adminContext: BrowserContext;
  let adminPage: Page;
  let previousCommerceSettings: CommerceSettings;

  test.beforeAll(async ({ browser }) => {
    // Mỗi sản phẩm cần nhiều lệnh API, nên tạo một lần cho cả nhóm.
    test.setTimeout(180000);
    adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    adminPage = await adminContext.newPage();
    await loginAdmin(adminPage);
    // Giữ cấu hình trước khi test ghi đè để các spec khác không nhận giá trị thử nghiệm.
    const settingsResponse = await adminPage.request.get('/api/admin/commerce/settings');
    expect(settingsResponse.status(), await settingsResponse.text()).toBe(200);
    previousCommerceSettings = (await settingsResponse.json()).data as CommerceSettings;
    catalog = await seedCatalog(adminPage);

    const picked = await pickAddress(adminPage);
    address = picked.choice;
    await configureShipping(adminPage, picked.provinceCode, picked.wardCode);
  });

  test.afterAll(async () => {
    try {
      // Trả đúng cấu hình ban đầu kể cả khi một test dừng sớm, không thay bằng ngưỡng riêng của checkout.
      if (previousCommerceSettings) {
        const restored = await adminPage.request.put('/api/admin/commerce/settings', {
          headers: adminHeaders,
          data: {
            freeShippingThreshold: previousCommerceSettings.freeShippingThreshold,
            codMaxAmount: previousCommerceSettings.codMaxAmount,
            maxQuantityPerSku: previousCommerceSettings.maxQuantityPerSku,
            maxCartLines: previousCommerceSettings.maxCartLines,
            sepayAccountNumber: previousCommerceSettings.sepayAccountNumber,
            sepayBankCode: previousCommerceSettings.sepayBankCode,
          },
        });
        expect(restored.status(), await restored.text()).toBe(200);
      }
    } finally {
      await adminContext?.close();
    }
  });

  test.beforeEach(async () => {
    await applyCommerceSettings(adminPage, {});
  });

  test('khách vãng lai thấy phí ship, đặt đơn COD và tới trang thành công có mã đơn', async ({
    page,
  }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    await addToCartFromProductPage(page, catalog.laptop);
    await openCheckoutFromCart(page);

    // Chưa có địa chỉ: tóm tắt hiện tạm tính của giỏ và tắt nút đặt hàng.
    await expect(summaryOf(page)).toContainText('20.000.000 đ');
    await expect(summaryOf(page)).toContainText('Chọn địa chỉ để tính phí vận chuyển');
    await expect(placeOrderButton(page)).toBeDisabled();
    await checkLayoutAtBothWidths(page, 'checkout-empty');

    await fillAddress(page, address);
    await expect(summaryOf(page)).toContainText('Phí vận chuyển');
    await expect(summaryOf(page)).toContainText('Tổng tiền');
    await expect(placeOrderButton(page)).toBeEnabled();
    await fillContact(page);
    await page.getByLabel(/^Ghi chú cho đơn hàng/).fill('Giao giờ hành chính');
    await expect(page.getByLabel('Thanh toán khi nhận hàng (COD)')).toBeChecked();
    await checkLayoutAtBothWidths(page, 'checkout-preview');

    await placeOrderButton(page).click();

    await expect(page).toHaveURL(/\/orders\/[^/]+\/success$/);
    await expect(
      page.getByRole('heading', { level: 1, name: 'Đặt hàng thành công' }),
    ).toBeVisible();
    const orderCode = await page.locator('.order-success-code').innerText();
    expect(orderCode.trim().length).toBeGreaterThan(0);
    // Token đi qua state của router, nên đường dẫn của trang không chứa token.
    expect(page.url()).not.toContain('t=');
    await expect(page.getByRole('link', { name: 'Xem đơn hàng' })).toHaveAttribute(
      'href',
      new RegExp(`^/orders/${orderCode}\\?t=.+`),
    );
    // Giỏ đã được xóa sau khi đặt đơn.
    await expect(page.getByRole('button', { name: 'Giỏ hàng, trống' })).toBeVisible();

    // Thư xác nhận nằm trong outbox dev. Liên kết trong thư phải trùng nút "Xem đơn hàng" của trang thành công.
    const confirmation = await readOrderConfirmation('an@example.com', orderCode.trim());
    const emailUrl = new URL(confirmation.link);
    expect(emailUrl.origin).toBe(e2eEnvironment.webOrigin);
    expect(emailUrl.pathname).toBe(`/orders/${orderCode.trim()}`);
    expect(emailUrl.searchParams.get('t')).toBeTruthy();
    const viewOrderHref = await page
      .getByRole('link', { name: 'Xem đơn hàng' })
      .getAttribute('href');
    expect(`${emailUrl.pathname}${emailUrl.search}`).toBe(viewOrderHref);
    await checkLayoutAtBothWidths(page, 'checkout-success');

    expect(issues).toEqual([]);
  });

  test('bấm Đặt hàng hai lần nhanh chỉ gửi một request và chỉ tạo một đơn', async ({ page }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    await addToCartFromProductPage(page, catalog.laptop);
    await openCheckoutFromCart(page);
    await fillAddress(page, address);
    await fillContact(page);
    await expect(placeOrderButton(page)).toBeEnabled();

    const orderRequests: string[] = [];
    page.on('request', (request) => {
      if (request.method() === 'POST' && new URL(request.url()).pathname === '/api/orders') {
        orderRequests.push(request.headers()['idempotency-key'] ?? '');
      }
    });
    const reservationsBefore = await countOrderReservations(adminPage, catalog.laptop.skuId);

    await placeOrderButton(page).dblclick();

    await expect(page).toHaveURL(/\/orders\/[^/]+\/success$/);
    expect(orderRequests).toHaveLength(1);
    expect(orderRequests[0]).toMatch(/^[A-Za-z0-9_-]{16,100}$/);
    // Mỗi đơn giữ hàng đúng một lần cho SKU. Hai đơn sẽ có hai dòng giữ hàng mới.
    const reservationsAfter = await countOrderReservations(adminPage, catalog.laptop.skuId);
    expect(reservationsAfter - reservationsBefore).toBe(1);

    expect(issues).toEqual([]);
  });

  test('tạm tính trên ngưỡng thì khách trả phí ship bằng 0 và thấy phần miễn phí', async ({
    page,
  }) => {
    const issues = collectConsoleIssues(page);
    await applyCommerceSettings(adminPage, { freeShippingThreshold: 400_000 });
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    await addToCartFromProductPage(page, catalog.mouse);
    await openCheckoutFromCart(page);
    await fillAddress(page, address);

    await expect(summaryOf(page)).toContainText('Miễn phí vận chuyển');
    await expect(summaryOf(page)).toContainText('-');
    // Tổng bằng tạm tính, vì phần miễn phí bù đúng phí vận chuyển.
    const totalRow = summaryOf(page).locator('.checkout-summary-total');
    await expect(totalRow).toContainText('500.000 đ');
    await checkLayoutAtBothWidths(page, 'checkout-free-shipping');

    expect(issues).toEqual([]);
  });

  test('đơn trên hạn mức COD thì COD tắt kèm lý do và trang chuyển sang chuyển khoản', async ({
    page,
  }) => {
    const issues = collectConsoleIssues(page, [{ urlPattern: previewUrl, status: 422 }]);
    await applyCommerceSettings(adminPage, { codMaxAmount: 1_000_000 });
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    await addToCartFromProductPage(page, catalog.laptop);
    await openCheckoutFromCart(page);
    await fillAddress(page, address);

    const cod = page.getByLabel('Thanh toán khi nhận hàng (COD)');
    await expect(cod).toBeDisabled();
    await expect(page.getByText('Đơn trên 1.000.000 đ chỉ thanh toán chuyển khoản')).toBeVisible();
    await expect(page.getByLabel('Chuyển khoản (VietQR)')).toBeChecked();
    await expect(placeOrderButton(page)).toBeEnabled();
    await checkLayoutAtBothWidths(page, 'checkout-cod-disabled');

    expect(issues).toEqual([]);
  });

  test('preview lỗi 503 hiện đúng câu, tắt nút đặt hàng và cho thử lại', async ({ page }) => {
    const issues = collectConsoleIssues(page, [{ urlPattern: previewUrl, status: 503 }]);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    await addToCartFromProductPage(page, catalog.laptop);
    await openCheckoutFromCart(page);

    // Chặn đúng route preview để ép lỗi shipping mà không đổi backend.
    const previewRoute = '**/api/checkout/previews';
    await page.route(previewRoute, (route) =>
      route.fulfill({
        status: 503,
        contentType: 'application/json',
        body: JSON.stringify({
          error: {
            code: 'SHIPPING_UNAVAILABLE',
            message: 'Đơn vị vận chuyển đang gián đoạn, vui lòng thử lại sau.',
          },
        }),
      }),
    );
    await fillAddress(page, address);
    await fillContact(page);

    await expect(summaryOf(page)).toContainText('Chưa tính được phí vận chuyển, thử lại');
    await expect(summaryOf(page)).not.toContainText('Tổng tiền');
    await expect(placeOrderButton(page)).toBeDisabled();
    await checkLayoutAtBothWidths(page, 'checkout-shipping-error');

    // Bỏ chặn rồi thử lại: phí hiện ra và nút đặt hàng dùng được.
    await page.unroute(previewRoute);
    await summaryOf(page).getByRole('button', { name: 'Thử lại' }).click();
    await expect(summaryOf(page)).toContainText('Tổng tiền');
    await expect(placeOrderButton(page)).toBeEnabled();

    expect(issues).toEqual([]);
  });

  test('lỗi Zod hiện dưới đúng ô, focus ô đầu tiên và không gửi đơn', async ({ page }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    await addToCartFromProductPage(page, catalog.laptop);
    await openCheckoutFromCart(page);
    await fillAddress(page, address);
    await expect(placeOrderButton(page)).toBeEnabled();

    const orderRequests: string[] = [];
    page.on('request', (request) => {
      if (request.method() === 'POST' && new URL(request.url()).pathname === '/api/orders') {
        orderRequests.push(request.url());
      }
    });

    await page.getByLabel('Số điện thoại', { exact: true }).fill('123');
    await page.getByRole('checkbox', { name: 'Xuất hóa đơn công ty' }).check();
    await page.getByLabel('Mã số thuế', { exact: true }).fill('12345');
    await placeOrderButton(page).click();

    await expect(page.locator('#checkout-contact-name-error')).toHaveText('Cần nhập họ và tên.');
    await expect(page.locator('#checkout-contact-phone-error')).toHaveText(
      'Số điện thoại không đúng định dạng.',
    );
    await expect(page.locator('#checkout-contact-email-error')).toHaveText('Cần nhập email.');
    await expect(page.locator('#checkout-invoice-company-name-error')).toHaveText(
      'Cần nhập tên công ty.',
    );
    await expect(page.locator('#checkout-invoice-tax-code-error')).toHaveText(
      'Mã số thuế gồm 10 hoặc 13 chữ số, có thể có dấu gạch ngang.',
    );
    await expect(page.locator('#checkout-invoice-address-error')).toHaveText(
      'Cần nhập địa chỉ hóa đơn.',
    );
    await expect(page.locator('#checkout-invoice-email-error')).toHaveText(
      'Cần nhập email nhận hóa đơn.',
    );
    await expect(page.getByLabel('Họ và tên', { exact: true })).toBeFocused();
    await expect(page.getByLabel('Họ và tên', { exact: true })).toHaveAttribute(
      'aria-invalid',
      'true',
    );
    await checkLayoutAtBothWidths(page, 'checkout-form-errors');
    expect(orderRequests).toHaveLength(0);

    expect(issues).toEqual([]);
  });

  test('đi hết form bằng bàn phím và đặt đơn bằng phím Enter', async ({ page }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    await addToCartFromProductPage(page, catalog.laptop);
    await openCheckoutFromCart(page);

    const nameInput = page.getByLabel('Họ và tên', { exact: true });
    await nameInput.focus();
    await page.keyboard.type('Trần Thị Bình');
    await page.keyboard.press('Tab');
    await expect(page.getByLabel('Số điện thoại', { exact: true })).toBeFocused();
    await page.keyboard.type('0987654321');
    await page.keyboard.press('Tab');
    await expect(page.getByLabel('Email', { exact: true })).toBeFocused();
    await page.keyboard.type('binh@example.com');

    // Tỉnh: gõ tên rồi Enter chọn dòng đầu của danh sách đã lọc.
    await page.keyboard.press('Tab');
    await expect(page.locator('#checkout-address-province')).toBeFocused();
    await page.keyboard.type(address.provinceName);
    await page.keyboard.press('Enter');
    await expect(page.locator('#checkout-address-province')).toHaveValue(address.provinceName);

    await page.keyboard.press('Tab');
    await expect(page.locator('#checkout-address-ward')).toBeFocused();
    await expect(page.locator('#checkout-address-ward')).toBeEnabled();
    await page.keyboard.type(address.wardName);
    await page.keyboard.press('Enter');
    await expect(page.locator('#checkout-address-ward')).toHaveValue(address.wardName);

    await page.keyboard.press('Tab');
    await expect(page.locator('#checkout-address-line')).toBeFocused();
    await page.keyboard.type('88 Đường Bàn Phím');

    await page.keyboard.press('Tab');
    await expect(page.getByLabel(/^Ghi chú cho đơn hàng/)).toBeFocused();
    await page.keyboard.type('Gọi trước khi giao');

    // Nhóm radio: phím mũi tên đổi lựa chọn, phím Tab rời nhóm.
    await page.keyboard.press('Tab');
    await expect(page.getByLabel('Thanh toán khi nhận hàng (COD)')).toBeFocused();
    await page.keyboard.press('ArrowDown');
    await expect(page.getByLabel('Chuyển khoản (VietQR)')).toBeChecked();
    await page.keyboard.press('ArrowUp');
    await expect(page.getByLabel('Thanh toán khi nhận hàng (COD)')).toBeChecked();

    await page.keyboard.press('Tab');
    await expect(page.getByRole('checkbox', { name: 'Xuất hóa đơn công ty' })).toBeFocused();
    await page.keyboard.press('Space');
    await expect(page.getByLabel('Tên công ty')).toBeVisible();
    await page.keyboard.press('Tab');
    await expect(page.getByLabel('Tên công ty')).toBeFocused();
    await page.keyboard.type('Công ty Bàn Phím');
    await page.keyboard.press('Tab');
    await expect(page.getByLabel('Mã số thuế', { exact: true })).toBeFocused();
    await page.keyboard.type('0312345678');
    await page.keyboard.press('Tab');
    await expect(page.getByLabel('Email nhận hóa đơn')).toBeFocused();
    await page.keyboard.type('ketoan@example.com');
    await page.keyboard.press('Tab');
    await expect(page.getByLabel('Địa chỉ công ty')).toBeFocused();
    await page.keyboard.type('1 Đường Công Ty');

    // Nút đặt hàng dùng được khi preview đã về, rồi Tab tới nút và Enter để gửi.
    await expect(placeOrderButton(page)).toBeEnabled();
    await page.keyboard.press('Tab');
    await expect(placeOrderButton(page)).toBeFocused();
    await page.keyboard.press('Enter');

    await expect(page).toHaveURL(/\/orders\/[^/]+\/success$/);
    expect(issues).toEqual([]);
  });

  test('GHN từ chối địa chỉ thì lỗi hiện dưới ô địa chỉ, khách sửa lại rồi đặt được đơn', async ({
    page,
  }) => {
    // Response 422 của `POST /api/orders` là lỗi test chủ động gây ra, nên chỉ request đó được phép.
    const issues = collectConsoleIssues(page, [{ urlPattern: /\/api\/orders$/, status: 422 }]);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    const reservationsBefore = await countOrderReservations(adminPage, catalog.mouse.skuId);
    await addToCartFromProductPage(page, catalog.mouse);
    await openCheckoutFromCart(page);

    // Dòng địa chỉ chứa chuỗi đánh dấu: GHN giả báo `To address conflict` cho dòng này.
    const rejectedLine = '25 Đường Test #ghn-tu-choi';
    await fillAddress(page, address, rejectedLine);
    await fillContact(page);
    await expect(placeOrderButton(page)).toBeEnabled();

    await placeOrderButton(page).click();

    // Câu lỗi nằm dưới ô dòng địa chỉ, ô đó nhận focus và được đánh dấu lỗi.
    const lineInput = page.locator('#checkout-address-line');
    await expect(page.locator('#checkout-address-line-error')).toHaveText(
      'Đơn vị vận chuyển không nhận địa chỉ này. Kiểm tra tên đường có thuộc phường/xã đã chọn không.',
    );
    await expect(lineInput).toHaveAttribute('aria-invalid', 'true');
    await expect(lineInput).toBeFocused();
    // Các ô khác giữ giá trị khách đã nhập, và trang không chuyển đi đâu.
    await expect(lineInput).toHaveValue(rejectedLine);
    await expect(page.locator('#checkout-address-province')).toHaveValue(address.provinceName);
    await expect(page.locator('#checkout-address-ward')).toHaveValue(address.wardName);
    await expect(page.getByLabel('Họ và tên', { exact: true })).toHaveValue('Nguyễn Văn An');
    await expect(page.getByLabel('Số điện thoại', { exact: true })).toHaveValue('0912345678');
    await expect(page.getByLabel('Email', { exact: true })).toHaveValue('an@example.com');
    await expect(page).toHaveURL(/\/checkout$/);
    // Đơn bị chặn trước khi giữ hàng, nên kho không có thêm dòng giữ hàng.
    expect(await countOrderReservations(adminPage, catalog.mouse.skuId)).toBe(reservationsBefore);
    await checkLayoutAtBothWidths(page, 'checkout-address-rejected');

    // Khách sửa dòng địa chỉ thành địa chỉ không có chuỗi đánh dấu rồi đặt lại.
    await lineInput.fill('25 Đường Test');
    await expect(placeOrderButton(page)).toBeEnabled();
    await placeOrderButton(page).click();

    await expect(page).toHaveURL(/\/orders\/[^/]+\/success$/);
    await expect(
      page.getByRole('heading', { level: 1, name: 'Đặt hàng thành công' }),
    ).toBeVisible();
    expect(await countOrderReservations(adminPage, catalog.mouse.skuId)).toBe(
      reservationsBefore + 1,
    );
    expect(issues).toEqual([]);
  });
});
