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
}

// Tạo sản phẩm bằng API của admin. Tên có dấu tiếng Việt và tồn kho thấp, để tìm không dấu và lọc sắp hết hàng có dữ liệu.
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
          stock: 3,
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
  return { name };
}

test('FLT-UI3: màn sản phẩm giữ bộ lọc trên URL, không tràn trang', async ({ page }, testInfo) => {
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

  for (const width of [1280, 375]) {
    await test.step(`Ảnh chụp màn sản phẩm ở ${width}px không tràn ngang`, async () => {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      await page.goto('/admin/products?status=draft');
      await waitForListLoaded(page);
      await expectNoHorizontalOverflow(page);
      await page.screenshot({
        path: testInfo.outputPath(`products-filters-${width}.png`),
        fullPage: true,
      });
    });
  }

  expect(consoleIssues).toEqual([]);
});
