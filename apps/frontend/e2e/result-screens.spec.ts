import { expect, test, type Page, type Route } from '@playwright/test';

// Tiền tố ảnh: `before` chụp bản cũ để so sánh, `after` chụp bản mới. Mặc định là `after`.
const shotPrefix = process.env.UI_RESULT_SHOT_PREFIX ?? 'after';
// Ảnh chụp để người duyệt xem lại. Thư mục nằm ngoài repo nên không lẫn vào Git.
const screenshotDirectory =
  process.env.UI_RESULT_SHOTS_DIR ??
  'C:/Users/juven/AppData/Local/Temp/claude/C--Users-juven-Desktop-pickory/224dfa1c-b08d-4682-93a6-90d8bec3a32e/scratchpad';

interface AllowedLoadFailure {
  urlPattern: RegExp;
  status: number;
}

function collectConsoleIssues(page: Page, allowedFailures: AllowedLoadFailure[] = []): string[] {
  const issues: string[] = [];
  page.on('console', (message) => {
    // Trình duyệt Chromium có thể tự động tải favicon dù ứng dụng chưa khai báo.
    if (message.location().url.endsWith('/favicon.ico')) return;
    const isAllowedFailure = allowedFailures.some(
      (failure) =>
        failure.urlPattern.test(message.location().url) &&
        message.text().includes(`status of ${failure.status}`),
    );
    if (message.type() === 'error' && isAllowedFailure) return;
    if (message.type() === 'error' || message.type() === 'warning') {
      issues.push(`${message.type()}: ${message.text()} [url: ${message.location().url}]`);
    }
  });
  page.on('pageerror', (error) => issues.push(`pageerror: ${error.message}`));
  return issues;
}

async function dismissConsent(page: Page) {
  const consent = page.getByRole('button', { name: 'Chỉ cần thiết' });
  if (await consent.isVisible()) await consent.click();
}

async function expectNoHorizontalOverflow(page: Page) {
  // Đo chiều rộng tài liệu so với chiều rộng cửa sổ để phát hiện lỗi tràn ngang.
  const widths = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    innerWidth: window.innerWidth,
  }));
  expect(widths.scrollWidth).toBeLessThanOrEqual(widths.innerWidth);
}

// Kiểm tràn ngang rồi chụp `UI-RESULT-<trước hoặc sau>-<màn>-<độ rộng>.png` ở cả 1280px và 375px.
async function checkLayoutAtBothWidths(page: Page, screenName: string) {
  for (const width of [1280, 375]) {
    await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
    await expectNoHorizontalOverflow(page);
    // Cuộn về đầu trang trước khi chụp để thanh điều hướng không che nội dung.
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({
      path: `${screenshotDirectory}/UI-RESULT-${shotPrefix}-${screenName}-${width}.png`,
      fullPage: true,
    });
  }
  await page.setViewportSize({ width: 1280, height: 800 });
}

function fulfillJson(route: Route, status: number, body: unknown) {
  return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}

const guestActor = {
  anonymousId: 'e2e-anonymous',
  userId: null,
  kind: 'guest',
  roles: [],
  permissions: [],
  consent: { personalization: false, marketing: false },
};

const customerActor = {
  ...guestActor,
  userId: 'e2e-customer',
  email: 'khach@example.com',
  name: 'Khách Hàng',
  kind: 'customer',
  roles: ['customer'],
};

