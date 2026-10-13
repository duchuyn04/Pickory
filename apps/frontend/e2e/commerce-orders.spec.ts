import { expect, test, type BrowserContext, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';

// Ảnh PNG 1x1 trả cho mọi yêu cầu ảnh sản phẩm để tránh lỗi tải tài nguyên vào console.
const tinyPng = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64',
);

const screenshotDirectory =
  process.env.COM_UI3_SHOTS_DIR ??
  'C:/Users/juven/AppData/Local/Temp/claude/C--Users-juven-Desktop-pickory/c0628d6e-6470-493e-8673-a94b5488add7/scratchpad/com-ui3-shots';

const adminHeaders = { Origin: e2eEnvironment.webOrigin };

interface SeededProduct {
  name: string;
  slug: string;
  skuId: string;
  price: number;
}

interface AddressChoice {
  provinceName: string;
  wardName: string;
}

interface AllowedLoadFailure {
  urlPattern: RegExp;
  status: number;
}

function collectConsoleIssues(page: Page, allowedFailures: AllowedLoadFailure[] = []): string[] {
  const issues: string[] = [];
  page.on('console', (message) => {
    // Trình duyệt Chromium có thể tự động tải favicon dù ứng dụng chưa khai báo.
    if (message.location().url.endsWith('/favicon.ico')) return;
    const isAllowedFailure = allowedFailures.some(
      (failure) =>
        failure.urlPattern.test(message.location().url) &&
        message.text().includes(`status of ${failure.status}`),
    );
    if (message.type() === 'error' && isAllowedFailure) return;
    if (message.type() === 'error' || message.type() === 'warning') {
      issues.push(`${message.type()}: ${message.text()} [url: ${message.location().url}]`);
    }
  });
  page.on('pageerror', (error) => issues.push(`pageerror: ${error.message}`));
  return issues;
}

async function stubProductImages(page: Page) {
  // Thay thế toàn bộ yêu cầu tải ảnh Cloudinary bằng ảnh giả 1x1.
  await page.route('https://res.cloudinary.com/**', (route) =>
    route.fulfill({ body: tinyPng, contentType: 'image/png' }),
  );
}

async function dismissConsent(page: Page) {
  const consent = page.getByRole('button', { name: 'Chỉ cần thiết' });
  if (await consent.isVisible()) await consent.click();
}

async function expectNoHorizontalOverflow(page: Page) {
  // Đo chiều rộng tài liệu so với khung nhìn để phát hiện lỗi tràn ngang.
  const widths = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(widths.scrollWidth).toBeLessThanOrEqual(widths.clientWidth);
}

async function capture(page: Page, name: string) {
  // Cuộn về đầu trang trước khi chụp để thanh điều hướng không che nội dung.
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: `${screenshotDirectory}/${name}.png`, fullPage: true });
}

async function checkLayoutAtBothWidths(page: Page, shotName: string) {
  // Kiểm tra tràn ngang và chụp ảnh ở cả màn hình máy tính 1280px và di động 375px.
  for (const width of [1280, 375]) {
    await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
    await expectNoHorizontalOverflow(page);
    await capture(page, `${shotName}-${width}`);
  }
  await page.setViewportSize({ width: 1280, height: 800 });
}

