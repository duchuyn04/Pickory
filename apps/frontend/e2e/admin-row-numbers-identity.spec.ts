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
    await page.screenshot({ path: outputPath(`FLT-UI10-${screen}-${width}.png`), fullPage: true });
  }
  await page.setViewportSize({ width: 1280, height: 800 });
}

// Đọc `text-align` đã tính của một ô, để phép kiểm căn phải dựa trên kiểu thật của trình duyệt.
async function textAlignOf(cell: Locator) {
  return cell.evaluate((element) => getComputedStyle(element).textAlign);
}

// Kiểm một bảng có cột đầu là STT: tiêu đề `STT`, tiêu đề và ô đầu căn phải.
async function expectRowNumberColumn(table: Locator) {
  const headerCell = table.locator('thead th').first();
  await expect(headerCell).toHaveText('STT');
  expect(await textAlignOf(headerCell)).toBe('right');
  expect(await textAlignOf(table.locator('tbody tr').first().locator('td').first())).toBe('right');
  return headerCell;
}

test('FLT-UI10: màn khách hàng, nhân viên và nhật ký thao tác có cột STT', async ({
  page,
}, testInfo) => {
  test.setTimeout(180000);
  const consoleIssues = collectConsoleIssues(page);
  const headers = { Origin: e2eEnvironment.webOrigin };
  // Hậu tố riêng của nhân viên spec tạo. Email nhân viên chứa hậu tố này, nên lọc theo nó luôn ra đúng hai nhân viên của spec.
  const suffix = Date.now().toString(36);
  const outputPath = testInfo.outputPath.bind(testInfo);

  await loginAdmin(page);
  await page.setViewportSize({ width: 1280, height: 800 });

  await test.step('Tạo hai nhân viên bằng API', async () => {
    for (const letter of ['a', 'b']) {
      const invited = await page.request.post('/api/admin/staff/invitations', {
        headers,
        data: {
          email: `e2e-stt-staff-${letter}-${suffix}@pickory.local`,
          name: `Nhân viên STT ${letter.toUpperCase()}`,
          roleCodes: ['sales'],
        },
      });
      expect(invited.status(), await invited.text()).toBeLessThan(300);
    }
  });

  await test.step('Màn khách hàng: STT bắt đầu từ 1 và liền nhau', async () => {
    // Spec không tạo khách hàng, vì đăng ký thật bị giới hạn 5 lần mỗi giờ cho mỗi IP.
    // DB E2E luôn có khách seed, nên spec mở danh sách không lọc rồi kiểm STT liền nhau theo số dòng đang hiện.
    await page.goto('/admin/customers');
    const table = page.locator('table.identity-table');
    const rows = table.locator('tbody tr');
    // Chờ dòng đầu hiện ra. Dòng chữ nạp có thể chưa kịp xuất hiện, nên chỉ chờ chữ đó biến mất thì bảng vẫn có thể rỗng.
    await expect(rows.first()).toBeVisible();
    expect(await rows.count()).toBeGreaterThanOrEqual(2);

    const headerCell = await expectRowNumberColumn(table);
    const rowCount = await rows.count();
    const expected = Array.from({ length: rowCount }, (_, index) => String(index + 1));
    await expect(rows.locator('td:first-child')).toHaveText(expected);

    await captureBothWidths(page, 'customers', outputPath, headerCell);
  });

  await test.step('Màn nhân viên: lọc theo hậu tố riêng, STT 1 và 2', async () => {
    await page.goto('/admin/staff');
    const searchBox = page.getByLabel('Tìm theo tên hoặc email', { exact: true });
    const table = page.locator('table.identity-table');
    const rows = table.locator('tbody tr');
    await searchBox.fill(suffix);
    await searchBox.press('Enter');
    await expect(rows).toHaveCount(2);

    const headerCell = await expectRowNumberColumn(table);
    await expect(rows.locator('td:first-child')).toHaveText(['1', '2']);

    await captureBothWidths(page, 'staff', outputPath, headerCell);
  });

  await test.step('Màn nhật ký thao tác: STT bắt đầu từ 1 và liền nhau', async () => {
    // Bộ lọc của màn không cô lập được dữ liệu riêng của spec: các spec khác cũng ghi nhật ký vào cùng DB.
    // Nên spec chỉ kiểm dòng đầu có STT 1 và các STT liền nhau, theo số dòng đang hiện.
    await page.goto('/admin/audit-logs');
    const table = page.locator('table.audit-table');
    const rows = table.locator('tbody tr');
    // Chờ dòng đầu hiện ra, vì chờ chữ nạp biến mất vẫn có thể đọc bảng rỗng.
    await expect(rows.first()).toBeVisible();
    // Hai lời mời nhân viên của spec đã ghi ít nhất hai dòng nhật ký.
    expect(await rows.count()).toBeGreaterThanOrEqual(2);

    const headerCell = await expectRowNumberColumn(table);
    const rowCount = await rows.count();
    const expected = Array.from({ length: rowCount }, (_, index) => String(index + 1));
    await expect(rows.locator('td:first-child')).toHaveText(expected);

    await captureBothWidths(page, 'audit-logs', outputPath, headerCell);
  });

  expect(consoleIssues).toEqual([]);
});