// Các màn kết quả dùng API giả vì mỗi màn cần một trạng thái khó tạo thật (link hết hạn, thư đã gửi).
// Luồng thật của từng API đã có spec riêng.
test.describe('UI-RESULT: màn kết quả, màn trống và không tìm thấy', () => {
  test('đã gửi yêu cầu quên mật khẩu', async ({ page }) => {
    const issues = collectConsoleIssues(page);
    await page.route('**/api/auth/password-resets', (route) =>
      fulfillJson(route, 202, { data: {} }),
    );
    await page.goto('/forgot-password');
    await dismissConsent(page);
    await page.getByLabel('Email tài khoản').fill('khach@example.com');
    await page.getByRole('button', { name: 'Gửi liên kết đặt lại' }).click();
    await expect(
      page.getByRole('status').filter({ hasText: 'Nếu email tồn tại trong hệ thống' }),
    ).toBeVisible();
    await checkLayoutAtBothWidths(page, 'forgot-password-sent');
    expect(issues).toEqual([]);
  });

  test('đặt lại mật khẩu xong', async ({ page }) => {
    const issues = collectConsoleIssues(page);
    await page.route('**/api/auth/password', (route) =>
      fulfillJson(route, 200, { data: { success: true, userKind: 'customer' } }),
    );
    await page.goto('/reset-password?token=token-e2e');
    await dismissConsent(page);
    await page.getByLabel('Mật khẩu mới (tối thiểu 8 ký tự)').fill('MatKhauMoi123');
    await page.getByLabel('Xác nhận mật khẩu mới').fill('MatKhauMoi123');
    await page.getByRole('button', { name: 'Cập nhật mật khẩu' }).click();
    await expect(
      page.getByRole('status').filter({ hasText: 'Mật khẩu của bạn đã được cập nhật' }),
    ).toBeVisible();
    await checkLayoutAtBothWidths(page, 'reset-password-done');
    expect(issues).toEqual([]);
  });

  test('link đặt lại mật khẩu thiếu mã', async ({ page }) => {
    const issues = collectConsoleIssues(page);
    await page.goto('/reset-password');
    await dismissConsent(page);
    await expect(
      page.getByRole('alert').filter({ hasText: 'Mã token không hợp lệ' }),
    ).toBeVisible();
    await checkLayoutAtBothWidths(page, 'reset-password-invalid');
    expect(issues).toEqual([]);
  });

  test('xác thực email thành công', async ({ page }) => {
    const issues = collectConsoleIssues(page);
    await page.route('**/api/auth/email-verifications', (route) =>
      fulfillJson(route, 200, { data: {} }),
    );
    await page.goto('/verify-email?token=token-e2e');
    await dismissConsent(page);
    await expect(
      page.getByRole('status').filter({ hasText: 'đã được xác thực thành công' }),
    ).toBeVisible();
    await checkLayoutAtBothWidths(page, 'verify-email-success');
    expect(issues).toEqual([]);
  });

  test('xác thực email thất bại', async ({ page }) => {
    const issues = collectConsoleIssues(page, [
      { urlPattern: /\/api\/auth\/email-verifications/, status: 400 },
    ]);
    await page.route('**/api/auth/email-verifications', (route) =>
      fulfillJson(route, 400, {
        error: {
          code: 'TOKEN_INVALID',
          message: 'Liên kết xác thực đã hết hạn hoặc đã được dùng.',
        },
      }),
    );
    await page.goto('/verify-email?token=token-e2e');
    await dismissConsent(page);
    await expect(
      page.getByRole('alert').filter({ hasText: 'Liên kết xác thực đã hết hạn' }),
    ).toBeVisible();
    await checkLayoutAtBothWidths(page, 'verify-email-error');
    expect(issues).toEqual([]);
  });

  test('không tìm thấy đơn hàng', async ({ page }) => {
    const issues = collectConsoleIssues(page, [{ urlPattern: /\/api\/orders\//, status: 404 }]);
    await page.route('**/api/orders/**', (route) =>
      fulfillJson(route, 404, {
        error: { code: 'NOT_FOUND', message: 'Không tìm thấy đơn hàng.' },
      }),
    );
    await page.goto('/orders/PKAB23CD45');
    await dismissConsent(page);
    await expect(
      page.getByRole('heading', { level: 1, name: 'Không tìm thấy đơn hàng' }),
    ).toBeVisible();
    await checkLayoutAtBothWidths(page, 'order-not-found');
    expect(issues).toEqual([]);
  });

  test('giỏ hàng trống', async ({ page }) => {
    const issues = collectConsoleIssues(page);
    await page.goto('/cart');
    await dismissConsent(page);
    await expect(page.getByText('Giỏ hàng của bạn đang trống')).toBeVisible();
    await checkLayoutAtBothWidths(page, 'cart-empty');
    expect(issues).toEqual([]);
  });

  test('danh sách đơn rỗng của khách đã đăng nhập', async ({ page }) => {
    const issues = collectConsoleIssues(page);
    await page.route('**/api/me', (route) => fulfillJson(route, 200, { data: customerActor }));
    await page.route('**/api/orders?**', (route) =>
      fulfillJson(route, 200, { data: { items: [], total: 0, page: 1, limit: 20 } }),
    );
    await page.goto('/me/orders');
    await dismissConsent(page);
    await expect(page.getByText('Bạn chưa có đơn hàng nào.')).toBeVisible();
    await checkLayoutAtBothWidths(page, 'orders-empty');
    expect(issues).toEqual([]);
  });

  test('đơn hàng của tôi khi chưa đăng nhập', async ({ page }) => {
    const issues = collectConsoleIssues(page);
    await page.route('**/api/me', (route) => fulfillJson(route, 200, { data: guestActor }));
    await page.goto('/me/orders');
    await dismissConsent(page);
    await expect(page.getByText('Đăng nhập để xem các đơn hàng của bạn.')).toBeVisible();
    await checkLayoutAtBothWidths(page, 'orders-signed-out');
    expect(issues).toEqual([]);
  });
});
