import { expect, test, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';

// Đăng nhập khu quản trị bằng tài khoản admin đã seed rồi mở trang khách hàng, tìm đúng một khách.
async function openCustomerAsAdmin(page: Page, customerEmail: string) {
  await page.goto('/admin/login');
  await page.getByLabel(/^Email/).fill(e2eEnvironment.adminEmail);
  await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.adminPassword);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await page.getByRole('link', { name: 'Khách hàng', exact: true }).click();
  await page
    .getByLabel('Tìm theo tên, email hoặc số điện thoại', { exact: true })
    .fill(customerEmail);
  // Ô tìm kiếm tự lọc sau 300 ms kể từ lần gõ cuối, không còn nút Tìm. Chờ từ khóa lên URL và danh sách nạp xong.
  await expect(page).toHaveURL(/[?&]search=/);
  await expect(page.getByText(/^Đang nạp danh sách/)).toHaveCount(0);
  return page.getByRole('row').filter({ hasText: customerEmail });
}

async function signOutFromAdmin(page: Page) {
  await page.getByRole('button', { name: 'Đăng xuất', exact: true }).click();
  await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/login`);
}

// Khách bị khóa không đăng nhập được. Mở khóa cho khách đăng nhập lại.
test('nhân viên khóa khách rồi mở khóa qua giao diện quản trị', async ({ page }) => {
  const consoleIssues: string[] = [];
  page.on('console', (message) => {
    if (message.location().url.endsWith('/favicon.ico')) return;
    if (message.type() === 'error' || message.type() === 'warning') {
      consoleIssues.push(`[${message.type()}] ${message.text()}`);
    }
  });
  page.on('pageerror', (error) => consoleIssues.push(`[pageerror] ${error.message}`));
  // Hộp thoại hỏi lại trước khi khóa hoặc mở khóa. Test chấp nhận để chạy tiếp.
  page.on('dialog', (dialog) => void dialog.accept());

  // Khách này chỉ bị khóa trong spec này, nên phiên của các spec khác không thay đổi.
  const email = e2eEnvironment.lockCustomerEmail;

  await test.step('Khách đã seed đăng nhập rồi đăng xuất', async () => {
    await page.goto('/login');
    await page.getByRole('button', { name: 'Chỉ cần thiết', exact: true }).click();
    await page.getByLabel(/^Email/).fill(email);
    await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.accountPassword);
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
    await expect(page.getByRole('link', { name: 'Tài khoản', exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Đăng xuất', exact: true }).click();
    await expect(page.getByRole('link', { name: 'Đăng nhập', exact: true })).toBeVisible();
  });

  await test.step('Admin khóa khách', async () => {
    const row = await openCustomerAsAdmin(page, email);
    await expect(row.getByText('Hoạt động')).toBeVisible();
    await row.getByRole('button', { name: 'Khóa', exact: true }).click();
    await expect(row.getByText('Đã khóa')).toBeVisible();
    await signOutFromAdmin(page);
  });

  await test.step('Khách bị khóa không đăng nhập được', async () => {
    const issuesBeforeFailedLogin = consoleIssues.length;
    await page.goto('/login');
    await page.getByLabel(/^Email/).fill(email);
    await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.accountPassword);
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
    // API trả cùng thông báo cho tài khoản bị khóa và sai mật khẩu. Người ngoài không biết trạng thái tài khoản.
    await expect(page.getByText('Email hoặc mật khẩu không chính xác.')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Tài khoản', exact: true })).toHaveCount(0);
    // Trình duyệt tự ghi lỗi 401 của lần đăng nhập bị từ chối. Đây là kết quả mong đợi, không phải lỗi của trang.
    const unexpectedIssues = consoleIssues
      .splice(issuesBeforeFailedLogin)
      .filter((issue) => !issue.includes('status of 401'));
    consoleIssues.push(...unexpectedIssues);
  });

  await test.step('Admin mở khóa khách', async () => {
    const row = await openCustomerAsAdmin(page, email);
    await expect(row.getByText('Đã khóa')).toBeVisible();
    await row.getByRole('button', { name: 'Mở khóa', exact: true }).click();
    await expect(row.getByText('Hoạt động')).toBeVisible();
    await signOutFromAdmin(page);
  });

  await test.step('Khách đăng nhập lại được', async () => {
    await page.goto('/login');
    await page.getByLabel(/^Email/).fill(email);
    await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.accountPassword);
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
    await expect(page.getByRole('link', { name: 'Tài khoản', exact: true })).toBeVisible();
  });

  expect(consoleIssues).toEqual([]);
});
