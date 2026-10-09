import { expect, test, type Locator, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';

// Ảnh PNG 1x1 trả cho mọi yêu cầu ảnh sản phẩm, để trình duyệt không ghi lỗi tải ảnh vào console.
const tinyPng = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64',
);

// Thư mục lưu ảnh chụp màn hình khi reviewer cần xem. Không đặt biến này thì spec không ghi ảnh.
const screenshotDirectory = process.env.UI_C1_SHOTS_DIR;

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
    // Trình duyệt tự ghi dòng "Failed to load resource" cho phản hồi 400 và 404 mà test chủ động gây ra.
    if (message.type() === 'error' && /status of (400|404)/.test(message.text())) return;
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

interface SeededCatalog {
  parentSlug: string;
  childSlug: string;
  brandAName: string;
  brandBName: string;
  brandASlug: string;
  productNames: Record<string, string>;
}

interface ProductSeed {
  label: string;
  brandKey: 'A' | 'B';
  ram: '8' | '16' | '32';
  screen: number;
  touch: boolean;
  price: number;
  // Số lượng nhập kho. 0 nghĩa là không nhập, nên sản phẩm hết hàng.
  stock: number;
  finalStatus: 'draft' | 'active' | 'discontinued';
}

// Bảy sản phẩm khách thấy được và một bản nháp. Số đếm trong các test suy ra từ bảng này.
const PRODUCT_SEEDS: ProductSeed[] = [
  {
    label: 'P1',
    brandKey: 'A',
    ram: '8',
    screen: 13,
    touch: false,
    price: 10_000_000,
    stock: 5,
    finalStatus: 'active',
  },
  {
    label: 'P2',
    brandKey: 'A',
    ram: '16',
    screen: 14,
    touch: true,
    price: 15_000_000,
    stock: 5,
    finalStatus: 'active',
  },
  {
    label: 'P3',
    brandKey: 'A',
    ram: '16',
    screen: 15.6,
    touch: false,
    price: 20_000_000,
    stock: 5,
    finalStatus: 'active',
  },
  {
    label: 'P4',
    brandKey: 'B',
    ram: '16',
    screen: 15.6,
    touch: true,
    price: 25_000_000,
    stock: 5,
    finalStatus: 'active',
  },
  {
    label: 'P5',
    brandKey: 'B',
    ram: '32',
    screen: 16,
    touch: false,
    price: 30_000_000,
    stock: 5,
    finalStatus: 'active',
  },
  {
    label: 'P6',
    brandKey: 'B',
    ram: '8',
    screen: 17,
    touch: false,
    price: 35_000_000,
    stock: 0,
    finalStatus: 'active',
  },
  {
    label: 'P7',
    brandKey: 'A',
    ram: '32',
    screen: 14,
    touch: true,
    price: 40_000_000,
    stock: 5,
    finalStatus: 'discontinued',
  },
  // Bản nháp rẻ nhất. Nếu danh sách lộ bản nháp, số đếm và thứ tự giá đều sai.
  {
    label: 'P8',
    brandKey: 'A',
    ram: '16',
    screen: 13,
    touch: false,
    price: 5_000_000,
    stock: 5,
    finalStatus: 'draft',
  },
];

