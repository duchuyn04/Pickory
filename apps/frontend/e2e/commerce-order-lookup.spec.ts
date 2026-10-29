import { expect, test, type BrowserContext, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';
import { readOrderConfirmation } from './outbox';

// Ảnh PNG 1x1 trả cho mọi yêu cầu ảnh sản phẩm để tránh lỗi tải tài nguyên vào console.
const tinyPng = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64',
);

// Ảnh chụp để người duyệt xem lại. Thư mục nằm ngoài repo nên không lẫn vào Git.
const screenshotDirectory =
  process.env.COM_UI11_SHOTS_DIR ??
  'C:/Users/juven/AppData/Local/Temp/claude/C--Users-juven-Desktop-pickory/224dfa1c-b08d-4682-93a6-90d8bec3a32e/scratchpad';

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
  // Đo chiều rộng tài liệu so với chiều rộng cửa sổ để phát hiện lỗi tràn ngang.
  const widths = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    innerWidth: window.innerWidth,
  }));
  expect(widths.scrollWidth).toBeLessThanOrEqual(widths.innerWidth);
}

// Kiểm tràn ngang rồi chụp ảnh `COM-UI11-<trạng thái>-<độ rộng>.png` ở cả 1280px và 375px.
async function checkLayoutAtBothWidths(page: Page, stateName: string) {
  for (const width of [1280, 375]) {
    await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
    await expectNoHorizontalOverflow(page);
    // Cuộn về đầu trang trước khi chụp để thanh điều hướng không che nội dung.
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({
      path: `${screenshotDirectory}/COM-UI11-${stateName}-${width}.png`,
      fullPage: true,
    });
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
          stock: 100,
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

const guestContactEmail = 'khach@example.com';
const lookupRateLimitMessage = 'Bạn đã tra cứu quá nhiều lần. Vui lòng thử lại sau 15 phút.';

// Khách vãng lai đặt một đơn COD và trả về mã đơn. Dữ liệu liên hệ lấy từ `fillContact`.
async function placeGuestCodOrder(
  page: Page,
  product: SeededProduct,
  address: AddressChoice,
): Promise<string> {
  await page.goto(`/p/${product.slug}`);
  await dismissConsent(page);
  await page.getByRole('button', { name: 'Thêm vào giỏ' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Đã thêm vào giỏ' })).toBeVisible();

  await page.getByRole('button', { name: /^Giỏ hàng, / }).click();
  await page.getByRole('link', { name: 'Xem giỏ hàng' }).click();
  await page.getByRole('link', { name: 'Tiến hành đặt hàng' }).click();

  await fillAddress(page, address);
  await fillContact(page);
  await expect(page.getByLabel('Thanh toán khi nhận hàng (COD)')).toBeChecked();
  await page.getByRole('button', { name: /Đặt hàng$/ }).click();
  await expect(page).toHaveURL(/\/orders\/[^/]+\/success$/);
  return (await page.locator('.order-success-code').innerText()).trim();
}

test.describe('COM-UI11: trang tra cứu đơn', () => {
  let product: SeededProduct;
  let address: AddressChoice;
  let adminContext: BrowserContext;

  test.beforeAll(async ({ browser }) => {
    test.setTimeout(180000);
    adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    const adminPage = await adminContext.newPage();
    await loginAdmin(adminPage);
    product = await seedCatalog(adminPage);
    const picked = await pickAddress(adminPage);
    address = picked.choice;
    await configureShipping(adminPage, picked.provinceCode, picked.wardCode);
  });

  test.afterAll(async () => {
    await adminContext.close();
  });

  // Đây là request tra cứu thật duy nhất của cả suite. Giới hạn tần suất của API tính theo IP 127.0.0.1.
  test('khách tra cứu bằng mã đơn viết thường, mở đơn ngay trên UI, link cũ hết hiệu lực', async ({
    page,
    browser,
  }) => {
    test.setTimeout(120000);
    await stubProductImages(page);
    const orderCode = await placeGuestCodOrder(page, product, address);
    // Đọc thư xác nhận trước khi tra cứu. Tra cứu đổi token nên link trong thư này sẽ hết hiệu lực.
    const confirmation = await readOrderConfirmation(guestContactEmail, orderCode);

    // Ngữ cảnh mới không có cookie, mô phỏng khách mở trang trên thiết bị khác.
    const lookupContext = await browser.newContext({
      baseURL: e2eEnvironment.webOrigin,
      viewport: { width: 1280, height: 800 },
    });
    const lookupPage = await lookupContext.newPage();
    const issues = collectConsoleIssues(lookupPage, [
      { urlPattern: /\/api\/orders\//, status: 404 },
    ]);
    await stubProductImages(lookupPage);

    await lookupPage.goto('/order-lookup');
    await dismissConsent(lookupPage);
    await expect(
      lookupPage.getByRole('heading', { level: 1, name: 'Tra cứu đơn hàng' }),
    ).toBeVisible();
    await checkLayoutAtBothWidths(lookupPage, 'form');

    // Nhập mã đơn viết thường và email có khoảng trắng hai đầu. Trang phải chuẩn hóa trước khi gửi.
    await lookupPage.getByLabel('Mã đơn hàng').fill(orderCode.toLowerCase());
    await lookupPage.getByLabel('Email đặt hàng').fill(`  ${guestContactEmail}  `);
    await lookupPage.getByRole('button', { name: 'Xem đơn hàng' }).click();

    // Khớp thông tin thì trang chuyển ngay tới chi tiết đơn, không chờ email.
    await expect(lookupPage).toHaveURL(new RegExp(`/orders/${orderCode}\\?t=.+`));
    await expect(
      lookupPage.getByRole('heading', { level: 1, name: `Đơn hàng #${orderCode}` }),
    ).toBeVisible();
    await checkLayoutAtBothWidths(lookupPage, 'detail');

    // Link cũ trong thư xác nhận không còn token hợp lệ nên trang báo không tìm thấy đơn.
    await lookupPage.goto(confirmation.link);
    await expect(
      lookupPage.getByRole('heading', { level: 1, name: 'Không tìm thấy đơn hàng' }),
    ).toBeVisible();

    expect(issues).toEqual([]);
    await lookupContext.close();
  });

  test('429 hiện câu thử lại sau và mở lại nút gửi', async ({
    page,
  }) => {
    const issues = collectConsoleIssues(page, [
      { urlPattern: /\/api\/order-lookups/, status: 429 },
    ]);
    // Giữ phản hồi lại tới khi test cho phép. Nhờ vậy test kiểm được nút gửi bị khóa lúc request đang chạy.
    let releaseResponse: () => void = () => {};
    const responseGate = new Promise<void>((resolve) => {
      releaseResponse = resolve;
    });
    // Giả phản hồi 429 để không tốn lượt thật trong giới hạn tần suất của API.
    await page.route('**/api/order-lookups', async (route) => {
      await responseGate;
      await route.fulfill({
        status: 429,
        contentType: 'application/json',
        body: JSON.stringify({ error: { code: 'RATE_LIMITED', message: 'Quá nhiều yêu cầu.' } }),
      });
    });

    await page.goto('/order-lookup');
    await dismissConsent(page);
    await page.getByLabel('Mã đơn hàng').fill('PKAB23CD45');
    await page.getByLabel('Email đặt hàng').fill(guestContactEmail);
    // Chữ trên nút đổi khi đang gửi, nên chọn nút theo loại thay vì theo tên.
    const submitButton = page.locator('button[type="submit"]');
    await submitButton.click();

    // Request chưa có phản hồi: nút phải bị khóa để khách không gửi hai lần.
    await expect(submitButton).toBeDisabled();
    releaseResponse();

    await expect(page.getByRole('alert').filter({ hasText: lookupRateLimitMessage })).toBeVisible();
    await expect(submitButton).toBeEnabled();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/order-lookup`);
    expect(issues).toEqual([]);
  });

  test('link Tra cứu đơn ở header và ở trang Đơn hàng của tôi dẫn tới trang tra cứu', async ({
    page,
  }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 375, height: 812 });

    await page.goto('/');
    await dismissConsent(page);
    // Header có thêm link không được làm trang tràn ngang ở 375px.
    await expectNoHorizontalOverflow(page);
    // Ở màn hẹp, mở ngăn menu trước khi bấm liên kết điều hướng.
    const menuButton = page.getByRole('button', { name: 'Menu', exact: true });
    if (await menuButton.isVisible()) {
      await menuButton.click();
    }
    await page
      .getByRole('navigation', { name: 'Điều hướng chính' })
      .getByRole('link', { name: 'Tra cứu đơn' })
      .click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/order-lookup`);
    await expect(page.getByRole('heading', { level: 1, name: 'Tra cứu đơn hàng' })).toBeVisible();

    // Khách chưa đăng nhập thấy lời mời đăng nhập và đường tra cứu thay vì bị chuyển về trang đăng nhập.
    await page.goto('/me/orders');
    await expect(page.getByText('Đăng nhập để xem các đơn hàng của bạn.')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Đăng nhập', exact: true }).last()).toBeVisible();
    await page.getByRole('link', { name: 'Tra cứu đơn bằng mã đơn' }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/order-lookup`);

    expect(issues).toEqual([]);
  });
});
