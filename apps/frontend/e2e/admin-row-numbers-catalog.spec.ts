import { expect, test, type Locator, type Page } from '@playwright/test';
import { collectConsoleIssues, loginAdmin } from './catalog-admin-flow';
import { e2eEnvironment } from './environment';

const PRODUCT_COUNT = 21;

// Trang không được rộng hơn khung nhìn. Bảng rộng phải cuộn trong khung riêng của nó.
async function expectNoHorizontalOverflow(page: Page) {
  const widths = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth,
  }));
  expect(widths.content).toBeLessThanOrEqual(widths.viewport);
}

// Đọc `text-align` đã tính của một ô, để phép kiểm căn phải dựa trên kiểu thật của trình duyệt.
async function textAlignOf(cell: Locator) {
  return cell.evaluate((element) => getComputedStyle(element).textAlign);
}

interface SeededCatalog {
  categoryId: string;
}

// Tạo danh mục riêng với 21 sản phẩm, đủ để danh sách có hai trang.
async function seedCatalog(page: Page): Promise<SeededCatalog> {
  const headers = { Origin: e2eEnvironment.webOrigin };
  const suffix = Date.now().toString(36);

  const category = await page.request.post('/api/admin/categories', {
    headers,
    data: { name: `Danh mục STT ${suffix}`, parentId: null, position: 0, attributes: [] },
  });
  expect(category.status(), await category.text()).toBe(201);
  const categoryId = (await category.json()).data.id;
  const brand = await page.request.post('/api/admin/brands', {
    headers,
    data: { name: `Hãng STT ${suffix}` },
  });
  expect(brand.status(), await brand.text()).toBe(201);
  const brandId = (await brand.json()).data.id;

  for (let number = 1; number <= PRODUCT_COUNT; number += 1) {
    const created = await page.request.post('/api/admin/products', {
      headers,
      data: {
        categoryId,
        brandId,
        name: `Sản phẩm STT ${String(number).padStart(2, '0')} ${suffix}`,
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
        images: [],
      },
    });
    expect(created.status(), await created.text()).toBe(201);
  }

  return { categoryId };
}

// Chụp cả trang ở hai bề rộng. Mỗi lần kiểm trang không tràn ngang.
async function captureBothWidths(
  page: Page,
  screen: string,
  outputPath: (name: string) => string,
  ready: Locator,
) {
  for (const width of [1280, 375]) {
    await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
    await expect(ready).toBeVisible();
    await expectNoHorizontalOverflow(page);
    await page.screenshot({ path: outputPath(`FLT-UI8-${screen}-${width}.png`), fullPage: true });
  }
  await page.setViewportSize({ width: 1280, height: 800 });
}

test('FLT-UI8: màn sản phẩm có cột STT', async ({ page }, testInfo) => {
  test.setTimeout(180000);
  const consoleIssues = collectConsoleIssues(page);

  await loginAdmin(page);
  const seeded = await seedCatalog(page);
  await page.setViewportSize({ width: 1280, height: 800 });

  await test.step('Màn sản phẩm: trang 1 có STT 1 đến 20, trang 2 bắt đầu từ 21', async () => {
    await page.goto(`/admin/products?categoryId=${seeded.categoryId}`);
    const rows = page.locator('table.product-list-table tbody tr');
    await expect(rows).toHaveCount(20);

    const headerCell = page.locator('table.product-list-table thead th').first();
    await expect(headerCell).toHaveText('STT');
    expect(await textAlignOf(headerCell)).toBe('right');
    await expect(rows.first().locator('td').first()).toHaveText('1');
    await expect(rows.last().locator('td').first()).toHaveText('20');
    expect(await textAlignOf(rows.first().locator('td').first())).toBe('right');

    await captureBothWidths(page, 'products', testInfo.outputPath.bind(testInfo), headerCell);

    await page.getByRole('button', { name: 'Trang sau', exact: true }).click();
    await expect(page).toHaveURL(/[?&]page=2/);
    // Trang 2 giữ bảng cũ cho tới khi response về, nên chờ tới khi chỉ còn một dòng của trang 2.
    await expect(rows).toHaveCount(1);
    await expect(rows.first().locator('td').first()).toHaveText('21');
  });

  expect(consoleIssues).toEqual([]);
});