// Tạo dữ liệu bằng API của admin. Gồm danh mục cha có danh mục con, ba thuộc tính lọc được, hai thương hiệu và các sản phẩm.
async function seedCatalog(page: Page): Promise<SeededCatalog> {
  const headers = { Origin: e2eEnvironment.webOrigin };
  const suffix = Date.now().toString(36);

  const parent = await page.request.post('/api/admin/categories', {
    headers,
    data: { name: `Máy tính C1 ${suffix}`, parentId: null, position: 0, attributes: [] },
  });
  expect(parent.status()).toBe(201);
  const parentData = (await parent.json()).data;

  const child = await page.request.post('/api/admin/categories', {
    headers,
    data: {
      name: `Laptop C1 ${suffix}`,
      parentId: parentData.id,
      position: 0,
      attributes: [
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
      ],
    },
  });
  expect(child.status()).toBe(201);
  const childData = (await child.json()).data;

  const brandIds: Record<'A' | 'B', string> = { A: '', B: '' };
  const brandSlugs: Record<'A' | 'B', string> = { A: '', B: '' };
  const brandNames = { A: `Hãng A C1 ${suffix}`, B: `Hãng B C1 ${suffix}` };
  for (const key of ['A', 'B'] as const) {
    const brand = await page.request.post('/api/admin/brands', {
      headers,
      data: { name: brandNames[key] },
    });
    expect(brand.status()).toBe(201);
    const brandData = (await brand.json()).data;
    brandIds[key] = brandData.id;
    brandSlugs[key] = brandData.slug;
  }

  const productNames: Record<string, string> = {};
  for (const seed of PRODUCT_SEEDS) {
    const name = `Laptop ${seed.label} C1 ${suffix}`;
    productNames[seed.label] = name;
    const created = await page.request.post('/api/admin/products', {
      headers,
      data: {
        categoryId: childData.id,
        brandId: brandIds[seed.brandKey],
        name,
        description: { type: 'doc', content: [{ type: 'paragraph' }] },
        specs: { ram: seed.ram, screen: seed.screen, touch: seed.touch },
        options: [],
        variants: [
          {
            optionValues: {},
            price: seed.price,
            enabled: true,
            weightGrams: 1500,
            lengthCm: 35,
            widthCm: 25,
            heightCm: 3,
          },
        ],
        images: [
          { publicId: `pickory/products/e2e-c1-${suffix}-${seed.label}`, alt: `Ảnh ${name}` },
        ],
      },
    });
    expect(created.status(), await created.text()).toBe(201);
    const product = (await created.json()).data;

    if (seed.stock > 0) {
      const adjusted = await page.request.post(
        `/api/admin/skus/${product.variants[0].id}/adjust-stock`,
        {
          headers,
          data: { delta: seed.stock, reason: 'Nhập kho E2E trang danh mục' },
        },
      );
      expect(adjusted.status(), await adjusted.text()).toBe(200);
    }
    if (seed.finalStatus === 'draft') continue;

    // Sản phẩm phải `active` trước. Ngừng kinh doanh là bước chuyển tiếp từ `active`.
    for (const status of seed.finalStatus === 'active' ? ['active'] : ['active', 'discontinued']) {
      const changed = await page.request.post(`/api/admin/products/${product.id}/status`, {
        headers,
        data: { status },
      });
      expect(changed.status(), await changed.text()).toBe(200);
    }
  }

  return {
    parentSlug: parentData.slug,
    childSlug: childData.slug,
    brandAName: brandNames.A,
    brandBName: brandNames.B,
    brandASlug: brandSlugs.A,
    productNames,
  };
}

// Ô lọc là ô điều khiển: trạng thái chọn chỉ đổi sau khi URL đổi và trang vẽ lại. Vì vậy bấm rồi chờ, không dùng `check()`.
async function chooseOption(option: Locator) {
  await option.click();
  await expect(option).toBeChecked();
}

// Dòng tổng số sản phẩm ở đầu trang. Chỉ định theo vùng đầu trang để không lẫn với dòng "Đang tải sản phẩm...".
function totalLabel(page: Page) {
  return page.locator('.catalog-head [role="status"]');
}

async function visibleProductNames(page: Page): Promise<string[]> {
  return page.locator('.catalog-grid .product-card-name').allTextContents();
}

// Nhấn Tab cho tới khi phần tử nhận focus. Mô phỏng người dùng bàn phím đi qua trang.
async function tabUntilFocused(
  page: Page,
  target: ReturnType<Page['locator']>,
  direction: 'Tab' | 'Shift+Tab' = 'Tab',
) {
  for (let pressCount = 0; pressCount < 60; pressCount += 1) {
    if (await target.evaluate((element) => element === document.activeElement)) return;
    await page.keyboard.press(direction);
  }
  throw new Error(`Nhấn ${direction} 60 lần mà phần tử vẫn chưa nhận focus.`);
}

