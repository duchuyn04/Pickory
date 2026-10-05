import { expect, test } from '@playwright/test';
import { e2eEnvironment } from './environment';

test('Luồng 1: khách đăng ký, đăng xuất và đăng nhập lại', async ({ page }, testInfo) => {
  const customerName = 'Khách E2E';

  await test.step('Đăng ký tài khoản khách bằng giao diện', async () => {
    // Chọn cookie cần thiết để banner không che form.
    await page.goto('/register');
    await page.getByRole('button', { name: 'Chỉ cần thiết', exact: true }).click();
    // Dùng cùng email để phát hiện dữ liệu còn từ lần chạy trước.
    await page.getByLabel(/^Email/).fill(e2eEnvironment.customerEmail);
    await page.getByLabel(/^Mật khẩu/).fill(e2eEnvironment.accountPassword);
    await page.getByLabel(/^Họ và tên/).fill(customerName);
    await page.getByRole('button', { name: 'Tạo tài khoản', exact: true }).click();
    // Link tài khoản xác nhận actor đã chuyển thành customer.
    await expect(page.getByRole('link', { name: 'Tài khoản', exact: true })).toBeVisible();
  });

  await test.step('Đăng xuất và xác nhận trạng thái chưa đăng nhập', async () => {
    // Kiểm tra UI đã bỏ quyền truy cập tài khoản sau đăng xuất.
    await page.getByRole('button', { name: 'Đăng xuất', exact: true }).click();
    await expect(page.getByRole('link', { name: 'Tài khoản', exact: true })).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'Đăng nhập', exact: true })).toBeVisible();
  });

  await test.step('Đăng nhập lại bằng credential vừa đăng ký', async () => {
    // Dùng link của header để kiểm tra điều hướng tới form đăng nhập.
    await page.getByRole('link', { name: 'Đăng nhập', exact: true }).click();
    await page.getByLabel('Email', { exact: true }).fill(e2eEnvironment.customerEmail);
    await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.accountPassword);
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
    await page.getByRole('link', { name: 'Tài khoản', exact: true }).click();
    // Kiểm tra tên đã lưu để xác nhận phiên mới thuộc đúng customer.
    await expect(
      page.getByRole('heading', { name: 'Tài khoản của tôi', exact: true }),
    ).toBeVisible();
    await expect(page.getByLabel('Họ và tên', { exact: true })).toHaveValue(customerName);
  });

  // Lưu ảnh cuối luồng để kiểm tra UI thực tế. File nằm ngoài Git.
  await page.screenshot({ path: testInfo.outputPath('customer-after-login.png'), fullPage: true });
});
