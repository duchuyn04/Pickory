import { expect, test, type Locator, type Page } from '@playwright/test';
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
    await page.screenshot({ path: outputPath(`FLT-UI9-${screen}-${width}.png`), fullPage: true });
  }
  await page.setViewportSize({ width: 1280, height: 800 });
}

// Đọc `text-align` đã tính của một ô, để phép kiểm căn phải dựa trên kiểu thật của trình duyệt.
async function textAlignOf(cell: Locator) {
  return cell.evaluate((element) => getComputedStyle(element).textAlign);
}

// Từ khóa riêng của spec. Mọi dữ liệu spec tạo đều chứa hậu tố này, nên lọc theo nó luôn ra đúng dữ liệu của spec.
function makeSuffix() {
  return Date.now().toString(36);
}

test('FLT-UI9: màn thương hiệu, vai trò và danh mục đánh STT theo danh sách đang hiện', async ({
  page,
}, testInfo) => {
  test.setTimeout(180000);
  const consoleIssues = collectConsoleIssues(page);
  const headers = { Origin: e2eEnvironment.webOrigin };
  const suffix = makeSuffix();
  const outputPath = testInfo.outputPath.bind(testInfo);

  await loginAdmin(page);
  await page.setViewportSize({ width: 1280, height: 800 });

  await test.step('Màn thương hiệu: ba dòng STT 1, 2, 3, lọc còn một dòng thì STT 1', async () => {
    for (const label of ['Alpha', 'Beta', 'Gamma']) {
      const created = await page.request.post('/api/admin/brands', {
        headers,
        data: { name: `Hãng STT ${suffix} ${label}` },
      });
      expect(created.status(), await created.text()).toBe(201);
    }

    await page.goto('/admin/brands');
    const searchBox = page.getByLabel('Tìm theo tên hoặc slug thương hiệu', { exact: true });
    const rows = page.locator('table.tbl tbody tr');
    await searchBox.fill(`hang stt ${suffix}`);
    await searchBox.press('Enter');
    await expect(rows).toHaveCount(3);

    const headerCell = page.locator('table.tbl thead th').first();
    await expect(headerCell).toHaveText('STT');
    expect(await textAlignOf(headerCell)).toBe('right');
    await expect(rows.locator('td:first-child')).toHaveText(['1', '2', '3']);
    expect(await textAlignOf(rows.first().locator('td').first())).toBe('right');

    await captureBothWidths(page, 'brands', outputPath, headerCell);

    // Từ khóa dài hơn chỉ khớp một thương hiệu. Dòng còn lại đánh lại từ 1.
    await searchBox.fill(`hang stt ${suffix} gamma`);
    await searchBox.press('Enter');
    await expect(rows).toHaveCount(1);
    await expect(rows.first().locator('td').first()).toHaveText('1');
    await expect(rows.first()).toContainText(`Hãng STT ${suffix} Gamma`);
  });

  await test.step('Màn vai trò: hai dòng STT 1, 2, lọc còn một dòng thì STT 1', async () => {
    for (const letter of ['a', 'b']) {
      const created = await page.request.post('/api/admin/roles', {
        headers,
        data: {
          code: `stt-${letter}-${suffix}`,
          name: `Vai trò STT ${suffix} ${letter.toUpperCase()}`,
          permissions: [],
        },
      });
      expect(created.status(), await created.text()).toBe(201);
    }

    await page.goto('/admin/roles');
    const searchBox = page.getByLabel('Tìm theo tên hoặc mã role', { exact: true });
    const rows = page.locator('table.identity-table tbody tr');
    await searchBox.fill(`vai tro stt ${suffix}`);
    await searchBox.press('Enter');
    await expect(rows).toHaveCount(2);

    const headerCell = page.locator('table.identity-table thead th').first();
    await expect(headerCell).toHaveText('STT');
    expect(await textAlignOf(headerCell)).toBe('right');
    await expect(rows.locator('td:first-child')).toHaveText(['1', '2']);
    expect(await textAlignOf(rows.first().locator('td').first())).toBe('right');

    await captureBothWidths(page, 'roles', outputPath, headerCell);

    // Từ khóa dài hơn chỉ khớp vai trò B. Dòng còn lại đánh lại từ 1.
    await searchBox.fill(`vai tro stt ${suffix} b`);
    await searchBox.press('Enter');
    await expect(rows).toHaveCount(1);
    await expect(rows.first().locator('td').first()).toHaveText('1');
    await expect(rows.first()).toContainText(`stt-b-${suffix}`);
  });

  await test.step('Màn danh mục: gốc hiện 1, con hiện 1.1 và 1.2, lọc theo con thứ hai thì con đó hiện 1.1', async () => {
    const root = await page.request.post('/api/admin/categories', {
      headers,
      data: { name: `Gốc STT ${suffix}`, parentId: null, position: 0, attributes: [] },
    });
    expect(root.status(), await root.text()).toBe(201);
    const rootId = (await root.json()).data.id;
    // Vị trí 1 và 2 cố định thứ tự hai con, nên con B luôn là con thứ hai.
    for (const [letter, position] of [
      ['A', 1],
      ['B', 2],
    ] as const) {
      const child = await page.request.post('/api/admin/categories', {
        headers,
        data: {
          name: `Con STT ${suffix} ${letter}`,
          parentId: rootId,
          position,
          attributes: [],
        },
      });
      expect(child.status(), await child.text()).toBe(201);
    }

    await page.goto('/admin/categories');
    const searchBox = page.getByLabel('Tìm theo tên hoặc slug danh mục', { exact: true });
    const numbers = page.locator('.category-number');
    await searchBox.fill(`goc stt ${suffix}`);
    await searchBox.press('Enter');
    await expect(numbers).toHaveText(['1', '1.1', '1.2']);
    await expect(page.locator('.category-node-main strong')).toHaveText([
      `Gốc STT ${suffix}`,
      `Con STT ${suffix} A`,
      `Con STT ${suffix} B`,
    ]);

    await captureBothWidths(page, 'categories', outputPath, numbers.first());

    // Từ khóa chỉ khớp con thứ hai. Gốc được giữ làm khung nên vẫn hiện 1, con đó hiện 1.1.
    await searchBox.fill(`con stt ${suffix} b`);
    await searchBox.press('Enter');
    await expect(numbers).toHaveText(['1', '1.1']);
    await expect(page.locator('.category-node-main strong')).toHaveText([
      `Gốc STT ${suffix}`,
      `Con STT ${suffix} B`,
    ]);
  });

  expect(consoleIssues).toEqual([]);
});