test.describe('UI-C1: trang danh mục của khách', () => {
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

  test('mở lại URL đã chia sẻ thì thấy đúng bộ lọc, trang và danh sách (tiêu chí 1)', async ({
    page,
    browser,
  }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    // Kích thước trang 1 để 2 sản phẩm khớp bộ lọc nằm ở hai trang khác nhau.
    await page.goto(`/c/${catalog.childSlug}?pageSize=1`);
    const consent = page.getByRole('button', { name: 'Chỉ cần thiết' });
    if (await consent.isVisible()) await consent.click();
    await expect(page.getByRole('heading', { level: 1 })).toContainText('Laptop C1');
    await page.getByLabel('Sắp xếp').selectOption('price_asc');
    await chooseOption(
      page
        .getByRole('group', { name: 'Thương hiệu' })
        .getByRole('checkbox', { name: new RegExp(`^${catalog.brandAName}`) }),
    );
    await chooseOption(
      page.getByRole('group', { name: 'RAM (GB)' }).getByRole('checkbox', { name: /^16 GB/ }),
    );
    await expect(totalLabel(page)).toHaveText('2 sản phẩm');
    await expect(page.getByText('Trang 1 / 2 (2 sản phẩm)')).toBeVisible();
    await page.getByRole('button', { name: 'Trang sau' }).click();
    await expect(page.getByText('Trang 2 / 2 (2 sản phẩm)')).toBeVisible();
    expect(await visibleProductNames(page)).toEqual([catalog.productNames.P3]);

    const sharedUrl = page.url();
    expect(sharedUrl).toContain(`brand=${catalog.brandASlug}`);
    expect(sharedUrl).toContain('attr.ram=16');
    expect(sharedUrl).toContain('sort=price_asc');
    expect(sharedUrl).toContain('page=2');
    expect(sharedUrl).toContain('pageSize=1');

    // Trang mới không có trạng thái nào từ trang cũ, nên chỉ URL quyết định kết quả.
    const sharedContext = await browser.newContext({ viewport: { width: 1280, height: 800 } });
    const sharedPage = await sharedContext.newPage();
    const sharedIssues = collectConsoleIssues(sharedPage);
    await stubProductImages(sharedPage);
    await sharedPage.goto(sharedUrl);
    await expect(
      sharedPage.getByRole('checkbox', { name: new RegExp(`^${catalog.brandAName}`) }),
    ).toBeChecked();
    await expect(sharedPage.getByRole('checkbox', { name: /^16 GB/ })).toBeChecked();
    await expect(sharedPage.getByRole('checkbox', { name: /^32 GB/ })).not.toBeChecked();
    await expect(sharedPage.getByLabel('Sắp xếp')).toHaveValue('price_asc');
    await expect(sharedPage.getByText('Trang 2 / 2 (2 sản phẩm)')).toBeVisible();
    expect(await visibleProductNames(sharedPage)).toEqual([catalog.productNames.P3]);
    await saveScreenshot(sharedPage, 'category-1280-shared-url.png');
    expect(sharedIssues).toEqual([]);
    await sharedContext.close();

    expect(issues).toEqual([]);
  });

  test('facet hiện số đếm khớp dữ liệu và giữ số đếm của giá trị khác (tiêu chí 2)', async ({
    page,
  }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto(`/c/${catalog.childSlug}`);
    const consent = page.getByRole('button', { name: 'Chỉ cần thiết' });
    if (await consent.isVisible()) await consent.click();

    const ramGroup = page.getByRole('group', { name: 'RAM (GB)' });
    const brandGroup = page.getByRole('group', { name: 'Thương hiệu' });
    // Bảy sản phẩm khách thấy được. Bản nháp P8 không được đếm vào đâu.
    await expect(totalLabel(page)).toHaveText('7 sản phẩm');
    await expect(ramGroup.getByRole('checkbox', { name: '8 GB (2)' })).toBeVisible();
    await expect(ramGroup.getByRole('checkbox', { name: '16 GB (3)' })).toBeVisible();
    await expect(ramGroup.getByRole('checkbox', { name: '32 GB (2)' })).toBeVisible();
    await expect(
      brandGroup.getByRole('checkbox', { name: `${catalog.brandAName} (4)` }),
    ).toBeVisible();
    await expect(
      brandGroup.getByRole('checkbox', { name: `${catalog.brandBName} (3)` }),
    ).toBeVisible();
    await expect(
      page.getByRole('group', { name: 'Cảm ứng' }).getByRole('radio', { name: 'Có (3)' }),
    ).toBeVisible();
    await expect(
      page.getByRole('group', { name: 'Cảm ứng' }).getByRole('radio', { name: 'Không (4)' }),
    ).toBeVisible();
    await expect(
      page.getByRole('group', { name: 'Màn hình (inch)' }).getByLabel('Từ'),
    ).toHaveAttribute('placeholder', '13');
    await expect(
      page.getByRole('group', { name: 'Màn hình (inch)' }).getByLabel('Đến'),
    ).toHaveAttribute('placeholder', '17');

    // Chọn RAM 16 GB: số đếm của RAM khác vẫn hiện, vì mỗi nhóm bỏ bộ lọc của chính nó.
    await chooseOption(ramGroup.getByRole('checkbox', { name: '16 GB (3)' }));
    await expect(totalLabel(page)).toHaveText('3 sản phẩm');
    await expect(ramGroup.getByRole('checkbox', { name: '8 GB (2)' })).toBeVisible();
    await expect(ramGroup.getByRole('checkbox', { name: '32 GB (2)' })).toBeVisible();
    // Nhóm thương hiệu áp bộ lọc RAM: P2, P3 của hãng A và P4 của hãng B.
    await expect(
      brandGroup.getByRole('checkbox', { name: `${catalog.brandAName} (2)` }),
    ).toBeVisible();
    await expect(
      brandGroup.getByRole('checkbox', { name: `${catalog.brandBName} (1)` }),
    ).toBeVisible();

    // URL chia sẻ có bộ lọc không còn sản phẩm nào. Giá trị đang chọn vẫn hiện với số đếm 0 để khách bỏ chọn được.
    await page.goto(
      `/c/${catalog.childSlug}?brand=${catalog.brandASlug}&attr.ram=32&attr.touch=false`,
    );
    await expect(page.getByText('Không có sản phẩm phù hợp.')).toBeVisible();
    await expect(ramGroup.getByRole('checkbox', { name: '32 GB (0)' })).toBeChecked();
    // Hãng đang chọn hiện tên hãng, không hiện slug kỹ thuật.
    await expect(
      brandGroup.getByRole('checkbox', { name: `${catalog.brandAName} (0)` }),
    ).toBeChecked();
    await expect(ramGroup.getByRole('checkbox', { name: '8 GB (1)' })).toBeVisible();
    await expect(ramGroup.getByRole('checkbox', { name: '16 GB (1)' })).toBeVisible();
    await saveScreenshot(page, 'category-1280-empty-state.png');
    // Bỏ chọn xong thì giá trị có số đếm 0 biến mất, vì không còn lý do để giữ nó.
    await ramGroup.getByRole('checkbox', { name: '32 GB (0)' }).click();
    await expect(ramGroup.getByRole('checkbox', { name: /^32 GB/ })).toHaveCount(0);
    await expect(totalLabel(page)).toHaveText('2 sản phẩm');

    expect(issues).toEqual([]);
  });

  test('thẻ sản phẩm, nhãn trạng thái, ảnh và các trạng thái đặc biệt', async ({ page }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto(`/c/${catalog.childSlug}`);
    const consent = page.getByRole('button', { name: 'Chỉ cần thiết' });
    if (await consent.isVisible()) await consent.click();
    await expect(page.locator('.catalog-grid .product-card')).toHaveCount(7);

    const names = await visibleProductNames(page);
    expect(names).toContain(catalog.productNames.P1);
    expect(names).not.toContain(catalog.productNames.P8);

    const outOfStockCard = page
      .locator('.product-card')
      .filter({ hasText: catalog.productNames.P6 });
    await expect(outOfStockCard.getByText('Hết hàng')).toBeVisible();
    const discontinuedCard = page
      .locator('.product-card')
      .filter({ hasText: catalog.productNames.P7 });
    await expect(discontinuedCard.getByText('Ngừng kinh doanh')).toBeVisible();
    await expect(discontinuedCard.getByText('Hết hàng')).toHaveCount(0);

    const firstCard = page.locator('.product-card').filter({ hasText: catalog.productNames.P1 });
    await expect(firstCard.getByText('10.000.000 đ')).toBeVisible();
    const image = firstCard.locator('img');
    await expect(image).toHaveAttribute('alt', `Ảnh ${catalog.productNames.P1}`);
    await expect(image).toHaveAttribute('loading', 'lazy');
    const srcSet = (await image.getAttribute('srcset')) ?? '';
    for (const width of [200, 400, 800, 1200]) {
      expect(srcSet).toContain(`f_auto,q_auto,w_${width}/`);
      expect(srcSet).toContain(` ${width}w`);
    }
    await expect(firstCard.locator('a')).toHaveAttribute('href', /^\/p\/laptop-p1-c1-/);

    // Danh mục cha trả cả sản phẩm của danh mục con, kèm đường dẫn từ trang chủ.
    await page.goto(`/c/${catalog.parentSlug}`);
    await expect(totalLabel(page)).toHaveText('7 sản phẩm');
    await expect(page.getByRole('navigation', { name: 'Đường dẫn' })).toContainText('Trang chủ');
    await page.goto(`/c/${catalog.childSlug}`);
    const crumbs = page.getByRole('navigation', { name: 'Đường dẫn' });
    await expect(crumbs.getByRole('link', { name: /^Máy tính C1/ })).toHaveAttribute(
      'href',
      `/c/${catalog.parentSlug}`,
    );
    await expect(crumbs.locator('[aria-current="page"]')).toContainText('Laptop C1');
    await saveScreenshot(page, 'category-1280-grid.png');

    // Bộ lọc không có kết quả hiện trạng thái rỗng kèm nút xóa bộ lọc.
    await page.goto(`/c/${catalog.childSlug}?priceMin=90000000`);
    await expect(page.getByText('Không có sản phẩm phù hợp.')).toBeVisible();
    await page.locator('.catalog-empty').getByRole('button', { name: 'Xóa bộ lọc' }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/c/${catalog.childSlug}`);
    await expect(page.locator('.catalog-grid .product-card')).toHaveCount(7);

    // Trang vượt trang cuối hiện trạng thái rỗng kèm link về trang 1.
    await page.goto(`/c/${catalog.childSlug}?page=9`);
    await expect(page.getByText('Trang 9 không có sản phẩm.')).toBeVisible();
    await page.getByRole('link', { name: 'Về trang 1' }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/c/${catalog.childSlug}`);
    await expect(page.locator('.catalog-grid .product-card')).toHaveCount(7);

    // Bộ lọc lạ từ URL sửa tay hiện lỗi kèm nút xóa bộ lọc, trang không trắng.
    await page.goto(`/c/${catalog.childSlug}?attr.khong_ton_tai=1`);
    const alert = page.getByRole('alert');
    await expect(alert).toBeVisible();
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await saveScreenshot(page, 'category-1280-unknown-filter.png');
    await page.locator('.catalog-results').getByRole('button', { name: 'Xóa bộ lọc' }).click();
    await expect(page.getByRole('alert')).toHaveCount(0);
    await expect(page.locator('.catalog-grid .product-card')).toHaveCount(7);

    // Slug lạ hiện thông báo kèm link về trang chủ.
    await page.goto('/c/khong-co-danh-muc-nay');
    await expect(page.getByRole('heading', { name: 'Không tìm thấy danh mục' })).toBeVisible();
    await page.getByRole('link', { name: 'Về trang chủ' }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/`);

    expect(issues).toEqual([]);
  });

  test('nút Back của trình duyệt trả về bộ lọc trước', async ({ page }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto(`/c/${catalog.childSlug}`);
    const consent = page.getByRole('button', { name: 'Chỉ cần thiết' });
    if (await consent.isVisible()) await consent.click();

    const brandBox = page.getByRole('checkbox', { name: new RegExp(`^${catalog.brandBName}`) });
    const ramBox = page.getByRole('checkbox', { name: /^8 GB/ });
    await chooseOption(brandBox);
    await chooseOption(ramBox);
    await expect(totalLabel(page)).toHaveText('1 sản phẩm');

    await page.goBack();
    await expect(page).toHaveURL(/brand=/);
    await expect(page).not.toHaveURL(/attr\.ram/);
    await expect(brandBox).toBeChecked();
    await expect(ramBox).not.toBeChecked();
    await expect(totalLabel(page)).toHaveText('3 sản phẩm');

    expect(issues).toEqual([]);
  });

  test('ô giá chỉ áp khi bấm "Áp dụng" hoặc Enter', async ({ page }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    let productRequests = 0;
    page.on('request', (request) => {
      if (request.url().includes('/api/products?')) productRequests += 1;
    });
    await page.goto(`/c/${catalog.childSlug}`);
    const consent = page.getByRole('button', { name: 'Chỉ cần thiết' });
    if (await consent.isVisible()) await consent.click();
    await expect(page.locator('.catalog-grid .product-card')).toHaveCount(7);
    const requestsBeforeTyping = productRequests;

    const priceGroup = page.getByRole('group', { name: 'Mức giá (đ)' });
    await priceGroup.getByLabel('Từ').pressSequentially('18000000');
    await priceGroup.getByLabel('Đến').pressSequentially('31000000');
    // Mỗi phím gõ không được gọi API.
    expect(productRequests).toBe(requestsBeforeTyping);

    await priceGroup.getByLabel('Đến').press('Enter');
    await expect(page).toHaveURL(/priceMin=18000000&priceMax=31000000/);
    await expect(page.locator('.catalog-grid .product-card')).toHaveCount(3);

    // Khoảng ngược hiện lỗi dưới ô và không đổi URL.
    await priceGroup.getByLabel('Từ').fill('50');
    await priceGroup.getByLabel('Đến').fill('10');
    await priceGroup.getByRole('button', { name: 'Áp dụng' }).click();
    await expect(priceGroup.getByText('Giá trị "Từ" không được lớn hơn "Đến".')).toBeVisible();
    await expect(page).toHaveURL(/priceMin=18000000&priceMax=31000000/);

    expect(issues).toEqual([]);
  });

  for (const viewport of [
    { width: 1280, height: 800 },
    { width: 375, height: 812 },
  ]) {
    test(`ô khoảng giá và khoảng thuộc tính hiện đủ 9 chữ số ở ${viewport.width} px`, async ({
      page,
    }) => {
      const issues = collectConsoleIssues(page);
      await stubProductImages(page);
      await page.setViewportSize(viewport);
      await page.goto(`/c/${catalog.childSlug}`);
      const consent = page.getByRole('button', { name: 'Chỉ cần thiết' });
      if (await consent.isVisible()) await consent.click();
      await expect(page.locator('.catalog-grid .product-card')).toHaveCount(7);

      // Màn hẹp giấu bộ lọc sau nút "Bộ lọc". Mở nút này trước khi nhập.
      const toggle = page.getByRole('button', { name: /^Bộ lọc/ });
      if (await toggle.isVisible()) await toggle.click();
      const panel = page.getByRole('complementary', { name: 'Bộ lọc sản phẩm' });
      await expect(panel).toBeVisible();

      for (const legend of ['Mức giá (đ)', 'Màn hình']) {
        const group = panel.getByRole('group', {
          name: new RegExp(`^${legend.replace('(', '\\(').replace(')', '\\)')}`),
        });
        for (const label of ['Từ', 'Đến']) {
          const input = group.getByLabel(label);
          await input.fill('999999999');
          // Chữ số bị cắt khi nội dung rộng hơn phần nhìn thấy của ô.
          const widths = await input.evaluate((element) => ({
            scrollWidth: element.scrollWidth,
            clientWidth: element.clientWidth,
          }));
          expect(widths.scrollWidth, `${legend} / ${label}`).toBeLessThanOrEqual(
            widths.clientWidth,
          );
        }
        if (screenshotDirectory) {
          const shotName = legend === 'Mức giá (đ)' ? 'price' : 'attribute';
          await group.screenshot({
            path: `${screenshotDirectory}/range-${shotName}-${viewport.width}.png`,
          });
        }
      }

      expect(issues).toEqual([]);
    });
  }

  test('viewport 375 px: không cuộn ngang và bộ lọc dùng được bằng bàn phím (tiêu chí 3)', async ({
    page,
  }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto(`/c/${catalog.childSlug}`);
    const consent = page.getByRole('button', { name: 'Chỉ cần thiết' });
    if (await consent.isVisible()) await consent.click();
    await expect(page.locator('.catalog-grid .product-card')).toHaveCount(7);

    const toggle = page.getByRole('button', { name: /^Bộ lọc/ });
    const panel = page.getByRole('complementary', { name: 'Bộ lọc sản phẩm' });

    await test.step('khối bộ lọc đóng: không cuộn ngang', async () => {
      await expect(toggle).toHaveAttribute('aria-expanded', 'false');
      await expect(panel).toBeHidden();
      await expectNoHorizontalPageOverflow(page);
      await saveScreenshot(page, 'category-375-closed.png');
    });

    await test.step('mở bằng Enter: focus vào khối và không cuộn ngang', async () => {
      await tabUntilFocused(page, toggle);
      await page.keyboard.press('Enter');
      await expect(toggle).toHaveAttribute('aria-expanded', 'true');
      await expect(toggle).toHaveAttribute('aria-controls', 'category-filters');
      await expect(panel).toBeVisible();
      await expect(panel).toBeFocused();
      await expectNoHorizontalPageOverflow(page);
      await saveScreenshot(page, 'category-375-open.png');
      await saveScreenshot(page, 'category-375-open-top.png', false);
    });

    await test.step('Tab tới ô RAM rồi Space chọn: URL đổi theo', async () => {
      const ramBox = panel.getByRole('checkbox', { name: '16 GB (3)' });
      await tabUntilFocused(page, ramBox);
      await page.keyboard.press('Space');
      await expect(page).toHaveURL(/attr\.ram=16/);
      await expect(ramBox).toBeChecked();
      await expectNoHorizontalPageOverflow(page);
    });

    await test.step('Enter trong ô giá áp khoảng giá lên URL', async () => {
      const priceMin = panel.getByRole('group', { name: 'Mức giá (đ)' }).getByLabel('Từ');
      await tabUntilFocused(page, priceMin, 'Shift+Tab');
      await page.keyboard.type('16000000');
      await page.keyboard.press('Enter');
      await expect(page).toHaveURL(/priceMin=16000000/);
      await expect(page).toHaveURL(/attr\.ram=16/);
    });

    await test.step('Escape đóng khối và trả focus về nút "Bộ lọc"', async () => {
      await panel.getByRole('checkbox', { name: /^Chỉ hiện hàng còn/ }).focus();
      await page.keyboard.press('Escape');
      await expect(toggle).toHaveAttribute('aria-expanded', 'false');
      await expect(panel).toBeHidden();
      await expect(toggle).toBeFocused();
      await expectNoHorizontalPageOverflow(page);
    });

    expect(issues).toEqual([]);
  });

  test('bộ lọc trên màn rộng dùng được bằng bàn phím và hiện ngay, không có nút gập', async ({
    page,
  }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto(`/c/${catalog.childSlug}`);
    const consent = page.getByRole('button', { name: 'Chỉ cần thiết' });
    if (await consent.isVisible()) await consent.click();
    await expect(page.locator('.catalog-grid .product-card')).toHaveCount(7);

    await expect(page.getByRole('button', { name: /^Bộ lọc/ })).toBeHidden();
    await expect(page.getByRole('complementary', { name: 'Bộ lọc sản phẩm' })).toBeVisible();
    await expectNoHorizontalPageOverflow(page);

    const inStockBox = page.getByRole('checkbox', { name: /^Chỉ hiện hàng còn/ });
    await tabUntilFocused(page, inStockBox);
    await page.keyboard.press('Space');
    await expect(page).toHaveURL(/inStock=true/);
    // Hàng hết (P6) biến mất khỏi danh sách.
    await expect(
      page.locator('.product-card').filter({ hasText: catalog.productNames.P6 }),
    ).toHaveCount(0);
    await saveScreenshot(page, 'category-1280-filtered.png');

    expect(issues).toEqual([]);
  });
});
