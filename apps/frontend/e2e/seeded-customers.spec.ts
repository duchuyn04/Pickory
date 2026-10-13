import { expect, test } from '@playwright/test';
import { e2eEnvironment } from './environment';

test('khách E2E đã xác thực đăng nhập với vai trò customer', async ({ page }) => {
  const email = e2eEnvironment.seedVerificationEmail;
  const consoleIssues: string[] = [];
  page.on('console', (message) => {
    if (message.location().url.endsWith('/favicon.ico')) return;
    if (message.type() === 'error' || message.type() === 'warning') {
      consoleIssues.push(`[${message.type()}] ${message.text()}`);
    }
  });
  page.on('pageerror', (error) => consoleIssues.push(`[pageerror] ${error.message}`));

  // Đăng nhập qua giao diện để chứng minh mật khẩu seed được hệ thống xác thực thật.
  await page.goto('/login');
  await page.getByRole('button', { name: 'Chỉ cần thiết', exact: true }).click();
  await page.getByLabel(/^Email/).fill(email);
  await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.accountPassword);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Tài khoản', exact: true })).toBeVisible();

  // Phiên vừa tạo phải thuộc khách có quyền customer, không phải nhân viên.
  const meResponse = await page.request.get('/api/me');
  expect(meResponse.status()).toBe(200);
  const me = (await meResponse.json()) as {
    data: { email: string; kind: string; roles: string[] };
  };
  expect(me.data).toMatchObject({ email, kind: 'customer' });
  expect(me.data.roles).toContain('customer');

  // Chỉ trang quản trị hiển thị cờ emailVerified, nên đăng nhập admin để kiểm tra bản ghi.
  await page.getByRole('button', { name: 'Đăng xuất', exact: true }).click();
  await page.goto('/admin/login');
  await page.getByLabel(/^Email/).fill(e2eEnvironment.adminEmail);
  await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.adminPassword);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin`);
  const customersResponse = await page.request.get(
    `/api/admin/customers?search=${encodeURIComponent(email)}`,
  );
  expect(customersResponse.status()).toBe(200);
  const customers = (await customersResponse.json()) as {
    data: { items: { email: string; kind: string; emailVerified: boolean }[] };
  };
  expect(customers.data.items).toMatchObject([{ email, kind: 'customer', emailVerified: true }]);
  expect(consoleIssues).toEqual([]);
});
