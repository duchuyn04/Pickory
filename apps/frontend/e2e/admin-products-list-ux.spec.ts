import { expect, test, type Page } from '@playwright/test';
import { collectConsoleIssues, loginAdmin } from './catalog-admin-flow';
import { e2eEnvironment } from './environment';

const SEARCH_LABEL = 'Tên hoặc mã SKU';
const FILTER_BAR_NAME = 'Bộ lọc đang áp dụng';

// Trang không được rộng hơn khung nhìn. Bảng rộng phải cuộn trong khung riêng của nó.
async function expectNoHorizontalOverflow(page: Page) {
  const widths = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth,
  }));
  expect(widths.content).toBeLessThanOrEqual(widths.viewport);
}

interface SeededProducts {
  phoneName: string;
  phoneSkuCode: string;
  categoryId: string;
  categoryName: string;
  uniqueKeyword: string;
}

// Tạo danh mục, thương hiệu và hai sản phẩm bằng API của admin.
// Danh mục chỉ chứa hai sản phẩm này, nên lọc theo danh mục cho số dòng cố định.
async function seedProducts(page: Page): Promise<SeededProducts> {
  const headers = { Origin: e2eEnvironment.webOrigin };
  const suffix = Date.now().toString(36);

  const categoryName = `Danh mục giữ ${suffix}`;
  const category = await page.request.post('/api/admin/categories', {
    headers,
    data: { name: categoryName, parentId: null, position: 0, attributes: [] },
  });
  expect(category.status(), await category.text()).toBe(201);
  const brand = await page.request.post('/api/admin/brands', {
    headers,
    data: { name: `Hãng giữ ${suffix}` },
  });
  expect(brand.status(), await brand.text()).toBe(201);

  async function createProduct(name: string) {
    const created = await page.request.post('/api/admin/products', {
      headers,
      data: {
        categoryId: (await category.json()).data.id,
        brandId: (await brand.json()).data.id,
        name,
        description: { type: 'doc', content: [{ type: 'paragraph' }] },
        specs: {},
        options: [],
        variants: [
          {
            optionValues: {},
            price: 9_000_000,
            stock: 3,
            enabled: true,
            weightGrams: 400,
            lengthCm: 20,
            widthCm: 10,
            heightCm: 3,
          },
        ],
        images: [{ publicId: `pickory/products/e2e-ui6-${suffix}`, alt: `Ảnh ${name}` }],
      },
    });
    expect(created.status(), await created.text()).toBe(201);
    const product = (await created.json()).data;
    return product;
  }

  const phoneName = `Điện thoại Giữ ${suffix}`;
  const phone = await createProduct(phoneName);
  await createProduct(`Áo khoác Giữ ${suffix}`);

  return {
    phoneName,
    phoneSkuCode: phone.variants[0].code,
    categoryId: (await category.json()).data.id,
    categoryName,
    uniqueKeyword: `dien thoai giu ${suffix}`,
  };
}

// Giữ response `GET /api/admin/products` lại tới khi test cho đi. Test điều khiển thời điểm, không chờ thời gian thật.
async function controlProductListResponses(page: Page) {
  let gate: Promise<void> | null = null;
  let heldCount = 0;

  await page.route(
    (url) => url.pathname === '/api/admin/products',
    async (route) => {
      if (gate === null || route.request().method() !== 'GET') {
        await route.continue();
        return;
      }
      heldCount += 1;
      await gate;
      await route.continue();
    },
  );

  return {
    heldCount: () => heldCount,
    hold() {
      let release!: () => void;
      gate = new Promise<void>((resolve) => (release = resolve));
      return () => {
        gate = null;
        release();
      };
    },
  };
}

