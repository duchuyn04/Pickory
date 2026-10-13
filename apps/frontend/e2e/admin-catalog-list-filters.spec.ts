import { expect, test, type Page } from '@playwright/test';
import { collectConsoleIssues, loginAdmin } from './catalog-admin-flow';
import { e2eEnvironment } from './environment';

// Trang không được rộng hơn khung nhìn. Bảng rộng phải cuộn trong khung riêng của nó.
async function expectNoHorizontalOverflow(page: Page) {
  const widths = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth,
  }));
  expect(widths.content).toBeLessThanOrEqual(widths.viewport);
}

// Chờ dòng nạp biến mất. Thông báo rỗng của bộ lọc cũng dùng `role="status"` nên chỉ đếm dòng nạp.
async function waitForListLoaded(page: Page) {
  await expect(page.getByText(/^Đang (nạp|tải)/)).toHaveCount(0);
}

interface SeededProduct {
  name: string;
  productId: string;
  skuId: string;
  skuCode: string;
}

// Tạo sản phẩm bằng API của admin. Tên có dấu tiếng Việt và tồn kho thấp, để tìm không dấu và lọc tồn kho có dữ liệu.
async function seedProduct(page: Page): Promise<SeededProduct> {
  const headers = { Origin: e2eEnvironment.webOrigin };
  const suffix = Date.now().toString(36);

  const category = await page.request.post('/api/admin/categories', {
    headers,
    data: { name: `Danh mục lọc ${suffix}`, parentId: null, position: 0, attributes: [] },
  });
  expect(category.status(), await category.text()).toBe(201);
  const brand = await page.request.post('/api/admin/brands', {
    headers,
    data: { name: `Hãng lọc ${suffix}` },
  });
  expect(brand.status(), await brand.text()).toBe(201);

  const name = `Điện thoại Lọc ${suffix}`;
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
          enabled: true,
          weightGrams: 400,
          lengthCm: 20,
          widthCm: 10,
          heightCm: 3,
        },
      ],
      images: [{ publicId: `pickory/products/e2e-ui3-${suffix}`, alt: `Ảnh ${name}` }],
    },
  });
  expect(created.status(), await created.text()).toBe(201);
  const product = (await created.json()).data;
  const sku = product.variants[0];

  // Tồn kho 3 nằm trong ngưỡng sắp hết hàng. Lần điều chỉnh này cũng tạo một biến động loại `manual`.
  const adjusted = await page.request.post(`/api/admin/skus/${sku.id}/movements`, {
    headers,
    data: { delta: 3, reason: 'Nhập kho E2E màn lọc' },
  });
  expect(adjusted.status(), await adjusted.text()).toBe(201);

  return { name, productId: product.id, skuId: sku.id, skuCode: sku.code };
}

test('FLT-UI3: màn sản phẩm, tồn kho và biến động kho giữ bộ lọc trên URL, không tràn trang', async ({
  page,
}, testInfo) => {
  test.setTimeout(120000);
  const consoleIssues = collectConsoleIssues(page);

  await loginAdmin(page);
  const seeded = await seedProduct(page);
  const accentFreeName = seeded.name.replace('Điện thoại Lọc', 'dien thoai loc');

  await test.step('Từ khóa không dấu của sản phẩm nằm trên URL và còn nguyên sau khi tải lại', async () => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/admin/products');
    await expect(page.getByRole('heading', { level: 1, name: 'Sản phẩm' })).toBeVisible();

    const searchBox = page.getByLabel('Tên hoặc mã SKU', { exact: true });
    await searchBox.fill(accentFreeName);
    // Ô tìm kiếm chờ 300 ms sau lần gõ cuối rồi mới ghi từ khóa lên URL.
    await expect(page).toHaveURL(/[?&]search=/);
    await waitForListLoaded(page);
    const rows = page.locator('table.product-list-table tbody tr');
    await expect(rows).toHaveCount(1);
    await expect(rows.first()).toContainText(seeded.name);
    const rowsBeforeReload = await rows.allInnerTexts();

    await page.reload();
    await expect(searchBox).toHaveValue(accentFreeName);
    await waitForListLoaded(page);
    // Cùng từ khóa phải cho cùng danh sách dòng.
    await expect.poll(() => rows.allInnerTexts()).toEqual(rowsBeforeReload);
  });

  await test.step('Màn tồn kho lọc theo mã SKU từ link', async () => {
    await page.goto(`/admin/inventory?search=${encodeURIComponent(seeded.skuCode)}`);
    await expect(page.getByLabel('Tìm theo mã SKU hoặc tên sản phẩm')).toHaveValue(seeded.skuCode);
    await waitForListLoaded(page);

    const rows = page.locator('table.inventory-table tbody tr');
    await expect(rows).toHaveCount(1);
    await expect(rows.first()).toContainText(seeded.skuCode);
  });

  await test.step('Màn biến động kho lọc theo loại từ link', async () => {
    const movementsPath = `/admin/products/${seeded.productId}/skus/${seeded.skuId}/inventory`;
    await page.goto(`${movementsPath}?type=manual`);
    await expect(page.getByLabel('Loại biến động')).toHaveValue('manual');
    await waitForListLoaded(page);
    await expect(page.locator('table.movement-table tbody tr')).toHaveCount(1);

    // Loại không có biến động nào cho SKU này hiện thông báo rỗng của bộ lọc.
    await page.goto(`${movementsPath}?type=return`);
    await waitForListLoaded(page);
    await expect(page.getByText('Không có kết quả phù hợp với bộ lọc.')).toBeVisible();
  });

  for (const width of [1280, 375]) {
    await test.step(`Ảnh chụp ba màn ở ${width}px không tràn ngang`, async () => {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      const screens = [
        { path: '/admin/products?status=draft', name: 'products' },
        { path: '/admin/inventory?search=a', name: 'inventory' },
        {
          path: `/admin/products/${seeded.productId}/skus/${seeded.skuId}/inventory?type=manual`,
          name: 'movements',
        },
      ];
      for (const screen of screens) {
        await page.goto(screen.path);
        await waitForListLoaded(page);
        await expectNoHorizontalOverflow(page);
        await page.screenshot({
          path: testInfo.outputPath(`${screen.name}-filters-${width}.png`),
          fullPage: true,
        });
      }
    });
  }

  expect(consoleIssues).toEqual([]);
});
