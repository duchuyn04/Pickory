import { randomBytes } from 'node:crypto';
import { expect, test } from '@playwright/test';
import { e2eEnvironment } from './environment';
import { readEmailVerificationLink, readPasswordResetLink } from './outbox';

// Vòng đời tài khoản qua giao diện thật: đăng ký, xác thực email, quên mật khẩu, đặt lại mật khẩu và đổi mật khẩu.
test('khách đăng ký, xác thực email, quên mật khẩu, đặt lại và đổi mật khẩu', async ({ page }) => {
  const consoleIssues: string[] = [];
  page.on('console', (message) => {
    if (message.location().url.endsWith('/favicon.ico')) return;
    if (message.type() === 'error' || message.type() === 'warning') {
      consoleIssues.push(`[${message.type()}] ${message.text()}`);
    }
  });
  page.on('pageerror', (error) => consoleIssues.push(`[pageerror] ${error.message}`));

  // Email riêng cho test này, để thư outbox của test khác không lẫn vào.
  const email = `e2e-lifecycle-${randomBytes(4).toString('hex')}@pickory.local`;
  const resetPassword = 'LifecycleReset123!';
  const changedPassword = 'LifecycleChanged123!';

  await test.step('Đăng ký khách bằng giao diện', async () => {
    await page.goto('/register');
    await page.getByRole('button', { name: 'Chỉ cần thiết', exact: true }).click();
    await page.getByLabel(/^Email/).fill(email);
    await page.getByLabel(/^Mật khẩu/).fill(e2eEnvironment.accountPassword);
    await page.getByLabel(/^Họ và tên/).fill('Khách vòng đời');
    // Chờ trang chủ tra slug xong để lỗi tải chi tiết không xuất hiện sau khi test rời trang.
    const homeDetails = page.waitForResponse((response) =>
      response.url().includes('/api/product-details?'),
    );
    await page.getByRole('button', { name: 'Tạo tài khoản', exact: true }).click();
    // Link Tài khoản chứng minh API đăng ký đã trả phiên của khách.
    await expect(page.getByRole('link', { name: 'Tài khoản', exact: true })).toBeVisible();
    expect((await homeDetails).status()).toBe(200);
    await page.waitForLoadState('networkidle');
  });

  await test.step('Mở link xác thực email trong thư', async () => {
    const verifyLink = await readEmailVerificationLink(email);
    await page.goto(verifyLink);
    await expect(
      page.getByText('Địa chỉ email của bạn đã được xác thực thành công.'),
    ).toBeVisible();
  });

  await test.step('Đăng xuất rồi gửi yêu cầu quên mật khẩu', async () => {
    await page.getByRole('button', { name: 'Đăng xuất', exact: true }).click();
    await expect(page.getByRole('link', { name: 'Đăng nhập', exact: true })).toBeVisible();
    await page.goto('/forgot-password');
    await page.getByLabel(/^Email/).fill(email);
    await page.getByRole('button', { name: 'Gửi liên kết đặt lại', exact: true }).click();
    await expect(
      page.getByText('Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được gửi'),
    ).toBeVisible();
  });

  await test.step('Đặt lại mật khẩu bằng link trong thư', async () => {
    const resetLink = await readPasswordResetLink(email);
    await page.goto(resetLink);
    await page.getByLabel('Mật khẩu mới (tối thiểu 8 ký tự)', { exact: true }).fill(resetPassword);
    await page.getByLabel('Xác nhận mật khẩu mới', { exact: true }).fill(resetPassword);
    await page.getByRole('button', { name: 'Cập nhật mật khẩu', exact: true }).click();
    await expect(page.getByText('Mật khẩu của bạn đã được cập nhật thành công!')).toBeVisible();
  });

  await test.step('Đăng nhập bằng mật khẩu vừa đặt', async () => {
    await page.getByRole('link', { name: 'Đăng nhập ngay', exact: true }).click();
    await page.getByLabel(/^Email/).fill(email);
    await page.getByLabel('Mật khẩu', { exact: true }).fill(resetPassword);
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
    await expect(page.getByRole('link', { name: 'Tài khoản', exact: true })).toBeVisible();
  });

  await test.step('Đổi mật khẩu ở trang tài khoản rồi đăng nhập lại', async () => {
    await page.goto('/me');
    await page.getByLabel('Mật khẩu hiện tại', { exact: true }).fill(resetPassword);
    await page
      .getByLabel('Mật khẩu mới (tối thiểu 8 ký tự)', { exact: true })
      .fill(changedPassword);
    await page.getByLabel('Xác nhận mật khẩu mới', { exact: true }).fill(changedPassword);
    await page.getByRole('button', { name: 'Cập nhật mật khẩu', exact: true }).click();
    // Server thu hồi mọi phiên sau khi đổi, nên trang tài khoản mất actor và giao diện chuyển về trang đăng nhập.
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/login`);

    await page.getByLabel(/^Email/).fill(email);
    await page.getByLabel('Mật khẩu', { exact: true }).fill(changedPassword);
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
    await expect(page.getByRole('link', { name: 'Tài khoản', exact: true })).toBeVisible();
  });

  // Luồng không có lỗi console hoặc cảnh báo ở mọi trang đã đi qua.
  expect(consoleIssues).toEqual([]);
});