test('FLT-UI6: màn sản phẩm giữ bảng cũ, tô đậm chữ khớp, hiện thanh bộ lọc', async ({
  page,
}, testInfo) => {
  test.setTimeout(120000);
  const consoleIssues = collectConsoleIssues(page);
  const productList = await controlProductListResponses(page);

  await loginAdmin(page);
  const seeded = await seedProducts(page);
  await page.setViewportSize({ width: 1280, height: 800 });

  const rows = page.locator('table.product-list-table tbody tr');
  const searchBox = page.getByLabel(SEARCH_LABEL, { exact: true });
  const filterBar = page.getByRole('group', { name: FILTER_BAR_NAME });

  // Mở danh sách đã lọc theo danh mục riêng của spec, nên luôn đúng hai dòng. DB dùng chung với spec khác.
  async function openProductList() {
    await page.goto(`/admin/products?categoryId=${seeded.categoryId}`);
    await expect(
      page.getByRole('heading', { level: 1, name: 'Sản phẩm', exact: true }),
    ).toBeVisible();
    await expect(rows).toHaveCount(2);
  }

  await test.step('Gõ dien thoai: chữ Điện thoại trong tên nằm trong mark, mã SKU khớp cũng được tô', async () => {
    await openProductList();
    // Không có từ khóa thì bảng chưa có chữ nào được tô.
    await expect(page.locator('table.product-list-table mark')).toHaveCount(0);

    await searchBox.fill(seeded.uniqueKeyword);
    await searchBox.press('Enter');
    await expect(rows).toHaveCount(1);
    // Từ khóa không dấu khớp tên có dấu. Chữ trong mark vẫn là chữ gốc có dấu.
    await expect(rows.first().locator('mark')).toHaveText(seeded.phoneName);

    await searchBox.fill(seeded.phoneSkuCode);
    await searchBox.press('Enter');
    await expect(rows).toHaveCount(1);
    await expect(rows.first().locator('.cell-sub mark')).toHaveText(seeded.phoneSkuCode);
    // Tên không chứa mã SKU nên chỉ mã SKU được tô.
    await expect(rows.first().locator('mark')).toHaveCount(1);
  });

  await test.step('Giữ response: bảng cũ còn, mờ đi và có aria-busy cho tới khi response về', async () => {
    await openProductList();
    const rowsBefore = await rows.allInnerTexts();
    const busyBlock = page.locator('.product-list-body');
    await expect(busyBlock).not.toHaveAttribute('aria-busy', 'true');

    const releaseResponse = productList.hold();
    const heldBefore = productList.heldCount();
    await searchBox.fill(seeded.uniqueKeyword);
    await searchBox.press('Enter');
    // Chờ tới khi trình duyệt đã gửi request và test đang giữ nó.
    await expect.poll(() => productList.heldCount()).toBe(heldBefore + 1);

    await expect(busyBlock).toHaveAttribute('aria-busy', 'true');
    await expect(busyBlock).toHaveCSS('opacity', '0.6');
    // Bảng cũ vẫn nguyên hai dòng của danh mục riêng, như trước khi gõ.
    expect(await rows.allInnerTexts()).toEqual(rowsBefore);
    await expect(page.locator('.search-input-spinner')).toBeVisible();
    await expect(page.locator('.search-input-control [aria-live]')).toHaveText('Đang tải kết quả');
    // Ô có chữ thì có nút xóa. Vòng xoay nằm bên trái nút, không đè lên nút.
    const spinnerBox = await page.locator('.search-input-spinner').boundingBox();
    const clearButtonBox = await page.getByRole('button', { name: 'Xóa từ khóa' }).boundingBox();
    expect(spinnerBox).not.toBeNull();
    expect(clearButtonBox).not.toBeNull();
    expect(spinnerBox!.x + spinnerBox!.width).toBeLessThanOrEqual(clearButtonBox!.x);
    // Lần tải đầu mới hiện dòng nạp. Lần này đã có bảng nên dòng đó không hiện.
    await expect(page.getByText('Đang tải sản phẩm...')).toHaveCount(0);

    releaseResponse();

    await expect(busyBlock).not.toHaveAttribute('aria-busy', 'true');
    await expect(busyBlock).toHaveCSS('opacity', '1');
    await expect(page.locator('.search-input-spinner')).toHaveCount(0);
    await expect(rows).toHaveCount(1);
    await expect(rows.first()).toContainText(seeded.phoneName);
  });

  await test.step('Ô tìm rỗng mà màn đang tải: vòng xoay sát mép phải, không có nút xóa', async () => {
    await openProductList();
    const releaseResponse = productList.hold();
    const heldBefore = productList.heldCount();
    // Đổi trạng thái chứ không gõ từ khóa, nên ô tìm vẫn rỗng và không có nút xóa.
    await page.getByLabel('Trạng thái', { exact: true }).selectOption('draft');
    await expect.poll(() => productList.heldCount()).toBe(heldBefore + 1);

    await expect(page.locator('.search-input-spinner')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Xóa từ khóa' })).toHaveCount(0);
    const inputBox = await searchBox.boundingBox();
    const spinnerBox = await page.locator('.search-input-spinner').boundingBox();
    expect(inputBox).not.toBeNull();
    expect(spinnerBox).not.toBeNull();
    // Khoảng cách từ vòng xoay tới mép phải ô không quá 12 px: sát mép, không chừa chỗ trống của nút xóa.
    expect(inputBox!.x + inputBox!.width - (spinnerBox!.x + spinnerBox!.width)).toBeLessThanOrEqual(
      12,
    );
    await page.screenshot({
      path: testInfo.outputPath('FLT-UI6-spinner-empty.png'),
      clip: {
        x: inputBox!.x - 16,
        y: inputBox!.y - 40,
        width: inputBox!.width + 32,
        height: inputBox!.height + 56,
      },
    });

    releaseResponse();
    await expect(page.locator('.search-input-spinner')).toHaveCount(0);
  });

  await test.step('Bật bộ lọc: thanh có đủ nhãn, ảnh chụp 1280 px và 375 px không tràn ngang', async () => {
    // Danh mục đã nằm trên URL từ `openProductList`, nên thanh có sẵn nhãn danh mục.
    await openProductList();
    await page.getByLabel('Trạng thái', { exact: true }).selectOption('draft');
    await page.getByRole('checkbox', { name: /Sắp hết hàng/ }).check();
    await searchBox.fill('dien thoai');
    await searchBox.press('Enter');

    await expect(filterBar.getByRole('listitem')).toHaveText([
      'Từ khóa: dien thoai',
      'Trạng thái: Bản nháp',
      `Danh mục: ${seeded.categoryName}`,
      'Sắp hết hàng',
    ]);
    await expect(filterBar).toContainText('1 sản phẩm khớp "dien thoai"');
    await expect(rows).toHaveCount(1);
    await expect(rows.first().locator('mark')).toHaveText('Điện thoại');

    for (const width of [1280, 375]) {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      await expect(filterBar).toBeVisible();
      await expectNoHorizontalOverflow(page);
      await page.screenshot({
        path: testInfo.outputPath(`FLT-UI6-products-${width}.png`),
        fullPage: true,
      });
    }
    await page.setViewportSize({ width: 1280, height: 800 });
  });

  await test.step('Bấm X một nhãn: URL chỉ mất tham số đó. Bấm Xóa bộ lọc: URL hết bộ lọc, thanh ẩn', async () => {
    await page.getByRole('button', { name: 'Bỏ lọc Trạng thái: Bản nháp' }).click();

    await expect(page).not.toHaveURL(/[?&]status=/);
    await expect(page).toHaveURL(/[?&]search=dien/);
    await expect(page).toHaveURL(/[?&]categoryId=/);
    await expect(page).toHaveURL(/[?&]lowStock=true/);
    await expect(filterBar.getByRole('listitem')).toHaveCount(3);

    await page.getByRole('button', { name: 'Bỏ lọc Từ khóa: dien thoai' }).click();
    await expect(page).not.toHaveURL(/[?&]search=/);
    // Bỏ nhãn từ khóa thì ô tìm cũng rỗng.
    await expect(searchBox).toHaveValue('');

    await page.getByRole('button', { name: 'Xóa bộ lọc', exact: true }).click();
    await expect(page).not.toHaveURL(/[?&](search|status|categoryId|lowStock)=/);
    await expect(filterBar).toHaveCount(0);
    // Không còn bộ lọc thì dòng đếm cũ ở đầu danh sách trở lại. Danh sách là cả DB, nên không chờ số cố định.
    await expect(page.locator('.product-list-head')).toContainText(/\d+ sản phẩm/);
  });

  expect(consoleIssues).toEqual([]);
});
