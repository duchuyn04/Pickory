import { expect, test } from '@playwright/test';
import { e2eEnvironment } from './environment';
import { readPasswordSetupLink } from './outbox';

test('Luồng 2: admin mời sales, đặt mật khẩu và đăng nhập đúng quyền', async ({
  page,
}, testInfo) => {
  // Ghi lỗi console và lỗi trang. Cả luồng mời và đăng nhập phải có 0 lỗi và 0 cảnh báo.
  const consoleIssues: string[] = [];
  page.on('console', (message) => {
    // Bỏ qua lỗi tải `favicon.ico`. Chromium có giao diện tự gọi tệp này, còn ứng dụng chưa có favicon.
    if (message.location().url.endsWith('/favicon.ico')) return;
    if (message.type() === 'error' || message.type() === 'warning') {
      consoleIssues.push(`[${message.type()}] ${message.text()}`);
    }
  });
  page.on('pageerror', (error) => consoleIssues.push(`[pageerror] ${error.message}`));

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
    // Chỉ lấy menu quản trị (`aria-label` là `Quản trị`). Link trong `<nav>` khác, như phân trang, không làm sai kết quả.
    const navigation = page.getByRole('navigation', { name: 'Quản trị', exact: true });
    // Danh sách lấy từ `ADMIN_NAV_GROUPS` luôn khớp menu đang chạy, nên test không bắt được lỗi quyền. Vì vậy danh sách này viết tay.
    // `sales` có `reports:read` trong bảng quyền mặc định, nên ba mục báo cáo nằm trong danh sách được thấy.
    const salesMenuLabels = [
      'Tổng quan',
      'Đơn hàng',
      'Đối soát',
      'Khách hàng',
      'Sản phẩm',
      'Danh mục',
      'Thương hiệu',
      'Báo cáo tổng quan',
      'Doanh thu thương hiệu',
      'Doanh thu theo kỳ',
    ];
    // `sales` không có quyền mặc định cho các trang này. Thấy mục nào ở đây nghĩa là menu hiện trang của quyền `sales` không có.
    const salesHiddenMenuLabels = [
      'CSKH',
      'Đánh giá',
      'Nhân viên',
      'Quản lý Role',
      'Vận chuyển',
      'Phí ship dự phòng',
      'Cấu hình bán hàng',
      'Nhật ký thao tác',
    ];
    // Đây là các mục `sales` phải thấy theo quyền mặc định. Thiếu mục nào nghĩa là menu ẩn nhầm một trang `sales` được dùng.
    for (const label of salesMenuLabels) {
      await expect(navigation.getByRole('link', { name: label, exact: true })).toBeVisible();
    }
    for (const label of salesHiddenMenuLabels) {
      await expect(navigation.getByRole('link', { name: label, exact: true })).toHaveCount(0);
    }
    // Allowlist bắt mục menu mới hiện cho `sales` mà không kiểm quyền. Link nào ngoài `salesMenuLabels` làm test đỏ.
    for (const label of await navigation.getByRole('link').allTextContents()) {
      expect(salesMenuLabels).toContain(label);
    }
  });

  // Lưu menu thực tế ngoài Git để có bằng chứng quyền của `sales`.
  await page.screenshot({ path: testInfo.outputPath('sales-menu.png'), fullPage: true });

  expect(consoleIssues).toEqual([]);
});
