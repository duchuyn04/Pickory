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

async function createCategory(
  page: Page,
  name: string,
  parentId: string | null,
  attributes: object[],
) {
  // Dữ liệu API nằm trong phiên admin thật; mỗi lần chạy E2E khởi tạo cơ sở dữ liệu riêng.
  const response = await page.request.post('/api/admin/categories', {
    headers: { Origin: e2eEnvironment.webOrigin },
    data: { name, parentId, position: 0, attributes },
  });
  expect(response.status()).toBe(201);
  return (await response.json()).data.id as string;
}

async function assertNoPageOverflow(page: Page) {
  const widths = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth,
  }));
  expect(widths.content).toBeLessThanOrEqual(widths.viewport);
}

test('UI-C5 và UI-C8: tạo, lọc, xem sản phẩm và soạn mô tả', async ({
  page,
  context,
}, testInfo) => {
  test.setTimeout(120000);
  const issues: string[] = [];
  page.on('console', (message) => {
    if (message.location().url.endsWith('/favicon.ico')) return;
    if (message.type() === 'error' || message.type() === 'warning')
      issues.push(`${message.type()}: ${message.text()}`);
  });
  page.on('pageerror', (error) => issues.push(`pageerror: ${error.message}`));

  await loginAdmin(page);
  const rootId = await createCategory(page, 'Máy tính C5', null, [
    {
      key: 'origin',
      label: 'Xuất xứ',
      group: 'Chung',
      type: 'text',
      required: true,
      filterable: false,
    },
  ]);
  const laptopId = await createCategory(page, 'Laptop C5', rootId, [
    {
      key: 'ram',
      label: 'RAM',
      group: 'Cấu hình',
      type: 'number',
      required: true,
      filterable: true,
    },
  ]);
  const headsetId = await createCategory(page, 'Tai nghe C5', null, [
    {
      key: 'connection',
      label: 'Kết nối',
      group: 'Chung',
      type: 'text',
      required: true,
      filterable: false,
    },
  ]);
  const brandResponse = await page.request.post('/api/admin/brands', {
    headers: { Origin: e2eEnvironment.webOrigin },
    data: { name: 'Hãng C5' },
  });
  expect(brandResponse.status()).toBe(201);
  const brandId = (await brandResponse.json()).data.id as string;

  await page.goto('/admin/products/new');
  await expect(page.getByRole('heading', { name: 'Tạo sản phẩm' })).toBeVisible();
  await page.getByLabel('Danh mục lá').selectOption(headsetId);
  await expect(page.getByLabel('Kết nối *')).toBeVisible();
  await page.getByLabel('Danh mục lá').selectOption(laptopId);
  await expect(page.getByLabel('Kết nối *')).toHaveCount(0);
  await expect(page.getByLabel('RAM *')).toBeVisible();
  await expect(page.getByLabel('Xuất xứ *')).toBeVisible();
  await page.getByLabel('Tên sản phẩm').fill('Laptop UI C5');
  await page.getByLabel('Thương hiệu').selectOption(brandId);
  await page.getByLabel('Xuất xứ *').fill('Việt Nam');
  await page.getByLabel('RAM *').fill('16');
  await page.getByRole('button', { name: 'Thêm trục' }).click();
  await page.getByLabel('Tên trục 1').fill('Màu');
  // Gõ thật để phát hiện khoảng trắng bị mất giữa các ký tự của giá trị tùy chọn.
  await page.getByLabel('Giá trị trục 1').pressSequentially('Đỏ, Xanh dương');
  await expect(page.getByLabel('Giá trị trục 1')).toHaveValue('Đỏ, Xanh dương');
  await page.getByRole('button', { name: 'Thêm trục' }).click();
  await page.getByLabel('Tên trục 2').fill('Dung lượng');
  await page.getByLabel('Giá trị trục 2').fill('128, 256');
  await expect(page.locator('[data-variant-row]')).toHaveCount(4);
  await expect(page.locator('[data-variant-row]').nth(2)).toContainText('Xanh dương');
  for (let index = 0; index < 4; index++)
    await page.locator(`[name="variant-price-${index}"]`).fill(String(100000 + index * 10000));

  const editor = page.getByRole('textbox', { name: 'Nội dung mô tả chi tiết sản phẩm' });
  await editor.click();
  await editor.pressSequentially('Mô tả ');
  await editor.press('Control+b');
  await editor.pressSequentially('đậm');
  await editor.press('Control+b');
  await editor.press('Control+i');
  await editor.pressSequentially(' nghiêng');
  await editor.press('Control+i');
  await expect(editor.locator('strong')).toContainText('đậm');
  await expect(editor.locator('em')).toContainText('nghiêng');
  // Clipboard thật phải đi qua Tiptap để phần tử script, ảnh và link không vào tài liệu.
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  await page.evaluate(async () => {
    const html =
      '<p><a href="https://example.com">Liên kết</a><img src="https://example.com/a.png"><script>alert(1)</script></p><ul><li>Danh sách</li></ul>';
    await navigator.clipboard.write([
      new ClipboardItem({
        'text/html': new Blob([html], { type: 'text/html' }),
        'text/plain': new Blob(['Liên kết Danh sách'], { type: 'text/plain' }),
      }),
    ]);
  });
  await editor.click();
  await editor.press('Control+v');
  await expect(editor).toContainText('Liên kết');
  await expect(editor).toContainText('Danh sách');
  await expect(editor.locator('img, script, a, ul, li')).toHaveCount(0);

  await page.getByRole('button', { name: 'Lưu sản phẩm' }).click();
  await expect(page).toHaveURL(/\/admin\/products\/[0-9a-f]{32}$/);
  const savedUrl = page.url();
  await page.reload();
  await expect(page.getByLabel('Tên sản phẩm')).toHaveValue('Laptop UI C5');
  await expect(page.locator('[data-variant-row]')).toHaveCount(4);
  await expect(page.locator('[name="variant-price-2"]')).toHaveValue('120000');
  await expect(editor.locator('strong')).toContainText('đậm');
  await expect(editor.locator('em')).toContainText('nghiêng');
  // Mở lại bằng liên kết để phát hiện cache chi tiết cũ, vì tải lại trang sẽ che lỗi ghi đè giá.
  await page.locator('[name="variant-price-2"]').fill('150000');
  const updateResponse = page.waitForResponse(
    (response) =>
      response.request().method() === 'PATCH' &&
      /\/api\/admin\/products\/[0-9a-f]{32}$/.test(response.url()),
  );
  await page.getByRole('button', { name: 'Lưu sản phẩm' }).click();
  expect((await updateResponse).status()).toBe(200);
  await page.getByRole('link', { name: 'Về danh sách' }).click();
  await page
    .getByRole('row', { name: /Laptop UI C5/ })
    .getByRole('link', { name: 'Chỉnh sửa' })
    .click();
  await expect(page.locator('[name="variant-price-2"]')).toHaveValue('150000');

  await page.goto('/admin/products');
  await page.getByLabel('Tên hoặc mã SKU').fill('Laptop UI C5');
  await expect(page.getByRole('row', { name: /Laptop UI C5/ })).toBeVisible();
  await page.getByLabel('Trạng thái').selectOption('draft');
  await page.getByLabel('Danh mục').selectOption(rootId);
  await page.getByRole('checkbox', { name: /Sắp hết hàng/ }).check();
  await expect(page.getByRole('row', { name: /Laptop UI C5/ })).toBeVisible();
  await page.getByLabel('Trạng thái').selectOption('active');
  await expect(page.getByText('Không có sản phẩm phù hợp.')).toBeVisible();

  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto(savedUrl);
  await expect(page.getByLabel('Tên sản phẩm')).toHaveValue('Laptop UI C5');
  // Công tắc bán hàng phải thấy ở 1280 px mà không cuộn bảng sang ngang.
  const tableViewport = await page.locator('.product-table-scroll').boundingBox();
  const enabledToggle = await page
    .getByRole('checkbox', { name: 'Bật Xanh dương / 256' })
    .boundingBox();
  expect(tableViewport).not.toBeNull();
  expect(enabledToggle).not.toBeNull();
  expect(enabledToggle!.x).toBeGreaterThanOrEqual(tableViewport!.x);
  expect(enabledToggle!.x + enabledToggle!.width).toBeLessThanOrEqual(
    tableViewport!.x + tableViewport!.width,
  );
  await page.screenshot({ path: testInfo.outputPath('product-desktop.png'), fullPage: true });
  await page.setViewportSize({ width: 375, height: 812 });
  await assertNoPageOverflow(page);
  await page.screenshot({ path: testInfo.outputPath('product-mobile.png'), fullPage: true });
  await page.goto('/admin/products');
  await expect(page.getByRole('heading', { name: 'Danh sách' })).toBeVisible();
  await assertNoPageOverflow(page);
  await page.screenshot({ path: testInfo.outputPath('products-mobile.png'), fullPage: true });

  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto('/admin/staff');
  await page.getByRole('button', { name: '+ Mời nhân viên' }).click();
  const invitation = page.getByRole('dialog', { name: 'Mời nhân viên mới' });
  // Dùng tài khoản riêng để hai spec không tranh chấp email khi chạy cùng bộ E2E.
  const salesEmail = 'e2e-catalog-products-sales@pickory.local';
  await invitation.getByLabel(/^Email nhân viên/).fill(salesEmail);
  await invitation.getByLabel(/^Họ và tên/).fill('Sales C5');
  await invitation.getByRole('checkbox', { name: /\(sales\)$/ }).check();
  await invitation.getByRole('button', { name: 'Gửi lời mời' }).click();
  await expect(invitation).toBeHidden();
  await page.getByRole('button', { name: 'Đăng xuất' }).click();
  await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/login`);
  await page.goto(await readPasswordSetupLink(salesEmail));
  await page.getByLabel('Mật khẩu mới (tối thiểu 8 ký tự)').fill(e2eEnvironment.accountPassword);
  await page.getByLabel('Xác nhận mật khẩu mới').fill(e2eEnvironment.accountPassword);
  await page.getByRole('button', { name: 'Cập nhật mật khẩu' }).click();
  await page.getByRole('link', { name: 'Đăng nhập trang quản trị' }).click();
  await page.getByLabel(/^Email/).fill(salesEmail);
  await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.accountPassword);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  // Chờ phiên sales được tạo xong trước khi chuyển trang, tránh cắt ngang yêu cầu đăng nhập.
  await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin`);
  await page.goto(savedUrl);
  await expect(page.getByLabel('Tên sản phẩm')).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Lưu sản phẩm' })).toHaveCount(0);
  await page.goto('/admin/products');
  await expect(page.getByRole('link', { name: 'Tạo sản phẩm' })).toHaveCount(0);
  expect(issues).toEqual([]);
});
