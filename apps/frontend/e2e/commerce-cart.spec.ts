import { expect, test, type Page } from '@playwright/test';
import { setSkuStock } from './catalog-admin-flow';
import { e2eEnvironment } from './environment';

// Ảnh PNG 1x1 trả cho mọi yêu cầu ảnh sản phẩm, để trình duyệt không ghi lỗi tải ảnh vào console.
const tinyPng = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64',
);

// Thư mục lưu ảnh chụp màn hình cho reviewer. Không đặt biến này thì spec không ghi ảnh.
const screenshotDirectory = process.env.COM_UI1_SHOTS_DIR;

const adminHeaders = { Origin: e2eEnvironment.webOrigin };

interface SeededProduct {
  productId: string;
  name: string;
  slug: string;
  skuId: string;
  price: number;
}

interface SeededCatalog {
  laptop: SeededProduct;
  mouse: SeededProduct;
  // Hai sản phẩm riêng cho test dòng không mua được, để thao tác tồn kho không ảnh hưởng test khác.
  outOfStock: SeededProduct;
  lowStock: SeededProduct;
}

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
    // Trình duyệt tự ghi dòng "Failed to load resource" cho phản hồi 4xx mà test chủ động gây ra.
    if (message.type() === 'error' && /status of 4\d\d/.test(message.text())) return;
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

async function saveScreenshot(page: Page, fileName: string) {
  if (!screenshotDirectory) return;
  await page.screenshot({ path: `${screenshotDirectory}/${fileName}`, fullPage: true });
}

async function expectNoHorizontalOverflow(page: Page) {
  const widths = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(widths.scrollWidth).toBeLessThanOrEqual(widths.clientWidth);
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
          stock: input.stock,
          compareAtPrice: null,
          enabled: true,
          weightGrams: 500,
          lengthCm: 20,
          widthCm: 10,
          heightCm: 5,
        },
      ],
      images: [{ publicId: `pickory/products/e2e-cart-${suffix}-${imageKey}`, alt: input.name }],
    },
  });
  expect(created.status(), await created.text()).toBe(201);
  const product = (await created.json()).data as {
    id: string;
    slug: string;
    variants: { id: string }[];
  };
  const skuId = product.variants[0]!.id;

  // Sản phẩm đi qua `active` mới hiện cho khách.
  const published = await page.request.put(`/api/admin/products/${product.id}/status`, {
    headers: adminHeaders,
    data: { status: 'active' },
  });
  expect(published.status(), await published.text()).toBe(200);

  return { productId: product.id, name: input.name, slug: product.slug, skuId, price: input.price };
}

async function seedCatalog(page: Page): Promise<SeededCatalog> {
  const suffix = Date.now().toString(36);
  const category = await page.request.post('/api/admin/categories', {
    headers: adminHeaders,
    data: { name: `Giỏ hàng ${suffix}`, parentId: null, position: 0, attributes: [] },
  });
  expect(category.status(), await category.text()).toBe(201);
  const categoryId = (await category.json()).data.id as string;

  const brand = await page.request.post('/api/admin/brands', {
    headers: adminHeaders,
    data: { name: `Hãng giỏ ${suffix}` },
  });
  expect(brand.status()).toBe(201);
  const brandId = (await brand.json()).data.id as string;

  const base = { categoryId, brandId };
  return {
    laptop: await createProduct(
      page,
      { ...base, name: `Laptop Giỏ ${suffix}`, price: 20_000_000, stock: 20 },
      suffix,
      'laptop',
    ),
    mouse: await createProduct(
      page,
      { ...base, name: `Chuột Giỏ ${suffix}`, price: 500_000, stock: 20 },
      suffix,
      'mouse',
    ),
    outOfStock: await createProduct(
      page,
      { ...base, name: `Bàn phím Giỏ ${suffix}`, price: 1_500_000, stock: 5 },
      suffix,
      'keyboard',
    ),
    lowStock: await createProduct(
      page,
      { ...base, name: `Tai nghe Giỏ ${suffix}`, price: 900_000, stock: 5 },
      suffix,
      'headset',
    ),
  };
}

