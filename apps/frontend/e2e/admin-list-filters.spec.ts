import { expect, test, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';

// Trang không được rộng hơn khung nhìn. Bảng rộng phải cuộn trong khung riêng của nó.
async function expectNoHorizontalOverflow(page: Page) {
  const widths = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth,
  }));
  expect(widths.content).toBeLessThanOrEqual(widths.viewport);
}

async function loginAsAdmin(page: Page) {
  await page.goto('/admin/login');
  const consent = page.getByRole('button', { name: 'Chỉ cần thiết', exact: true });
  if (await consent.isVisible()) await consent.click();
  await page.getByLabel(/^Email/).fill(e2eEnvironment.adminEmail);
  await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.adminPassword);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin`);
}

// Chờ trạng thái đang tải biến mất. Thông báo rỗng của bộ lọc cũng dùng `role="status"` nên chỉ đếm dòng nạp.
async function waitForListLoaded(page: Page) {
  await expect(page.getByText(/^Đang nạp danh sách/)).toHaveCount(0);
}

test('FLT-UI2: màn khách hàng và nhân viên giữ bộ lọc trên URL, không tràn trang', async ({
  page,
}, testInfo) => {
  test.setTimeout(120000);
  // Ghi lỗi console và lỗi trang. Cả spec phải có 0 lỗi và 0 cảnh báo.
  const consoleIssues: string[] = [];
  page.on('console', (message) => {
    // Bỏ qua lỗi tải `favicon.ico`. Chromium có giao diện tự gọi tệp này, còn ứng dụng chưa có favicon.
    if (message.location().url.endsWith('/favicon.ico')) return;
    if (message.type() === 'error' || message.type() === 'warning') {
      consoleIssues.push(`[${message.type()}] ${message.text()}`);
    }
  });
  page.on('pageerror', (error) => consoleIssues.push(`[pageerror] ${error.message}`));

  await loginAsAdmin(page);

  await test.step('Từ khóa khách hàng nằm trên URL và còn nguyên sau khi tải lại', async () => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/admin/customers');
    await expect(page.getByRole('heading', { level: 1, name: 'Quản lý Khách hàng' })).toBeVisible();

    const searchBox = page.getByLabel('Tìm theo tên, email hoặc số điện thoại', { exact: true });
    await searchBox.fill('khach');
    // Ô tìm kiếm chờ 300 ms sau lần gõ cuối rồi mới ghi từ khóa lên URL.
    await expect(page).toHaveURL(/[?&]search=khach(&|$)/);
    await waitForListLoaded(page);
    const rows = page.locator('table.identity-table tbody tr');
    const rowsBeforeReload = await rows.allInnerTexts();

    await page.reload();
    await expect(searchBox).toHaveValue('khach');
    await waitForListLoaded(page);
    // Cùng từ khóa phải cho cùng danh sách dòng, theo cùng thứ tự.
    await expect.poll(() => rows.allInnerTexts()).toEqual(rowsBeforeReload);
  });

  await test.step('Bộ lọc trạng thái nhân viên đã mời chỉ hiện dòng Đã mời hoặc thông báo rỗng', async () => {
    await page.goto('/admin/staff?status=invited');
    await expect(page.getByRole('heading', { level: 1, name: 'Quản lý Nhân viên' })).toBeVisible();
    await expect(page.getByLabel('Trạng thái', { exact: true })).toHaveValue('invited');
    await waitForListLoaded(page);

    const emptyNotice = page.getByText('Không có kết quả phù hợp với bộ lọc.');
    if (await emptyNotice.isVisible()) {
      await expect(page.locator('table.identity-table')).toHaveCount(0);
    } else {
      // Cột trạng thái là ô thứ ba của mỗi dòng.
      const statusCells = page.locator('table.identity-table tbody tr td:nth-child(3)');
      expect(await statusCells.count()).toBeGreaterThan(0);
      for (const text of await statusCells.allInnerTexts()) {
        expect(text.trim()).toBe('Đã mời');
      }
    }
  });

  for (const width of [1280, 375]) {
    await test.step(`Ảnh chụp hai màn ở ${width}px không tràn ngang`, async () => {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      for (const screen of [
        { path: '/admin/customers?status=active', name: 'customers' },
        { path: '/admin/staff?status=active', name: 'staff' },
      ]) {
        await page.goto(screen.path);
        await expect(page.getByLabel('Trạng thái', { exact: true })).toHaveValue('active');
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
