import { expect, test, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';
import { readPasswordSetupLink } from './outbox';

async function loginAdmin(page: Page) {
  await page.goto('/admin/login');
  const consent = page.getByRole('button', { name: 'Chỉ cần thiết' });
  if (await consent.isVisible()) await consent.click();
  await page.getByLabel(/^Email/).fill(e2eEnvironment.adminEmail);
  await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.adminPassword);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin`);
}

// Ở màn hẹp, menu quản trị thu vào nút "Menu". Mở menu trước khi bấm mục trong menu.
async function openAdminMenuIfCollapsed(page: Page) {
  const menuToggle = page.getByRole('button', { name: 'Menu', exact: true });
  const logoutButton = page.getByRole('button', { name: 'Đăng xuất', exact: true });
  await expect(menuToggle.or(logoutButton).first()).toBeVisible();
  if (!(await menuToggle.isVisible())) return;
  if ((await menuToggle.getAttribute('aria-expanded')) === 'true') return;
  await menuToggle.click();
}

// Đo tràn ngang của cả trang. Ảnh chụp rộng hơn viewport nghĩa là người dùng phải cuộn ngang.
async function expectNoHorizontalPageOverflow(page: Page) {
  const widths = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(widths.scrollWidth).toBeLessThanOrEqual(widths.clientWidth);
}

interface CreatedSku {
  productId: string;
  skuId: string;
  code: string;
}

// Tạo sản phẩm một SKU bằng API trong phiên admin, để spec chỉ tập trung vào màn tồn kho.
async function createProduct(page: Page): Promise<CreatedSku> {
  const headers = { Origin: e2eEnvironment.webOrigin };
  const suffix = Date.now().toString(36);
  const category = await page.request.post('/api/admin/categories', {
    headers,
    data: { name: `Tồn kho C7 ${suffix}`, parentId: null, position: 0, attributes: [] },
  });
  expect(category.status()).toBe(201);
  const brand = await page.request.post('/api/admin/brands', {
    headers,
    data: { name: `Hãng tồn kho C7 ${suffix}` },
  });
  expect(brand.status()).toBe(201);
  const product = await page.request.post('/api/admin/products', {
    headers,
    data: {
      categoryId: (await category.json()).data.id,
      brandId: (await brand.json()).data.id,
      name: `Máy tồn kho C7 ${suffix}`,
      description: { type: 'doc', content: [{ type: 'paragraph' }] },
      specs: {},
      options: [],
      variants: [{ optionValues: {}, price: 100000, enabled: true }],
    },
  });
  expect(product.status()).toBe(201);
  const data = (await product.json()).data;
  return { productId: data.id, skuId: data.variants[0].id, code: data.variants[0].code };
}

// Mở hộp thoại, nhập số lượng và lý do rồi gửi. Trả về hộp thoại để test kiểm kết quả.
async function submitAdjustment(page: Page, delta: string, reason: string) {
  await page.getByRole('button', { name: 'Điều chỉnh tồn kho', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Số lượng thay đổi').fill(delta);
  if (reason) await dialog.getByLabel('Lý do').fill(reason);
  await dialog.getByRole('button', { name: 'Lưu điều chỉnh', exact: true }).click();
  return dialog;
}

test('UI-C7: điều chỉnh tồn kho, danh sách sắp hết hàng và quyền xem của sales', async ({
  page,
}, testInfo) => {
  test.setTimeout(150000);
  // Ghi lỗi console và lỗi trang. Spec phải có 0 lỗi và 0 cảnh báo, ngoài một lỗi 409 mà test chủ động gây ra.
  const issues: string[] = [];
  let expected409Logs = 0;
  page.on('console', (message) => {
    if (message.location().url.endsWith('/favicon.ico')) return;
    // Trình duyệt tự ghi dòng "Failed to load resource" cho phản hồi 409 của bước giảm quá tồn kho.
    if (
      message.type() === 'error' &&
      message.location().url.endsWith('/movements') &&
      message.text().includes('status of 409')
    ) {
      expected409Logs += 1;
      return;
    }
    if (message.type() === 'error' || message.type() === 'warning') {
      issues.push(`${message.type()}: ${message.text()}`);
    }
  });
  page.on('pageerror', (error) => issues.push(`pageerror: ${error.message}`));
  // Đếm request điều chỉnh để chứng minh lỗi nhập liệu không gửi gì lên máy chủ.
  let adjustRequests = 0;
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().endsWith('/movements')) adjustRequests += 1;
  });

  await loginAdmin(page);
  const sku = await createProduct(page);
  const skuUrl = `/admin/products/${sku.productId}/skus/${sku.skuId}/inventory`;
  await page.goto(skuUrl);
  await expect(page.getByRole('heading', { name: `Tồn kho ${sku.code}` })).toBeVisible();
  await expect(page.getByTestId('sku-stock')).toHaveText('0');

  await test.step('Điều chỉnh +7 cập nhật số tồn và lịch sử mà không tải lại trang', async () => {
    // Cờ trên `window` mất nếu trang điều hướng hoặc tải lại.
    await page.evaluate(() => {
      (window as unknown as { __noReload: boolean }).__noReload = true;
    });
    const dialog = await submitAdjustment(page, '7', 'Nhập kho đầu kỳ E2E');
    await expect(dialog).toBeHidden();
    await expect(page.getByTestId('sku-stock')).toHaveText('7');
    const firstRow = page.locator('table[aria-label="Lịch sử tồn kho"] tbody tr').first();
    await expect(firstRow).toContainText('+7');
    await expect(firstRow).toContainText('Nhập kho đầu kỳ E2E');
    await expect(firstRow).toContainText('Điều chỉnh');
    await expect(page.getByRole('status').filter({ hasText: 'Tồn kho hiện tại: 7' })).toBeVisible();
    expect(
      await page.evaluate(() => (window as unknown as { __noReload?: boolean }).__noReload),
    ).toBe(true);
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}${skuUrl}`);
    // Focus về nút đã mở hộp thoại.
    await expect(
      page.getByRole('button', { name: 'Điều chỉnh tồn kho', exact: true }),
    ).toBeFocused();
  });

  await test.step('Giảm quá số tồn hiện lỗi 409 bằng tiếng Việt và hộp thoại không đóng', async () => {
    const dialog = await submitAdjustment(page, '-100', 'Kiểm kê lệch E2E');
    await expect(dialog.getByRole('alert')).toHaveText('Tồn kho hiện có 7, không thể giảm 100.');
    await expect(dialog).toBeVisible();
    // Dữ liệu đã nhập được giữ để admin chỉ sửa số lượng.
    await expect(dialog.getByLabel('Lý do')).toHaveValue('Kiểm kê lệch E2E');
    expect(expected409Logs).toBe(1);
    await expect(page.getByTestId('sku-stock')).toHaveText('7');
    await page.screenshot({ path: testInfo.outputPath('dialog-409-1280.png') });
    await dialog.getByRole('button', { name: 'Hủy', exact: true }).click();
    await expect(dialog).toBeHidden();
  });

  await test.step('Bỏ trống lý do hiện lỗi dưới ô và không gửi request', async () => {
    const requestsBefore = adjustRequests;
    const dialog = await submitAdjustment(page, '1', '');
    await expect(dialog.getByLabel('Lý do')).toHaveAttribute('aria-invalid', 'true');
    await expect(dialog.locator('#adjust-reason-error')).toHaveText('Lý do là bắt buộc.');
    expect(adjustRequests).toBe(requestsBefore);
    await page.screenshot({ path: testInfo.outputPath('dialog-error-1280.png') });
    await dialog.getByRole('button', { name: 'Hủy', exact: true }).click();
  });

  await test.step('Giảm xuống 4 để SKU vào danh sách sắp hết hàng', async () => {
    const dialog = await submitAdjustment(page, '-3', 'Hàng trưng bày E2E');
    await expect(dialog).toBeHidden();
    await expect(page.getByTestId('sku-stock')).toHaveText('4');
    await expect(
      page.locator('table[aria-label="Lịch sử tồn kho"] tbody tr').first(),
    ).toContainText('-3');
    await page.screenshot({ path: testInfo.outputPath('sku-1280.png'), fullPage: true });
  });

  await test.step('Danh sách /admin/inventory hiện SKU và liên kết mở đúng trang SKU', async () => {
    await openAdminMenuIfCollapsed(page);
    await page.getByRole('link', { name: 'Tồn kho', exact: true }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/inventory`);
    await expect(page.getByRole('heading', { name: 'Tồn kho', exact: true })).toBeVisible();
    const link = page.getByRole('link', { name: `Xem tồn kho ${sku.code}`, exact: true });
    // Chờ bảng tải xong trước khi đoán SKU nằm ở trang nào.
    await expect(page.locator('tbody tr').first()).toBeVisible();
    // Danh sách sắp theo tồn tăng dần nên SKU có thể nằm ở trang sau. Lật trang đến khi thấy.
    for (let turn = 0; turn < 10 && !(await link.isVisible()); turn += 1) {
      await page.getByRole('button', { name: 'Trang sau', exact: true }).click();
    }
    await expect(link).toBeVisible();
    const row = page.locator('tbody tr').filter({ has: link });
    await expect(row).toContainText('Sắp hết');
    await expect(row).toContainText('4');
    await page.screenshot({ path: testInfo.outputPath('list-1280.png'), fullPage: true });
    await link.click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}${skuUrl}`);
  });

  await test.step('Bảng biến thể của form sản phẩm có liên kết Tồn kho', async () => {
    await page.goto(`/admin/products/${sku.productId}`);
    await page.getByRole('link', { name: 'Tồn kho Mặc định', exact: true }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}${skuUrl}`);
  });

  await test.step('Chụp ảnh 375 px và kiểm trang không tràn ngang', async () => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto(skuUrl);
    await expect(page.getByTestId('sku-stock')).toHaveText('4');
    await expectNoHorizontalPageOverflow(page);
    await page.screenshot({ path: testInfo.outputPath('sku-375.png'), fullPage: true });

    await page.getByRole('button', { name: 'Điều chỉnh tồn kho', exact: true }).click();
    await expect(page.getByRole('dialog')).toBeVisible();
    await expectNoHorizontalPageOverflow(page);
    await page.screenshot({ path: testInfo.outputPath('dialog-375.png') });
    await page.getByRole('button', { name: 'Hủy', exact: true }).click();

    await page.goto('/admin/inventory');
    await expect(page.getByRole('heading', { name: 'Tồn kho', exact: true })).toBeVisible();
    await expectNoHorizontalPageOverflow(page);
    await page.screenshot({ path: testInfo.outputPath('list-375.png'), fullPage: true });
    await page.setViewportSize({ width: 1280, height: 720 });
  });

  await test.step('Sales chỉ có products:read thấy số tồn và lịch sử, không thấy nút điều chỉnh', async () => {
    await page.goto('/admin/staff');
    await page.getByRole('button', { name: '+ Mời nhân viên', exact: true }).click();
    const invitation = page.getByRole('dialog', { name: 'Mời nhân viên mới', exact: true });
    const salesEmail = e2eEnvironment.inventorySalesEmail;
    await invitation.getByLabel(/^Email nhân viên/).fill(salesEmail);
    await invitation.getByLabel(/^Họ và tên/).fill('Sales Tồn Kho E2E');
    await invitation.getByRole('checkbox', { name: /\(sales\)$/ }).check();
    await invitation.getByRole('button', { name: 'Gửi lời mời', exact: true }).click();
    await expect(invitation).toBeHidden();

    await openAdminMenuIfCollapsed(page);
    await page.getByRole('button', { name: 'Đăng xuất', exact: true }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/login`);

    await page.goto(await readPasswordSetupLink(salesEmail));
    await page
      .getByLabel('Mật khẩu mới (tối thiểu 8 ký tự)', { exact: true })
      .fill(e2eEnvironment.accountPassword);
    await page
      .getByLabel('Xác nhận mật khẩu mới', { exact: true })
      .fill(e2eEnvironment.accountPassword);
    await page.getByRole('button', { name: 'Cập nhật mật khẩu', exact: true }).click();
    await page.getByRole('link', { name: 'Đăng nhập trang quản trị', exact: true }).click();
    await page.getByLabel(/^Email/).fill(salesEmail);
    await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.accountPassword);
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();

    // Chờ đăng nhập xong. Điều hướng sớm cắt ngang yêu cầu đăng nhập.
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin`);
    await page.goto(skuUrl);
    await expect(page.getByRole('heading', { name: `Tồn kho ${sku.code}` })).toBeVisible();
    await expect(page.getByTestId('sku-stock')).toHaveText('4');
    await expect(
      page.locator('table[aria-label="Lịch sử tồn kho"] tbody tr').first(),
    ).toContainText('-3');
    await expect(page.getByRole('button', { name: 'Điều chỉnh tồn kho' })).toHaveCount(0);
    await page.screenshot({ path: testInfo.outputPath('sku-sales-1280.png'), fullPage: true });
  });

  expect(issues).toEqual([]);
});