async function addFromProductPage(page: Page, product: SeededProduct) {
  await page.goto(`/p/${product.slug}`);
  await dismissConsent(page);
  await page.getByRole('button', { name: 'Thêm vào giỏ' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Đã thêm vào giỏ' })).toBeVisible();
}

// Đi tới trang giỏ bằng liên kết trong panel, nên trang không tải lại và biến trên `window` còn nguyên.
async function openCartPageFromMiniCart(page: Page) {
  await page.getByRole('button', { name: /^Giỏ hàng, / }).click();
  await page.getByRole('link', { name: 'Xem giỏ hàng' }).click();
  await expect(page).toHaveURL(/\/cart$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Giỏ hàng' })).toBeVisible();
}

function lineOf(page: Page, productName: string) {
  return page.locator('li.cart-line').filter({ hasText: productName });
}

test.describe('COM-UI1: giỏ hàng của khách', () => {
  let catalog: SeededCatalog;

  test.beforeAll(async ({ browser }) => {
    // Mỗi sản phẩm cần nhiều lệnh API, nên tạo một lần cho cả nhóm.
    test.setTimeout(180000);
    const context = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    const page = await context.newPage();
    await loginAdmin(page);
    catalog = await seedCatalog(page);
    await context.close();
  });

  test('thêm từ trang sản phẩm: badge đổi, panel mini cart mở và đóng bằng bàn phím', async ({
    page,
  }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto(`/p/${catalog.laptop.slug}`);
    await dismissConsent(page);

    const toggleEmpty = page.getByRole('button', { name: 'Giỏ hàng, trống' });
    await expect(toggleEmpty).toBeVisible();
    // Chú thích "Sắp có" đã bỏ và nút dùng được.
    await expect(page.getByText('Sắp có')).toHaveCount(0);
    const addButton = page.getByRole('button', { name: 'Thêm vào giỏ' });
    await expect(addButton).toBeEnabled();
    await saveScreenshot(page, 'product-1280-add-button.png');

    await addButton.click();
    await expect(page.getByRole('status').filter({ hasText: 'Đã thêm vào giỏ' })).toBeVisible();
    const toggle = page.getByRole('button', { name: 'Giỏ hàng, 1 sản phẩm' });
    await expect(toggle).toBeVisible();
    await expect(page.locator('.mini-cart-badge')).toHaveText('1');

    // Bàn phím: focus nút giỏ, Enter mở panel, Esc đóng và trả focus về nút.
    await toggle.focus();
    await page.keyboard.press('Enter');
    const panel = page.getByRole('dialog', { name: 'Giỏ hàng' });
    await expect(panel).toBeVisible();
    await expect(panel).toContainText(catalog.laptop.name);
    await expect(panel).toContainText('20.000.000 đ');
    await expect(panel.getByRole('link', { name: 'Xem giỏ hàng' })).toBeVisible();
    await saveScreenshot(page, 'mini-cart-1280.png');

    await page.keyboard.press('Escape');
    await expect(panel).toBeHidden();
    await expect(toggle).toBeFocused();

    // Bấm ra ngoài cũng đóng panel.
    await toggle.click();
    await expect(panel).toBeVisible();
    await page.getByRole('heading', { level: 1 }).click();
    await expect(panel).toBeHidden();

    expect(issues).toEqual([]);
  });

  test('trang giỏ: đổi số lượng và xóa cập nhật tạm tính, không tải lại trang', async ({
    page,
  }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    await addFromProductPage(page, catalog.laptop);
    await page.goto(`/p/${catalog.mouse.slug}`);
    await page.getByRole('button', { name: 'Thêm vào giỏ' }).click();
    await expect(page.locator('.mini-cart-badge')).toHaveText('2');

    await openCartPageFromMiniCart(page);
    // Biến gắn vào `window` mất khi trang tải lại, nên còn nguyên nghĩa là không tải lại.
    await page.evaluate(() => {
      (window as unknown as { __cartNoReload: boolean }).__cartNoReload = true;
    });
    const laptopLine = lineOf(page, catalog.laptop.name);
    const summary = page.getByRole('complementary', { name: 'Tổng giỏ hàng' });
    await expect(summary).toContainText('20.500.000 đ');
    await expect(page.getByRole('link', { name: 'Tiến hành đặt hàng' })).toHaveAttribute(
      'href',
      '/checkout',
    );
    await saveScreenshot(page, 'cart-1280.png');

    // Nút tăng: 2 laptop, thành tiền 40.000.000, tạm tính 41.000.000 (cộng chuột 500.000).
    await laptopLine.getByRole('button', { name: /^Tăng số lượng/ }).click();
    await expect(laptopLine.getByLabel(/^Số lượng của/)).toHaveValue('2');
    await expect(laptopLine.locator('.cart-line-total')).toHaveText('40.000.000 đ');
    await expect(summary).toContainText('40.500.000 đ');
    await expect(page.locator('.mini-cart-badge')).toHaveText('2');

    // Gõ số rồi nhấn Enter: 3 laptop.
    const quantityInput = laptopLine.getByLabel(/^Số lượng của/);
    await quantityInput.fill('3');
    await quantityInput.press('Enter');
    await expect(laptopLine.locator('.cart-line-total')).toHaveText('60.000.000 đ');
    await expect(summary).toContainText('60.500.000 đ');

    // Số nhỏ hơn 1 không gửi: ô quay về 3.
    await quantityInput.fill('0');
    await quantityInput.press('Enter');
    await expect(quantityInput).toHaveValue('3');
    await expect(laptopLine.locator('.cart-line-total')).toHaveText('60.000.000 đ');

    // Xóa chuột: badge giảm còn 1 dòng, tạm tính chỉ còn laptop.
    await lineOf(page, catalog.mouse.name).getByRole('button', { name: /^Xóa/ }).click();
    await expect(lineOf(page, catalog.mouse.name)).toHaveCount(0);
    await expect(summary).toContainText('60.000.000 đ');
    await expect(page.locator('.mini-cart-badge')).toHaveText('1');

    const stillSamePage = await page.evaluate(
      () => (window as unknown as { __cartNoReload?: boolean }).__cartNoReload === true,
    );
    expect(stillSamePage).toBe(true);

    // Xóa dòng cuối: trạng thái trống có liên kết về trang chủ.
    await laptopLine.getByRole('button', { name: /^Xóa/ }).click();
    await expect(page.getByText('Giỏ hàng của bạn đang trống')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Tiếp tục mua sắm' })).toHaveAttribute('href', '/');
    await expect(page.getByRole('button', { name: 'Giỏ hàng, trống' })).toBeVisible();
    await saveScreenshot(page, 'cart-empty-1280.png');

    expect(issues).toEqual([]);
  });

  test('vượt giới hạn mỗi SKU hiện thông báo tiếng Việt cạnh dòng', async ({ page }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    await addFromProductPage(page, catalog.mouse);
    await openCartPageFromMiniCart(page);

    const line = lineOf(page, catalog.mouse.name);
    const quantityInput = line.getByLabel(/^Số lượng của/);
    await quantityInput.fill('5');
    await quantityInput.press('Enter');
    await expect(line.locator('.cart-line-total')).toHaveText('2.500.000 đ');

    // Số lượng 6 vượt giới hạn mặc định 5: thông báo của API hiện trong dòng, ô quay về 5.
    await line.getByRole('button', { name: /^Tăng số lượng/ }).click();
    await expect(line.getByRole('alert')).toContainText('tối đa 5');
    await expect(quantityInput).toHaveValue('5');
    await expect(line.locator('.cart-line-total')).toHaveText('2.500.000 đ');
    await saveScreenshot(page, 'cart-limit-1280.png');

    // Cùng dòng lỗi ở 375 px: không tràn ngang, thông báo vẫn nằm trong dòng.
    await page.setViewportSize({ width: 375, height: 812 });
    await expectNoHorizontalOverflow(page);
    await expect(line.getByRole('alert')).toBeVisible();
    await saveScreenshot(page, 'cart-limit-375.png');
    await page.setViewportSize({ width: 1280, height: 800 });

    // Thêm lần nữa từ trang sản phẩm cũng bị từ chối và hiện thông báo cạnh nút.
    await page.goto(`/p/${catalog.mouse.slug}`);
    await page.getByRole('button', { name: 'Thêm vào giỏ' }).click();
    await expect(page.getByRole('alert')).toContainText('tối đa 5');
    await expect(page.getByRole('status').filter({ hasText: 'Đã thêm vào giỏ' })).toHaveCount(0);

    expect(issues).toEqual([]);
  });

  test('dòng không mua được có nhãn lý do và nút đặt hàng bị tắt', async ({ page, browser }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    await addFromProductPage(page, catalog.outOfStock);
    await page.goto(`/p/${catalog.lowStock.slug}`);
    await page.getByRole('button', { name: 'Thêm vào giỏ' }).click();
    await expect(page.locator('.mini-cart-badge')).toHaveText('2');
    await openCartPageFromMiniCart(page);
    const checkout = page.getByRole('link', { name: 'Tiến hành đặt hàng' });
    await expect(checkout).not.toHaveAttribute('aria-disabled', 'true');

    // Nhân viên kho trừ hết tồn của bàn phím và để tai nghe còn 2 (giỏ có 1, nên tăng lên 3 sẽ thiếu hàng).
    const adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    const adminPage = await adminContext.newPage();
    await loginAdmin(adminPage);
    await setSkuStock(adminPage, catalog.outOfStock.productId, catalog.outOfStock.skuId, 0);
    await setSkuStock(adminPage, catalog.lowStock.productId, catalog.lowStock.skuId, 2);
    await adminContext.close();

    // Tải lại trang giỏ để lấy giá và tồn kho mới.
    await page.reload();
    await expect(page.getByRole('heading', { level: 1, name: 'Giỏ hàng' })).toBeVisible();
    const outLine = lineOf(page, catalog.outOfStock.name);
    await expect(outLine.locator('.cart-line-reason')).toHaveText('Hết hàng');

    const lowLine = lineOf(page, catalog.lowStock.name);
    await lowLine.getByRole('button', { name: /^Tăng số lượng/ }).click();
    await lowLine.getByRole('button', { name: /^Tăng số lượng/ }).click();
    await expect(lowLine.locator('.cart-line-reason')).toHaveText('Chỉ còn 2 sản phẩm');

    // Nút đặt hàng tắt, có lời giải thích, và bấm vào không rời trang giỏ.
    await expect(checkout).toHaveAttribute('aria-disabled', 'true');
    await expect(page.locator('.cart-checkout-hint')).toContainText('không mua được');
    // Playwright coi liên kết `aria-disabled` là không bấm được, nên ép bấm để chứng minh không điều hướng.
    await checkout.click({ force: true });
    await expect(page).toHaveURL(/\/cart$/);
    await saveScreenshot(page, 'cart-unavailable-1280.png');

    // Cùng giỏ ở 375 px: không tràn ngang và mọi nút bấm trên dòng cao ít nhất 40 px.
    await page.setViewportSize({ width: 375, height: 812 });
    await expectNoHorizontalOverflow(page);
    for (const button of await lowLine.getByRole('button').all()) {
      const box = await button.boundingBox();
      expect(box?.height ?? 0).toBeGreaterThanOrEqual(40);
    }
    await saveScreenshot(page, 'cart-unavailable-375.png');
    await page.setViewportSize({ width: 1280, height: 800 });

    // Tạm tính không cộng dòng không mua được.
    await expect(page.locator('.cart-summary-row strong')).toHaveText('0 đ');

    // Xóa hai dòng lỗi thì nút đặt hàng dùng lại được trên giỏ khác sau khi thêm hàng còn bán.
    await outLine.getByRole('button', { name: /^Xóa/ }).click();
    await lowLine.getByRole('button', { name: /^Xóa/ }).click();
    await expect(page.getByText('Giỏ hàng của bạn đang trống')).toBeVisible();

    expect(issues).toEqual([]);
  });

  test('không tràn ngang ở 1280 px và 375 px trên trang sản phẩm, mini cart và trang giỏ', async ({
    page,
  }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);

    for (const width of [1280, 375]) {
      await page.setViewportSize({ width, height: width === 1280 ? 800 : 812 });
      await page.goto(`/p/${catalog.laptop.slug}`);
      await dismissConsent(page);
      const addButton = page.getByRole('button', { name: 'Thêm vào giỏ' });
      await expect(addButton).toBeEnabled();
      await expectNoHorizontalOverflow(page);
      await saveScreenshot(page, `product-${width}-add-button.png`);

      await addButton.click();
      await expect(page.getByRole('status').filter({ hasText: 'Đã thêm vào giỏ' })).toBeVisible();
      await expectNoHorizontalOverflow(page);

      await page.getByRole('button', { name: /^Giỏ hàng, / }).click();
      await expect(page.getByRole('dialog', { name: 'Giỏ hàng' })).toBeVisible();
      await expectNoHorizontalOverflow(page);
      await saveScreenshot(page, `mini-cart-${width}.png`);
      await page.keyboard.press('Escape');

      await page.goto('/cart');
      await expect(page.locator('li.cart-line').first()).toBeVisible();
      await expectNoHorizontalOverflow(page);
      await saveScreenshot(page, `cart-${width}.png`);
    }

    expect(issues).toEqual([]);
  });
});
