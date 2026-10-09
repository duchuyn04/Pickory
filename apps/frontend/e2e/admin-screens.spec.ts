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

// Mỗi màn admin của DS-04 cùng tiêu đề dùng để chờ màn tải xong trước khi đo và chụp.
const adminScreens = [
  { path: '/admin', heading: /Chào mừng trở lại/, name: 'dashboard' },
  { path: '/admin/customers', heading: 'Quản lý Khách hàng', name: 'customers' },
  { path: '/admin/staff', heading: 'Quản lý Nhân viên', name: 'staff' },
  { path: '/admin/roles', heading: 'Quản lý Role & Phân quyền', name: 'roles' },
  { path: '/admin/audit-logs', heading: 'Nhật ký thao tác (Audit Logs)', name: 'audit-logs' },
  { path: '/admin/ai', heading: 'Cấu hình và sử dụng AI', name: 'ai' },
];

test('DS-04: màn admin identity và AI không tràn trang ở 1280px và 375px', async ({
  page,
}, testInfo) => {
  test.setTimeout(120000);
  // Ghi lỗi console và lỗi trang. Mọi màn trong spec phải có 0 lỗi và 0 cảnh báo.
  const consoleIssues: string[] = [];
  page.on('console', (message) => {
    // Bỏ qua lỗi tải `favicon.ico`. Chromium có giao diện tự gọi tệp này, còn ứng dụng chưa có favicon.
    if (message.location().url.endsWith('/favicon.ico')) return;
    if (message.type() === 'error' || message.type() === 'warning') {
      consoleIssues.push(`[${message.type()}] ${message.text()}`);
    }
  });
  page.on('pageerror', (error) => consoleIssues.push(`[pageerror] ${error.message}`));

  await test.step('Màn đăng nhập nhân viên ở 375px và 1280px', async () => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto('/admin/login');
    const consent = page.getByRole('button', { name: 'Chỉ cần thiết', exact: true });
    if (await consent.isVisible()) await consent.click();
    await expect(page.getByRole('heading', { name: 'Pickory Back-Office' })).toBeVisible();
    await expectNoHorizontalOverflow(page);
    await page.screenshot({ path: testInfo.outputPath('admin-login-mobile.png'), fullPage: true });

    // Sai mật khẩu phải hiện lỗi trong vùng `role="alert"` để trình đọc màn hình đọc ngay.
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.getByLabel(/^Email/).fill(e2eEnvironment.adminEmail);
    await page.getByLabel('Mật khẩu', { exact: true }).fill('mat-khau-sai-e2e');
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
    await expect(page.getByRole('alert')).toBeVisible();
    await page.screenshot({ path: testInfo.outputPath('admin-login-desktop.png'), fullPage: true });
    // Trình duyệt ghi lỗi 401 của lần đăng nhập sai vào console. Chỉ bỏ dòng này, giữ mọi lỗi khác.
    const unexpectedIssues = consoleIssues.filter((issue) => !issue.includes('status of 401'));
    consoleIssues.splice(0, consoleIssues.length, ...unexpectedIssues);

    await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.adminPassword);
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin`);
  });

  for (const width of [1280, 375]) {
    await test.step(`Các màn admin ở ${width}px`, async () => {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      for (const screen of adminScreens) {
        await page.goto(screen.path);
        await expect(page.getByRole('heading', { level: 1, name: screen.heading })).toBeVisible();
        // Chờ trạng thái đang tải biến mất, để ảnh và số đo phản ánh nội dung thật.
        await expect(page.getByRole('status')).toHaveCount(0);
        await expectNoHorizontalOverflow(page);
        await page.screenshot({
          path: testInfo.outputPath(`${screen.name}-${width}.png`),
          fullPage: true,
        });
      }
    });
  }

  await test.step('Hộp thoại mời và form role không tràn ở 375px', async () => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto('/admin/staff');
    await page.getByRole('button', { name: '+ Mời nhân viên', exact: true }).click();
    const inviteDialog = page.getByRole('dialog', { name: 'Mời nhân viên mới' });
    await expect(inviteDialog).toBeVisible();
    // Nhóm vai trò có tên truy cập từ `legend`, để trình đọc màn hình đọc đúng câu hỏi.
    await expect(inviteDialog.getByRole('group', { name: 'Vai trò (Role) *' })).toBeVisible();
    await expectNoHorizontalOverflow(page);
    await page.screenshot({ path: testInfo.outputPath('invite-dialog-375.png') });
    // Focus nằm trong hộp thoại khi mở. Escape đóng hộp thoại và trả focus về nút đã mở nó.
    const focusInsideDialog = await inviteDialog.evaluate((dialog) =>
      dialog.contains(document.activeElement),
    );
    expect(focusInsideDialog).toBe(true);
    await page.keyboard.press('Escape');
    await expect(inviteDialog).toBeHidden();
    await expect(page.getByRole('button', { name: '+ Mời nhân viên', exact: true })).toBeFocused();

    await page.goto('/admin/roles');
    await page.getByRole('button', { name: '+ Thêm role mới', exact: true }).click();
    const roleDialog = page.getByRole('dialog', { name: 'Thêm Role Mới' });
    await expect(roleDialog).toBeVisible();
    // Lưới quyền không được đẩy hộp thoại rộng hơn khung nhìn.
    const roleDialogWidths = await roleDialog.evaluate((dialog) => ({
      scroll: dialog.scrollWidth,
      client: dialog.clientWidth,
    }));
    expect(roleDialogWidths.scroll).toBeLessThanOrEqual(roleDialogWidths.client);
    await page.screenshot({ path: testInfo.outputPath('role-dialog-375.png') });
    await roleDialog.getByRole('button', { name: 'Đóng', exact: true }).click();
  });

  expect(consoleIssues).toEqual([]);
});
