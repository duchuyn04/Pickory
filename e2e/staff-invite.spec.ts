import { expect, test } from '@playwright/test';
import { e2eEnvironment } from './environment';
import { readPasswordSetupLink } from './outbox';

test('Luồng 2: admin mời sales, đặt mật khẩu và đăng nhập đúng quyền', async ({
  page,
}, testInfo) => {
  await test.step('Admin đăng nhập và gửi lời mời sales', async () => {
    // Dùng UI đăng nhập thật để kiểm tra phiên admin đã seed.
    await page.goto('/admin/login');
    await page.getByRole('button', { name: 'Chỉ cần thiết', exact: true }).click();
    await page.getByLabel(/^Email/).fill(e2eEnvironment.adminEmail);
    await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.adminPassword);
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
    await page.getByRole('link', { name: 'Nhân viên', exact: true }).click();
    // Gán role qua hộp thoại để kiểm tra payload và quyền mời của app.
    await page.getByRole('button', { name: '+ Mời nhân viên', exact: true }).click();
    const invitation = page.getByRole('dialog', { name: 'Mời nhân viên mới', exact: true });
    await invitation.getByLabel(/^Email nhân viên/).fill(e2eEnvironment.salesEmail);
    await invitation.getByLabel(/^Họ và tên/).fill('Nhân viên sales E2E');
    await invitation.getByRole('checkbox', { name: /\(sales\)$/ }).check();
    await invitation.getByRole('button', { name: 'Gửi lời mời', exact: true }).click();
    // Hộp thoại chỉ đóng sau khi API đã nhận lời mời.
    await expect(invitation).toBeHidden();
  });

  await test.step('Nhân viên mở link outbox và đặt mật khẩu', async () => {
    // Lấy link từ email thật của adapter dev. Không gọi API tạo token riêng cho test.
    const setupLink = await readPasswordSetupLink(e2eEnvironment.salesEmail);
    // Xóa phiên admin bằng UI trước khi mở link của nhân viên.
    await page.getByRole('button', { name: 'Đăng xuất', exact: true }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/login`);
    await page.goto(setupLink);
    await page
      .getByLabel('Mật khẩu mới (tối thiểu 8 ký tự)', { exact: true })
      .fill(e2eEnvironment.accountPassword);
    await page
      .getByLabel('Xác nhận mật khẩu mới', { exact: true })
      .fill(e2eEnvironment.accountPassword);
    await page.getByRole('button', { name: 'Cập nhật mật khẩu', exact: true }).click();
    // Link riêng cho staff xác nhận token mời đã tạo tài khoản nhân viên.
    await page.getByRole('link', { name: 'Đăng nhập trang quản trị', exact: true }).click();
  });

  await test.step('Sales đăng nhập và chỉ thấy menu trong quyền', async () => {
    // Dùng mật khẩu vừa đặt để xác nhận tài khoản mời hoạt động.
    await page.getByLabel(/^Email/).fill(e2eEnvironment.salesEmail);
    await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.accountPassword);
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
    const navigation = page.getByRole('navigation');
    // Kiểm tra menu hiện có. Hoãn yêu cầu thấy Đơn hàng tới commerce/admin-console.
    for (const label of ['Tổng quan', 'Sản phẩm', 'Khách hàng', 'Cấu hình AI']) {
      await expect(navigation.getByRole('link', { name: label, exact: true })).toBeVisible();
    }
    // Sales không có các quyền này trong policy mặc định.
    for (const label of ['CSKH', 'Nhân viên', 'Quản lý Role', 'Nhật ký thao tác']) {
      await expect(navigation.getByRole('link', { name: label, exact: true })).toHaveCount(0);
    }
    // Allowlist độc lập với NAV_ITEMS để bắt cả menu trái quyền được thêm sau này.
    const allowedLabels = ['Tổng quan', 'Đơn hàng', 'Sản phẩm', 'Khách hàng', 'Cấu hình AI'];
    for (const label of await navigation.getByRole('link').allTextContents()) {
      expect(allowedLabels).toContain(label);
    }
  });

  // Lưu menu thực tế ngoài Git để có bằng chứng quyền của sales.
  await page.screenshot({ path: testInfo.outputPath('sales-menu.png'), fullPage: true });
});
