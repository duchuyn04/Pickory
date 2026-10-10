import { expect, test, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';

// Ảnh PNG 1x1 trả cho mọi yêu cầu ảnh sản phẩm, để trình duyệt không ghi lỗi tải ảnh vào console.
const tinyPng = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64',
);

// Thư mục lưu ảnh chụp màn hình khi reviewer cần xem. Không đặt biến này thì spec không ghi ảnh.
const screenshotDirectory = process.env.UI_C3_SHOTS_DIR;

const adminHeaders = { Origin: e2eEnvironment.webOrigin };
const COMPARE_STORAGE_KEY = 'pickory.compare';

async function loginAdmin(page: Page) {
  await page.goto('/admin/login');
  const consent = page.getByRole('button', { name: 'Chỉ cần thiết' });
  if (await consent.isVisible()) await consent.click();
  await page.getByLabel(/^Email/).fill(e2eEnvironment.adminEmail);
  await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.adminPassword);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin`);
}

// Ghi lỗi console và lỗi trang. Mỗi test phải kết thúc với 0 lỗi và 0 cảnh báo.
function collectConsoleIssues(page: Page): string[] {
  const issues: string[] = [];
  page.on('console', (message) => {
    if (message.location().url.endsWith('/favicon.ico')) return;
    if (message.type() === 'error' || message.type() === 'warning') {
      issues.push(`${message.type()}: ${message.text()}`);
    }
  });
  page.on('pageerror', (error) => issues.push(`pageerror: ${error.message}`));
  return issues;
}

// E2E không gọi Cloudinary thật: mọi ảnh sản phẩm là PNG 1x1.
async function stubProductImages(page: Page) {
  await page.route('https://res.cloudinary.com/**', (route) =>
    route.fulfill({ body: tinyPng, contentType: 'image/png' }),
  );
}

async function saveScreenshot(page: Page, fileName: string, fullPage = true) {
  if (!screenshotDirectory) return;
  await page.screenshot({ path: `${screenshotDirectory}/${fileName}`, fullPage });
}

// Đo tràn ngang của cả trang. Rộng hơn viewport nghĩa là người dùng phải cuộn ngang.
async function expectNoHorizontalPageOverflow(page: Page) {
  const widths = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(widths.scrollWidth).toBeLessThanOrEqual(widths.clientWidth);
}

async function dismissConsent(page: Page) {
  const consent = page.getByRole('button', { name: 'Chỉ cần thiết' });
  if (await consent.isVisible()) await consent.click();
}

interface SeededProduct {
  id: string;
  slug: string;
  name: string;
}

interface SeededCatalog {
  categorySlug: string;
  // Năm sản phẩm cùng danh mục, theo thứ tự P1 đến P5.
  products: SeededProduct[];
  otherCategoryProduct: SeededProduct;
}

interface ProductSeed {
  label: string;
  specs: Record<string, string | number | boolean>;
}

// Thuộc tính `screen` và `touch` giống nhau ở mọi sản phẩm. Thuộc tính `cpu` bỏ trống ở P3.
const PRODUCT_SEEDS: ProductSeed[] = [
  { label: 'P1', specs: { ram: '8', cpu: 'Core i5', screen: 14, touch: false } },
  { label: 'P2', specs: { ram: '16', cpu: 'Core i7', screen: 14, touch: false } },
  { label: 'P3', specs: { ram: '16', screen: 14, touch: false } },
  { label: 'P4', specs: { ram: '32', cpu: 'Core i9', screen: 14, touch: false } },
  { label: 'P5', specs: { ram: '8', cpu: 'Core i3', screen: 14, touch: false } },
];

async function createCategory(
  page: Page,
  name: string,
  attributes: unknown[],
): Promise<{ id: string; slug: string }> {
  const response = await page.request.post('/api/admin/categories', {
    headers: adminHeaders,
    data: { name, parentId: null, position: 0, attributes },
  });
  expect(response.status(), await response.text()).toBe(201);
  return (await response.json()).data;
}

async function createActiveProduct(
  page: Page,
  input: {
    categoryId: string;
    brandId: string;
    name: string;
    imagePublicId: string;
    specs: Record<string, string | number | boolean>;
  },
): Promise<SeededProduct> {
  const created = await page.request.post('/api/admin/products', {
    headers: adminHeaders,
    data: {
      categoryId: input.categoryId,
      brandId: input.brandId,
      name: input.name,
      description: { type: 'doc', content: [{ type: 'paragraph' }] },
      warrantyMonths: 12,
      specs: input.specs,
      options: [],
      variants: [
        {
          optionValues: {},
          price: 15_000_000,
          enabled: true,
          weightGrams: 1500,
          lengthCm: 35,
          widthCm: 25,
          heightCm: 3,
        },
      ],
      images: [{ publicId: input.imagePublicId, alt: `Ảnh ${input.name}` }],
    },
  });
  expect(created.status(), await created.text()).toBe(201);
  const product = (await created.json()).data as {
    id: string;
    slug: string;
    variants: { id: string }[];
  };
  const skuId = product.variants[0]?.id;
  const adjusted = await page.request.post(`/api/admin/skus/${skuId}/movements`, {
    headers: adminHeaders,
    data: { delta: 3, reason: 'Nhập kho E2E trang so sánh' },
  });
  expect(adjusted.status(), await adjusted.text()).toBe(201);
  const activated = await page.request.put(`/api/admin/products/${product.id}/status`, {
    headers: adminHeaders,
    data: { status: 'active' },
  });
  expect(activated.status(), await activated.text()).toBe(200);
  return { id: product.id, slug: product.slug, name: input.name };
}

// Tạo dữ liệu bằng API quản trị. Gồm danh mục có 4 thuộc tính ở 2 nhóm, 5 sản phẩm cùng danh mục và 1 sản phẩm khác danh mục.
async function seedCatalog(page: Page): Promise<SeededCatalog> {
  const suffix = Date.now().toString(36);
  const category = await createCategory(page, `Laptop C3 ${suffix}`, [
    {
      key: 'ram',
      label: 'RAM',
      group: 'Cấu hình',
      type: 'enum',
      unit: 'GB',
      options: ['8', '16', '32'],
      required: false,
      filterable: true,
    },
    {
      key: 'cpu',
      label: 'CPU',
      group: 'Cấu hình',
      type: 'text',
      required: false,
      filterable: false,
    },
    {
      key: 'screen',
      label: 'Màn hình',
      group: 'Hiển thị',
      type: 'number',
      unit: 'inch',
      required: false,
      filterable: true,
    },
    {
      key: 'touch',
      label: 'Cảm ứng',
      group: 'Hiển thị',
      type: 'boolean',
      required: false,
      filterable: true,
    },
  ]);
  const otherCategory = await createCategory(page, `Điện thoại C3 ${suffix}`, [
    {
      key: 'ram',
      label: 'RAM',
      group: 'Cấu hình',
      type: 'enum',
      unit: 'GB',
      options: ['8', '16', '32'],
      required: false,
      filterable: true,
    },
  ]);

  const brand = await page.request.post('/api/admin/brands', {
    headers: adminHeaders,
    data: { name: `Hãng C3 ${suffix}` },
  });
  expect(brand.status()).toBe(201);
  const brandId = (await brand.json()).data.id as string;

  const products: SeededProduct[] = [];
  for (const seed of PRODUCT_SEEDS) {
    products.push(
      await createActiveProduct(page, {
        categoryId: category.id,
        brandId,
        name: `Laptop ${seed.label} C3 ${suffix}`,
        imagePublicId: `pickory/products/e2e-c3-${suffix}-${seed.label.toLowerCase()}`,
        specs: seed.specs,
      }),
    );
  }
  const otherCategoryProduct = await createActiveProduct(page, {
    categoryId: otherCategory.id,
    brandId,
    name: `Điện thoại C3 ${suffix}`,
    imagePublicId: `pickory/products/e2e-c3-${suffix}-phone`,
    specs: { ram: '8' },
  });
  return { categorySlug: category.slug, products, otherCategoryProduct };
}

// Thêm sản phẩm vào danh sách so sánh bằng nút trên trang sản phẩm.
async function addToCompare(page: Page, slug: string) {
  await page.goto(`/p/${slug}`);
  await dismissConsent(page);
  await page.getByRole('button', { name: 'Thêm vào so sánh' }).click();
  await expect(page.getByRole('button', { name: 'Bỏ khỏi so sánh' })).toBeVisible();
}

test.describe('UI-C3: trang so sánh của khách', () => {
  let catalog: SeededCatalog;

  test.beforeAll(async ({ browser }) => {
    // Tạo dữ liệu một lần cho cả nhóm test, vì mỗi sản phẩm cần nhiều lệnh gọi API.
    test.setTimeout(180000);
    const context = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    const page = await context.newPage();
    await loginAdmin(page);
    catalog = await seedCatalog(page);
    await context.close();
  });

  test('thêm sản phẩm thứ 5 hoặc khác danh mục thì hiện thông báo và không thêm (tiêu chí 1)', async ({
    page,
  }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });

    for (const product of catalog.products.slice(0, 4)) await addToCompare(page, product.slug);

    // Sản phẩm thứ 5: nút tắt, có thông báo đọc được bằng trình đọc màn hình, danh sách vẫn 4.
    await page.goto(`/p/${catalog.products[4]?.slug}`);
    await expect(page.getByRole('button', { name: 'Thêm vào so sánh' })).toBeDisabled();
    await expect(page.getByRole('status').filter({ hasText: 'Đã đủ 4 sản phẩm' })).toBeVisible();
    await expect(page.locator('.compare-count')).toHaveText('Danh sách so sánh: 4/4');
    await saveScreenshot(page, 'compare-1280-fifth-product-notice.png');

    // Sản phẩm khác danh mục: nút tắt, có thông báo, danh sách không đổi.
    await page.goto(`/p/${catalog.otherCategoryProduct.slug}`);
    await expect(page.getByRole('button', { name: 'Thêm vào so sánh' })).toBeDisabled();
    await expect(
      page.getByRole('status').filter({ hasText: 'sản phẩm của danh mục khác' }),
    ).toBeVisible();
    await expect(page.locator('.compare-count')).toHaveText('Danh sách so sánh: 4/4');

    // Danh sách trong storage vẫn đúng 4 id ban đầu.
    const stored = await page.evaluate(
      (key) => JSON.parse(window.localStorage.getItem(key) ?? '{}') as { productIds: string[] },
      COMPARE_STORAGE_KEY,
    );
    expect(stored.productIds).toEqual(catalog.products.slice(0, 4).map((product) => product.id));
    expect(issues).toEqual([]);
  });

  test('dòng khác nhau có nhãn chữ và màu nền, công tắc ẩn dòng giống nhau (tiêu chí 2)', async ({
    page,
  }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });

    for (const product of catalog.products.slice(0, 4)) await addToCompare(page, product.slug);
    // Vào trang so sánh bằng link của trang sản phẩm, đúng lối khách đi.
    await page.getByRole('link', { name: 'Xem so sánh' }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/compare`);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('So sánh sản phẩm');

    // Cột đầu là cột tên thông số, bốn cột sau là bốn sản phẩm theo thứ tự đã thêm.
    const headers = page.locator('thead th[scope="col"]');
    await expect(headers).toHaveCount(5);
    for (const [index, product] of catalog.products.slice(0, 4).entries()) {
      await expect(
        headers.nth(index + 1).getByRole('link', { name: product.name }),
      ).toHaveAttribute('href', `/p/${product.slug}`);
    }

    const ramRow = page.locator('tbody tr', { has: page.getByRole('rowheader', { name: /^RAM/ }) });
    const screenRow = page.locator('tbody tr', {
      has: page.getByRole('rowheader', { name: /^Màn hình/ }),
    });
    const cpuRow = page.locator('tbody tr', { has: page.getByRole('rowheader', { name: /^CPU/ }) });

    // Chữ: dòng khác nhau có nhãn, dòng giống nhau không có.
    await expect(ramRow.getByText('Khác nhau')).toBeVisible();
    await expect(cpuRow.getByText('Khác nhau')).toBeVisible();
    await expect(screenRow.getByText('Khác nhau')).toHaveCount(0);
    // Giá trị chưa nhập hiện chữ thay vì ô trống.
    await expect(cpuRow.getByText('Chưa có thông tin')).toHaveCount(1);

    // Màu: nền ô của dòng khác nhau phải khác nền ô của dòng giống nhau.
    const backgroundOf = (row: typeof ramRow) =>
      row
        .locator('td')
        .first()
        .evaluate((cell) => getComputedStyle(cell).backgroundColor);
    const differentBackground = await backgroundOf(ramRow);
    const sameBackground = await backgroundOf(screenRow);
    expect(differentBackground).not.toBe(sameBackground);
    await saveScreenshot(page, 'compare-1280-four-products.png');

    // Công tắc: dòng giống nhau biến mất, dòng khác nhau còn.
    await page.getByLabel('Chỉ hiện điểm khác nhau').check();
    await expect(screenRow).toHaveCount(0);
    await expect(ramRow).toHaveCount(1);
    await saveScreenshot(page, 'compare-1280-only-differences.png');

    // Bỏ một sản phẩm: cột biến mất và danh sách đổi.
    await page.getByRole('button', { name: 'Bỏ khỏi so sánh' }).first().click();
    await expect(headers).toHaveCount(4);
    const stored = await page.evaluate(
      (key) => JSON.parse(window.localStorage.getItem(key) ?? '{}') as { productIds: string[] },
      COMPARE_STORAGE_KEY,
    );
    expect(stored.productIds).toEqual(catalog.products.slice(1, 4).map((product) => product.id));
    expect(issues).toEqual([]);
  });

  test('storage không dùng được thì trang vẫn hoạt động trong phiên (tiêu chí 3)', async ({
    page,
  }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    // Chỉ chặn khóa so sánh, để các phần khác của trang không bị ảnh hưởng.
    await page.addInitScript((blockedKey) => {
      const originalGetItem = Storage.prototype.getItem;
      const originalSetItem = Storage.prototype.setItem;
      Storage.prototype.getItem = function (key: string) {
        if (key === blockedKey) throw new Error('storage bị chặn');
        return originalGetItem.call(this, key);
      };
      Storage.prototype.setItem = function (key: string, value: string) {
        if (key === blockedKey) throw new Error('storage bị chặn');
        originalSetItem.call(this, key, value);
      };
    }, COMPARE_STORAGE_KEY);

    // Đi qua danh mục bằng điều hướng trong ứng dụng, không tải lại trang, để kho bộ nhớ giữ danh sách.
    await page.goto(`/c/${catalog.categorySlug}`);
    await dismissConsent(page);
    for (const product of catalog.products.slice(0, 2)) {
      await page.getByRole('link', { name: new RegExp(product.name) }).click();
      await expect(page.getByRole('heading', { level: 1 })).toHaveText(product.name);
      await page.getByRole('button', { name: 'Thêm vào so sánh' }).click();
      await expect(page.getByRole('button', { name: 'Bỏ khỏi so sánh' })).toBeVisible();
      await page.goBack();
    }

    await page.getByRole('link', { name: new RegExp(catalog.products[0]?.name ?? '') }).click();
    await expect(page.locator('.compare-count')).toHaveText('Danh sách so sánh: 2/4');
    await page.getByRole('link', { name: 'Xem so sánh' }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/compare`);
    await expect(page.locator('thead th[scope="col"]')).toHaveCount(3);
    expect(issues).toEqual([]);
  });

  test('danh sách rỗng hiện lời nhắn và không gọi API so sánh', async ({ page }) => {
    const issues = collectConsoleIssues(page);
    const compareRequests: string[] = [];
    page.on('request', (request) => {
      if (request.url().includes('/api/products/compare')) compareRequests.push(request.url());
    });
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/compare');
    await dismissConsent(page);

    await expect(page.getByText('Chưa có sản phẩm nào để so sánh')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Về trang chủ' })).toBeVisible();
    await saveScreenshot(page, 'compare-1280-empty.png');
    await page.setViewportSize({ width: 375, height: 812 });
    await saveScreenshot(page, 'compare-375-empty-viewport.png', false);
    expect(compareRequests).toEqual([]);
    expect(issues).toEqual([]);
  });

  test('id mà API không trả bị bỏ khỏi danh sách kèm thông báo', async ({ page }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    const realProducts = catalog.products.slice(0, 2);
    const listWithUnknownId = {
      categoryId: 'danh-muc-bat-ky',
      productIds: [realProducts[0]?.id, 'san-pham-khong-con-hien-thi', realProducts[1]?.id],
    };
    await page.addInitScript(({ key, value }) => window.localStorage.setItem(key, value), {
      key: COMPARE_STORAGE_KEY,
      value: JSON.stringify(listWithUnknownId),
    });
    await page.goto('/compare');
    await dismissConsent(page);

    await expect(
      page.getByRole('status').filter({
        hasText: '1 sản phẩm không còn hiển thị đã được bỏ khỏi danh sách.',
      }),
    ).toBeVisible();
    await expect(page.locator('thead th[scope="col"]')).toHaveCount(3);
    const stored = await page.evaluate(
      (key) => JSON.parse(window.localStorage.getItem(key) ?? '{}') as { productIds: string[] },
      COMPARE_STORAGE_KEY,
    );
    expect(stored.productIds).toEqual(realProducts.map((product) => product.id));
    expect(issues).toEqual([]);
  });

  test('không cuộn ngang ở viewport 375 px, bảng cuộn trong khung riêng', async ({ page }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 375, height: 812 });
    for (const product of catalog.products.slice(0, 4)) await addToCompare(page, product.slug);
    await page.goto('/compare');
    await expect(page.locator('thead th[scope="col"]')).toHaveCount(5);

    await expectNoHorizontalPageOverflow(page);
    // Bảng rộng hơn khung, nên khung cuộn ngang được và trang thì không.
    const wrapWidths = await page.locator('.compare-wrap').evaluate((element) => ({
      scrollWidth: element.scrollWidth,
      clientWidth: element.clientWidth,
    }));
    expect(wrapWidths.scrollWidth).toBeGreaterThan(wrapWidths.clientWidth);
    await saveScreenshot(page, 'compare-375-four-products-viewport.png', false);
    await saveScreenshot(page, 'compare-375-four-products-full.png');

    await page.getByLabel('Chỉ hiện điểm khác nhau').check();
    await expectNoHorizontalPageOverflow(page);
    await saveScreenshot(page, 'compare-375-only-differences-viewport.png', false);
    expect(issues).toEqual([]);
  });
});
