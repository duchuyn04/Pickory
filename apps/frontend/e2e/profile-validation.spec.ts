import { randomBytes } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';

// Thư mục lưu ảnh chụp khi reviewer cần xem. Không đặt biến này thì spec không ghi ảnh.
const screenshotDirectory = process.env.PROFILE_VAL_SHOTS_DIR;

async function saveScreenshot(page: Page, fileName: string) {
  if (!screenshotDirectory) return;
  await page.screenshot({ path: `${screenshotDirectory}/${fileName}`, fullPage: true });
}

// Ghi mọi request ghi hồ sơ. Test dùng danh sách này để chứng minh dữ liệu sai không gọi API.
function recordProfileWrites(page: Page): string[] {
  const writes: string[] = [];
  page.on('request', (request) => {
    const isProfileWrite =
      (request.method() === 'PATCH' && request.url().endsWith('/api/me')) ||
      (request.method() === 'POST' && request.url().endsWith('/api/auth/registrations'));
    if (isProfileWrite) writes.push(`${request.method()} ${request.url()}`);
  });
  return writes;
}

test('số điện thoại và tên sai hiện lỗi dưới ô, không gọi API', async ({ page }) => {
  const consoleIssues: string[] = [];
  page.on('console', (message) => {
    if (message.location().url.endsWith('/favicon.ico')) return;
    if (message.type() === 'error' || message.type() === 'warning') {
      consoleIssues.push(`[${message.type()}] ${message.text()}`);
    }
  });
  page.on('pageerror', (error) => consoleIssues.push(`[pageerror] ${error.message}`));
  const profileWrites = recordProfileWrites(page);
  const email = `e2e-profile-val-${randomBytes(4).toString('hex')}@pickory.local`;

  for (const viewport of [
    { width: 1280, height: 800 },
    { width: 375, height: 812 },
  ]) {
    await test.step(`Trang đăng ký ở ${viewport.width}px`, async () => {
      await page.setViewportSize(viewport);
      await page.goto('/register');
      const consent = page.getByRole('button', { name: 'Chỉ cần thiết', exact: true });
      if (await consent.isVisible()) await consent.click();

      await page.getByLabel(/^Email/).fill(email);
      await page.getByLabel(/^Mật khẩu/).fill(e2eEnvironment.accountPassword);
      await page.getByLabel(/^Họ và tên/).fill('a'.repeat(101));
      await page.getByLabel(/^Số điện thoại/).fill('abc');
      await page.getByRole('button', { name: 'Tạo tài khoản', exact: true }).click();

      // Lỗi nằm trong `.field` của đúng ô và nối với ô bằng `aria-describedby`.
      const nameField = page.locator('.field', { has: page.locator('#reg-name') });
      const phoneField = page.locator('.field', { has: page.locator('#reg-phone') });
      await expect(nameField.locator('.form-error')).toHaveText('Họ và tên tối đa 100 ký tự.');
      await expect(phoneField.locator('.form-error')).toHaveText(
        'Số điện thoại không đúng định dạng.',
      );
      await expect(page.locator('#reg-phone')).toHaveAttribute('aria-invalid', 'true');
      await saveScreenshot(page, `register-errors-${viewport.width}.png`);
      expect(profileWrites).toEqual([]);
    });
  }

  await test.step('Đăng ký hợp lệ rồi nhập số sai ở trang tài khoản', async () => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/register');
    await page.getByLabel(/^Email/).fill(email);
    await page.getByLabel(/^Mật khẩu/).fill(e2eEnvironment.accountPassword);
    // Số có khoảng trắng là dạng hợp lệ. Backend lưu về `0912345678`.
    await page.getByLabel(/^Số điện thoại/).fill('0912 345 678');
    await page.getByRole('button', { name: 'Tạo tài khoản', exact: true }).click();
    await expect(page.getByRole('link', { name: 'Tài khoản', exact: true })).toBeVisible();
    // Lần ghi hợp lệ duy nhất cho đến giờ là lần đăng ký này.
    expect(profileWrites).toHaveLength(1);
    await page.getByRole('link', { name: 'Tài khoản', exact: true }).click();
    await expect(page.getByLabel('Số điện thoại', { exact: true })).toHaveValue('0912345678');
  });

  for (const viewport of [
    { width: 1280, height: 800 },
    { width: 375, height: 812 },
  ]) {
    await test.step(`Trang tài khoản ở ${viewport.width}px`, async () => {
      await page.setViewportSize(viewport);
      const writesBefore = profileWrites.length;
      await page.getByLabel('Số điện thoại', { exact: true }).fill('12345');
      await page.getByLabel('Họ và tên', { exact: true }).fill('Tên\u0007x');
      await page.getByRole('button', { name: 'Lưu thay đổi', exact: true }).click();

      const phoneField = page.locator('.field', { has: page.locator('#acc-phone') });
      const nameField = page.locator('.field', { has: page.locator('#acc-name') });
      await expect(phoneField.locator('.form-error')).toHaveText(
        'Số điện thoại không đúng định dạng.',
      );
      await expect(nameField.locator('.form-error')).toHaveText(
        'Họ và tên chứa ký tự không hợp lệ.',
      );
      await saveScreenshot(page, `account-errors-${viewport.width}.png`);
      // Không có request PATCH nào mới.
      expect(profileWrites).toHaveLength(writesBefore);
    });
  }

  expect(consoleIssues).toEqual([]);
});