async function loginAdmin(page: Page) {
  await page.goto('/admin/login');
  await dismissConsent(page);
  await page.getByLabel(/^Email/).fill(e2eEnvironment.adminEmail);
  await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.adminPassword);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin`);
}

async function seedCatalog(page: Page): Promise<SeededProduct> {
  const suffix = Date.now().toString(36);
  const category = await page.request.post('/api/admin/categories', {
    headers: adminHeaders,
    data: { name: `Đơn hàng ${suffix}`, parentId: null, position: 0, attributes: [] },
  });
  expect(category.status()).toBe(201);
  const categoryId = (await category.json()).data.id as string;

  const brand = await page.request.post('/api/admin/brands', {
    headers: adminHeaders,
    data: { name: `Hãng đơn ${suffix}` },
  });
  expect(brand.status()).toBe(201);
  const brandId = (await brand.json()).data.id as string;

  const created = await page.request.post('/api/admin/products', {
    headers: adminHeaders,
    data: {
      categoryId,
      brandId,
      name: `Bàn phím cơ ${suffix}`,
      description: { type: 'doc', content: [{ type: 'paragraph' }] },
      warrantyMonths: 12,
      specs: {},
      options: [],
      variants: [
        {
          optionValues: {},
          price: 1_500_000,
          compareAtPrice: null,
          enabled: true,
          weightGrams: 500,
          lengthCm: 20,
          widthCm: 10,
          heightCm: 5,
        },
      ],
      images: [{ publicId: `pickory/products/e2e-orders-${suffix}`, alt: 'Bàn phím cơ' }],
    },
  });
  expect(created.status()).toBe(201);
  const productData = (await created.json()).data as {
    id: string;
    slug: string;
    variants: { id: string }[];
  };
  const skuId = productData.variants[0]!.id;

  // Nhập tồn kho để sản phẩm có sẵn cho khách đặt hàng.
  await page.request.post(`/api/admin/skus/${skuId}/movements`, {
    headers: adminHeaders,
    data: { delta: 100, reason: 'Nhập kho E2E đơn hàng' },
  });

  // Kích hoạt sản phẩm để khách hàng nhìn thấy trên trang mua sắm.
  await page.request.put(`/api/admin/products/${productData.id}/status`, {
    headers: adminHeaders,
    data: { status: 'active' },
  });

  return { name: `Bàn phím cơ ${suffix}`, slug: productData.slug, skuId, price: 1_500_000 };
}

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
  // Cấu hình kho gửi hàng cho cửa hàng phục vụ tính phí vận chuyển.
  await page.request.put('/api/admin/shipping/settings', {
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
}

async function chooseOption(page: Page, inputId: string, name: string) {
  const input = page.locator(`#${inputId}`);
  await expect(input).toBeEnabled();
  await input.fill(name);
  await page.getByRole('option', { name, exact: true }).click();
  await expect(input).toHaveValue(name);
}

async function fillAddress(page: Page, address: AddressChoice) {
  await chooseOption(page, 'checkout-address-province', address.provinceName);
  await chooseOption(page, 'checkout-address-ward', address.wardName);
  await page.locator('#checkout-address-line').fill('15 Đường Đơn Hàng');
}

async function fillContact(page: Page) {
  await page.getByLabel('Họ và tên', { exact: true }).fill('Nguyễn Văn Khách');
  await page.getByLabel('Số điện thoại', { exact: true }).fill('0912345678');
  await page.getByLabel('Email', { exact: true }).fill('khach@example.com');
}

