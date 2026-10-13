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
  await expect(page.getByText(/^Đang nạp/)).toHaveCount(0);
}

interface SeededCatalog {
  rootName: string;
  childName: string;
  otherRootName: string;
  brandName: string;
  otherBrandName: string;
  suffix: string;
}

// Tạo danh mục gốc có con, một danh mục gốc khác và hai thương hiệu bằng API admin. Tên có dấu và hậu tố riêng.
async function seedCatalog(page: Page): Promise<SeededCatalog> {
  const headers = { Origin: e2eEnvironment.webOrigin };
  const suffix = Date.now().toString(36);
  const names = {
    rootName: `Điện thoại Cây ${suffix}`,
    childName: `Smartphone Gập ${suffix}`,
    otherRootName: `Phụ kiện Khác ${suffix}`,
    brandName: `Hãng Xanh ${suffix}`,
    otherBrandName: `Hãng Đỏ ${suffix}`,
  };

  const root = await page.request.post('/api/admin/categories', {
    headers,
    data: { name: names.rootName, parentId: null, position: 0, attributes: [] },
  });
  expect(root.status(), await root.text()).toBe(201);
  const child = await page.request.post('/api/admin/categories', {
    headers,
    data: {
      name: names.childName,
      parentId: (await root.json()).data.id,
      position: 0,
      attributes: [],
    },
  });
  expect(child.status(), await child.text()).toBe(201);
  const otherRoot = await page.request.post('/api/admin/categories', {
    headers,
    data: { name: names.otherRootName, parentId: null, position: 0, attributes: [] },
  });
  expect(otherRoot.status(), await otherRoot.text()).toBe(201);

  for (const brandName of [names.brandName, names.otherBrandName]) {
    const brand = await page.request.post('/api/admin/brands', {
      headers,
      data: { name: brandName },
    });
    expect(brand.status(), await brand.text()).toBe(201);
  }

  return { ...names, suffix };
}

test('FLT-UI4: màn danh mục, thương hiệu và role tìm kiếm trên URL, không tràn trang', async ({
  page,
}, testInfo) => {
  test.setTimeout(120000);
  const consoleIssues = collectConsoleIssues(page);

  await loginAdmin(page);
  const seeded = await seedCatalog(page);
  const childKeyword = `smartphone gap ${seeded.suffix}`;

  await test.step('Gõ tên con không dấu: hiện con và danh mục gốc của nó, ẩn danh mục khác', async () => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/admin/categories');
    await expect(page.getByRole('heading', { level: 1, name: 'Quản lý danh mục' })).toBeVisible();

    const searchBox = page.getByLabel('Tìm theo tên hoặc slug danh mục', { exact: true });
    await searchBox.fill(childKeyword);
    // Ô tìm kiếm chờ 300 ms sau lần gõ cuối rồi mới ghi từ khóa lên URL.
    await expect(page).toHaveURL(/[?&]search=/);
    await waitForListLoaded(page);

    const tree = page.locator('.category-tree');
    await expect(tree.getByText(seeded.childName, { exact: true })).toBeVisible();
    await expect(tree.getByText(seeded.rootName, { exact: true })).toBeVisible();
    await expect(tree.getByText(seeded.otherRootName, { exact: true })).toHaveCount(0);

    await page.reload();
    await expect(searchBox).toHaveValue(childKeyword);
    await expect(tree.getByText(seeded.childName, { exact: true })).toBeVisible();
    await expect(tree.getByText(seeded.otherRootName, { exact: true })).toHaveCount(0);
  });

  await test.step('Màn thương hiệu lọc theo từ khóa trên link', async () => {
    await page.goto(`/admin/brands?search=${encodeURIComponent(`hang xanh ${seeded.suffix}`)}`);
    await waitForListLoaded(page);

    const rows = page.locator('table.tbl tbody tr');
    await expect(rows).toHaveCount(1);
    await expect(rows.first()).toContainText(seeded.brandName);
    await expect(page.getByText(seeded.otherBrandName)).toHaveCount(0);
  });

  await test.step('Màn role lọc theo mã role trên link', async () => {
    await page.goto('/admin/roles?search=sales');
    await waitForListLoaded(page);

    const codes = page.locator('table.identity-table tbody tr th .mono');
    await expect(codes.filter({ hasText: /^sales$/ })).toHaveCount(1);
    await expect(codes.filter({ hasText: /^admin$/ })).toHaveCount(0);
  });

  for (const width of [1280, 375]) {
    await test.step(`Ảnh chụp ba màn ở ${width}px không tràn ngang`, async () => {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      const screens = [
        {
          path: `/admin/categories?search=${encodeURIComponent(childKeyword)}`,
          name: 'categories',
        },
        {
          path: `/admin/brands?search=${encodeURIComponent(`hang xanh ${seeded.suffix}`)}`,
          name: 'brands',
        },
        { path: '/admin/roles?search=sales', name: 'roles' },
      ];
      for (const screen of screens) {
        await page.goto(screen.path);
        await waitForListLoaded(page);
        await expectNoHorizontalOverflow(page);
        await page.screenshot({
          path: testInfo.outputPath(`${screen.name}-search-${width}.png`),
          fullPage: true,
        });
      }
    });
  }

  expect(consoleIssues).toEqual([]);
});
