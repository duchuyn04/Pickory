import { randomBytes } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';

// Trang không được rộng hơn khung nhìn ở mọi màn của khách.
async function expectNoHorizontalOverflow(page: Page) {
  const widths = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth,
  }));
  expect(widths.content).toBeLessThanOrEqual(widths.viewport);
}

// Các màn công khai của khách cùng tiêu đề dùng để chờ màn tải xong.
const publicScreens = [
  { path: '/login', heading: 'Đăng nhập', name: 'login' },
  { path: '/register', heading: 'Đăng ký tài khoản', name: 'register' },
  { path: '/forgot-password', heading: 'Quên mật khẩu', name: 'forgot-password' },
  { path: '/reset-password?token=token-e2e', heading: 'Đặt lại mật khẩu', name: 'reset-password' },
  { path: '/verify-email', heading: 'Xác thực email', name: 'verify-email' },
  { path: '/privacy', heading: 'Cài đặt quyền riêng tư', name: 'privacy' },
];

test('DS-05: header đỏ và màn tài khoản khách không tràn ở 1280px và 375px', async ({
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

  await test.step('Banner cookie có hai lựa chọn rộng bằng nhau ở 375px', async () => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto('/login');
    const necessaryOnly = page.getByRole('button', { name: 'Chỉ cần thiết', exact: true });
    const acceptAll = page.getByRole('button', { name: 'Đồng ý', exact: true });
    await expect(necessaryOnly).toBeVisible();
    // Spec identity yêu cầu hai lựa chọn có cùng kích thước, không nút nào nổi bật hơn.
    const necessaryBox = await necessaryOnly.boundingBox();
    const acceptBox = await acceptAll.boundingBox();
    expect(Math.round(necessaryBox?.width ?? 0)).toBe(Math.round(acceptBox?.width ?? -1));
    expect(Math.round(necessaryBox?.height ?? 0)).toBe(Math.round(acceptBox?.height ?? -1));
    await expectNoHorizontalOverflow(page);
    await page.screenshot({ path: testInfo.outputPath('consent-banner-375.png') });
    await necessaryOnly.click();
    await expect(necessaryOnly).toBeHidden();
  });

  for (const width of [1280, 375]) {
    await test.step(`Header và các màn công khai ở ${width}px`, async () => {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      for (const screen of publicScreens) {
        await page.goto(screen.path);
        await expect(page.getByRole('heading', { level: 1, name: screen.heading })).toBeVisible();
        await expectNoHorizontalOverflow(page);
        await page.screenshot({
          path: testInfo.outputPath(`${screen.name}-${width}.png`),
          fullPage: true,
        });
      }
      // Header dùng nền đỏ thương hiệu `#d70018` theo bản mẫu.
      const headerBackground = await page
        .locator('header')
        .first()
        .evaluate((header) => getComputedStyle(header).backgroundColor);
      expect(headerBackground).toBe('rgb(215, 0, 24)');
    });
  }

  await test.step('Lỗi nhập liệu hiện dưới ô sai trên trình duyệt thật', async () => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto('/login');
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
    const emailInput = page.getByLabel('Email', { exact: true });
    await expect(emailInput).toHaveAttribute('aria-invalid', 'true');
    await expect(page.locator('#login-email-error')).toHaveText('Email không được để trống.');
    await page.screenshot({ path: testInfo.outputPath('login-errors-375.png'), fullPage: true });
  });

  await test.step('Màn tài khoản sau khi đăng ký ở 1280px và 375px', async () => {
    // Email riêng cho mỗi lần chạy để spec không trùng tài khoản với `auth.spec.ts`.
    const email = `e2e-customer-screens-${randomBytes(4).toString('hex')}@pickory.local`;
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/register');
    await page.getByLabel(/^Email/).fill(email);
    await page.getByLabel(/^Mật khẩu/).fill(e2eEnvironment.accountPassword);
    await page.getByRole('button', { name: 'Tạo tài khoản', exact: true }).click();
    await expect(page.getByRole('link', { name: 'Tài khoản', exact: true })).toBeVisible();
    await page.getByRole('link', { name: 'Tài khoản', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Tài khoản của tôi' })).toBeVisible();
    await expectNoHorizontalOverflow(page);
    await page.screenshot({ path: testInfo.outputPath('account-1280.png'), fullPage: true });
    await page.setViewportSize({ width: 375, height: 812 });
    await expectNoHorizontalOverflow(page);
    await page.screenshot({ path: testInfo.outputPath('account-375.png'), fullPage: true });
  });

  expect(consoleIssues).toEqual([]);
});