test.describe('COM-UI3: theo dõi đơn hàng và khách tự hủy', () => {
  let product: SeededProduct;
  let address: AddressChoice;
  let adminContext: BrowserContext;
  let adminPage: Page;

  test.beforeAll(async ({ browser }) => {
    test.setTimeout(180000);
    adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    adminPage = await adminContext.newPage();
    await loginAdmin(adminPage);
    product = await seedCatalog(adminPage);
    const picked = await pickAddress(adminPage);
    address = picked.choice;
    await configureShipping(adminPage, picked.provinceCode, picked.wardCode);
  });

  test.afterAll(async () => {
    await adminContext.close();
  });

  test('khách vãng lai đặt đơn, xem chi tiết qua token, và tự hủy đơn hàng thành công', async ({
    page,
    browser,
  }) => {
    test.setTimeout(120000);
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });

    // Khách vãng lai thêm sản phẩm vào giỏ.
    await page.goto(`/p/${product.slug}`);
    await dismissConsent(page);
    await page.getByRole('button', { name: 'Thêm vào giỏ' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Đã thêm vào giỏ' })).toBeVisible();

    // Đi tới trang đặt hàng và hoàn tất thông tin đơn COD.
    await page.getByRole('button', { name: /^Giỏ hàng, / }).click();
    await page.getByRole('link', { name: 'Xem giỏ hàng' }).click();
    await page.getByRole('link', { name: 'Tiến hành đặt hàng' }).click();

    await fillAddress(page, address);
    await fillContact(page);
    await expect(page.getByLabel('Thanh toán khi nhận hàng (COD)')).toBeChecked();
    await page.getByRole('button', { name: /Đặt hàng$/ }).click();

    // 1. Kiểm tra trang đặt hàng thành công và chụp ảnh.
    await expect(page).toHaveURL(/\/orders\/[^/]+\/success$/);
    await expect(
      page.getByRole('heading', { level: 1, name: 'Đặt hàng thành công' }),
    ).toBeVisible();
    await checkLayoutAtBothWidths(page, 'order-success');

    const viewOrderLink = page.getByRole('link', { name: 'Xem đơn hàng' });
    await expect(viewOrderLink).toBeVisible();
    const targetUrl = await viewOrderLink.getAttribute('href');
    expect(targetUrl).toContain('?t=');

    // Bấm xem đơn hàng chuyển tới trang chi tiết đơn hàng.
    await viewOrderLink.click();
    await expect(page).toHaveURL(/\/orders\/[^/?]+\?t=.+$/);

    // 2. Kiểm tra thông tin chi tiết đơn hàng và chụp ảnh.
    await expect(page.getByRole('heading', { level: 1, name: /^Đơn hàng #PK/ })).toBeVisible();
    await expect(page.locator('.order-badge').filter({ hasText: 'Chờ xác nhận' })).toBeVisible();
    await expect(page.getByText('Chưa thanh toán')).toBeVisible();
    await expect(page.getByText(product.name)).toBeVisible();
    await checkLayoutAtBothWidths(page, 'order-detail');

    const currentUrl = page.url();
    const orderCodeMatch = currentUrl.match(/\/orders\/(PK[^/?]+)/);
    const orderCode = orderCodeMatch ? orderCodeMatch[1]! : '';
    expect(orderCode.length).toBeGreaterThan(0);

    // 3. Mở liên kết có token trong context mới không cookie thì xem được đơn.
    const guestContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    const guestPage = await guestContext.newPage();
    const guestIssues = collectConsoleIssues(guestPage);
    await stubProductImages(guestPage);
    await guestPage.goto(currentUrl);
    await dismissConsent(guestPage);
    await expect(
      guestPage.getByRole('heading', { level: 1, name: `Đơn hàng #${orderCode}` }),
    ).toBeVisible();
    // Token chỉ thuộc liên kết xem đơn, không được lưu trong trình duyệt.
    const orderToken = new URL(currentUrl).searchParams.get('t')!;
    const storageContainsToken = await guestPage.evaluate((token) => {
      return [localStorage, sessionStorage].some((storage) =>
        Object.entries(storage).some(
          ([key, value]) => key.includes(token) || value.includes(token),
        ),
      );
    }, orderToken);
    expect(storageContainsToken).toBe(false);
    expect((await guestContext.cookies()).some((cookie) => cookie.value.includes(orderToken))).toBe(
      false,
    );
    expect(guestIssues).toEqual([]);
    await guestContext.close();

    // 4. Mở cùng mã đơn trong context mới mà không có token thì nhận lỗi 404.
    const forbiddenContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    const forbiddenPage = await forbiddenContext.newPage();
    const forbiddenIssues = collectConsoleIssues(forbiddenPage, [
      { urlPattern: /\/api\/orders\//, status: 404 },
    ]);
    await stubProductImages(forbiddenPage);
    await forbiddenPage.goto(`/orders/${orderCode}`);
    await dismissConsent(forbiddenPage);
    await expect(
      forbiddenPage.getByRole('heading', { level: 1, name: 'Không tìm thấy đơn hàng.' }),
    ).toBeVisible();
    expect(forbiddenIssues).toEqual([]);
    await forbiddenContext.close();

    // 5. Khách tự hủy đơn: mở hộp thoại, Giữ đơn không đổi, Hủy đơn thành công.
    const cancelButton = page.getByRole('button', { name: 'Hủy đơn hàng' });
    await expect(cancelButton).toBeVisible();
    await cancelButton.click();

    // Kiểm tra hộp thoại xác nhận mở ra và chụp ảnh.
    const dialog = page.getByRole('dialog', { name: 'Xác nhận hủy đơn hàng' });
    await expect(dialog).toBeVisible();
    await checkLayoutAtBothWidths(page, 'order-cancel-dialog');

    // Bấm Giữ đơn để đóng hộp thoại mà không hủy.
    await page.getByRole('button', { name: 'Giữ đơn' }).click();
    await expect(dialog).toBeHidden();
    await expect(page.locator('.order-badge').filter({ hasText: 'Chờ xác nhận' })).toBeVisible();

    // Mở lại hộp thoại và bấm Hủy đơn.
    await cancelButton.click();
    await expect(dialog).toBeVisible();
    await page.getByRole('button', { name: 'Hủy đơn', exact: true }).click();

    // Trạng thái đơn đổi sang Đã hủy, nút hủy biến mất và chụp ảnh kết quả.
    await expect(page.getByRole('status').filter({ hasText: 'Đã hủy đơn hàng.' })).toBeVisible();
    await expect(page.locator('.order-badge').filter({ hasText: 'Đã hủy' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Hủy đơn hàng' })).toBeHidden();
    await checkLayoutAtBothWidths(page, 'order-detail-cancelled');

    expect(issues).toEqual([]);
  });

  test('khách đăng nhập đặt đơn, xem danh sách đơn trong /me/orders và mở chi tiết không cần token', async ({
    page,
  }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    test.setTimeout(120000);

    // Dùng khách đã seed riêng cho đơn hàng, không tiêu hao hạn mức đăng ký của identity.
    const session = await page.request.post('/api/auth/sessions', {
      headers: { Origin: e2eEnvironment.webOrigin },
      data: {
        email: e2eEnvironment.ordersCustomerEmail,
        password: e2eEnvironment.accountPassword,
      },
    });
    expect(session.status()).toBe(201);

    // Thêm sản phẩm vào giỏ và đặt hàng.
    await page.goto(`/p/${product.slug}`);
    await page.getByRole('button', { name: 'Thêm vào giỏ' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Đã thêm vào giỏ' })).toBeVisible();

    await page.getByRole('button', { name: /^Giỏ hàng, / }).click();
    await page.getByRole('link', { name: 'Xem giỏ hàng' }).click();
    await page.getByRole('link', { name: 'Tiến hành đặt hàng' }).click();

    await fillAddress(page, address);
    await page.getByRole('button', { name: /Đặt hàng$/ }).click();
    await expect(page).toHaveURL(/\/orders\/[^/]+\/success$/);

    const orderCode = (await page.locator('.order-success-code').innerText()).trim();

    // Mở trang danh sách đơn hàng của tôi và kiểm tra giao diện.
    await page.goto('/me/orders');
    await expect(page.getByRole('heading', { level: 1, name: 'Đơn hàng của tôi' })).toBeVisible();
    await expect(page.getByText(`Đơn hàng #${orderCode}`)).toBeVisible();
    await checkLayoutAtBothWidths(page, 'customer-orders');

    // Bấm vào mã đơn hàng mở chi tiết mà không cần token truy cập.
    await page.getByRole('link', { name: `Đơn hàng #${orderCode}` }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/orders/${orderCode}`);
    await expect(
      page.getByRole('heading', { level: 1, name: `Đơn hàng #${orderCode}` }),
    ).toBeVisible();
    await expect(page.getByText(product.name)).toBeVisible();

    expect(issues).toEqual([]);
  });
});
