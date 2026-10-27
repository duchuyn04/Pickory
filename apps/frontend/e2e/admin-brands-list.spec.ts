import { expect, test, type Page } from '@playwright/test';
import { collectConsoleIssues, loginAdmin } from './catalog-admin-flow';
import { e2eEnvironment } from './environment';

const SEARCH_LABEL = 'Tìm theo tên hoặc slug thương hiệu';

// Bảng có thể cuộn trong khung riêng, nhưng toàn bộ tài liệu không được tràn ngang.
async function expectNoHorizontalOverflow(page: Page) {
  const widths = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth,
  }));
  expect(widths.content).toBeLessThanOrEqual(widths.viewport);
}

test('màn thương hiệu tìm kiếm, lọc, sắp xếp và phân trang trên URL', async ({
  page,
}, testInfo) => {
  test.setTimeout(120000);
  const consoleIssues = collectConsoleIssues(page);
  const suffix = Date.now().toString(36);
  const keyword = `Brand ${suffix}`;
  const brandNames = Array.from({ length: 12 }, (_, index) => {
    const number = String(index + 1).padStart(2, '0');
    return `${keyword} ${number}`;
  });
  const headers = { Origin: e2eEnvironment.webOrigin };

  await loginAdmin(page);
  for (const [index, name] of brandNames.entries()) {
    // Chỉ brand có số chẵn nhận logo để dữ liệu kiểm thử phủ cả hai lựa chọn của bộ lọc.
    const response = await page.request.post('/api/admin/brands', {
      headers,
      data: {
        name,
        logoPublicId: (index + 1) % 2 === 0 ? `pickory/brands/e2e-${suffix}-${index + 1}` : null,
      },
    });
    expect(response.status(), await response.text()).toBe(201);
  }

  await page.goto('/admin/brands');
  const searchBox = page.getByLabel(SEARCH_LABEL, { exact: true });
  const rows = page.locator('table.tbl tbody tr');
  const pager = page.getByRole('navigation', { name: 'Phân trang thương hiệu' });

  await searchBox.fill(keyword);
  await searchBox.press('Enter');
  await expect.poll(() => new URL(page.url()).searchParams.get('search')).toBe(keyword);
  await expect(rows).toHaveCount(10);
  await expect(rows.locator('strong').first()).toHaveText(brandNames[0]!);
  await expect(rows.locator('td:first-child').last()).toHaveText('10');
  await expect(pager).toContainText('Trang 1 / 2');

  await pager.getByRole('button', { name: 'Trang sau' }).click();
  await expect(page).toHaveURL(/[?&]page=2(&|$)/);
  await expect(rows).toHaveCount(2);
  await expect(rows.locator('strong')).toHaveText(brandNames.slice(10));
  await expect(rows.locator('td:first-child')).toHaveText(['11', '12']);

  // Tải lại trang để chứng minh URL giữ đúng bộ lọc, thứ tự và vị trí danh sách.
  await page.reload();
  await expect(searchBox).toHaveValue(keyword);
  await expect(rows.locator('strong')).toHaveText(brandNames.slice(10));
  await expect(rows.locator('td:first-child')).toHaveText(['11', '12']);

  await page.getByLabel('Logo', { exact: true }).selectOption('with-logo');
  await expect(page).toHaveURL(/[?&]logo=with-logo(&|$)/);
  await expect(page).not.toHaveURL(/[?&]page=/);
  const namesWithLogo = brandNames.filter((_, index) => (index + 1) % 2 === 0);
  await expect(rows.locator('strong')).toHaveText(namesWithLogo);
  await expect(page.getByLabel('Logo', { exact: true })).toHaveValue('with-logo');
  await expect(page.getByText('6 thương hiệu', { exact: true })).toBeVisible();

  await page.getByLabel('Sắp xếp', { exact: true }).selectOption('name-desc');
  await expect(page).toHaveURL(/[?&]sort=name-desc(&|$)/);
  await expect(rows.locator('strong')).toHaveText([...namesWithLogo].reverse());

  await page.getByRole('button', { name: 'Xóa bộ lọc', exact: true }).click();
  await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/brands`);
  await expect(searchBox).toHaveValue('');
  await expect(page.getByLabel('Logo', { exact: true })).toHaveValue('');
  await expect(page.getByLabel('Sắp xếp', { exact: true })).toHaveValue('');
  await expect(rows).toHaveCount(10);

  await page.goto(`/admin/brands?search=${encodeURIComponent(keyword)}&page=99`);
  await expect(rows).toHaveCount(0);
  await page.getByRole('button', { name: 'Về trang 1', exact: true }).click();
  await expect(page).not.toHaveURL(/[?&]page=/);
  await expect(rows).toHaveCount(10);

  await page.getByLabel('Logo', { exact: true }).focus();
  await page.keyboard.press('Tab');
  await expect(page.getByLabel('Sắp xếp', { exact: true })).toBeFocused();

  for (const width of [320, 768, 1024, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    await expect(searchBox).toBeVisible();
    await expectNoHorizontalOverflow(page);
    await page.screenshot({
      path: testInfo.outputPath(`brands-list-${width}.png`),
      fullPage: true,
    });
  }

  expect(consoleIssues).toEqual([]);
});
