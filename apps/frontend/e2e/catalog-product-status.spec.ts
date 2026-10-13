import { expect, test, type Page } from '@playwright/test';
import { expectListAlertStyle } from './alert-assertions';
import { e2eEnvironment } from './environment';
import { readPasswordSetupLink } from './outbox';

// Ảnh PNG 1x1 trả cho mọi yêu cầu ảnh thu nhỏ, để trình duyệt không ghi lỗi tải ảnh vào console.
const tinyPng = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64',
);

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

interface CatalogRefs {
  categoryId: string;
  brandId: string;
  suffix: string;
}

// Tạo danh mục và thương hiệu bằng API trong phiên admin, để spec chỉ tập trung vào khối trạng thái.
async function createCatalogRefs(page: Page): Promise<CatalogRefs> {
  const headers = { Origin: e2eEnvironment.webOrigin };
  const suffix = Date.now().toString(36);
  const category = await page.request.post('/api/admin/categories', {
    headers,
    data: { name: `Trạng thái C5b ${suffix}`, parentId: null, position: 0, attributes: [] },
  });
  expect(category.status()).toBe(201);
  const brand = await page.request.post('/api/admin/brands', {
    headers,
    data: { name: `Hãng trạng thái C5b ${suffix}` },
  });
  expect(brand.status()).toBe(201);
  return {
    categoryId: (await category.json()).data.id,
    brandId: (await brand.json()).data.id,
    suffix,
  };
}

interface CreatedProduct {
  id: string;
  name: string;
}

// Sản phẩm `draft` một SKU, chưa có ảnh và chưa có cân nặng, nên chưa đủ điều kiện đăng bán.
async function createDraftProduct(
  page: Page,
  refs: CatalogRefs,
  label: string,
): Promise<CreatedProduct> {
  const name = `Máy ${label} C5b ${refs.suffix}`;
  const response = await page.request.post('/api/admin/products', {
    headers: { Origin: e2eEnvironment.webOrigin },
    data: {
      categoryId: refs.categoryId,
      brandId: refs.brandId,
      name,
      description: { type: 'doc', content: [{ type: 'paragraph' }] },
      specs: {},
      options: [],
      variants: [{ optionValues: {}, price: 100000, enabled: true }],
    },
  });
  expect(response.status()).toBe(201);
  return { id: (await response.json()).data.id as string, name };
}

const statusBadge = (page: Page) => page.locator('.status-current .badge');

test('UI-C5b: đăng bán, ẩn, bán lại, xóa sản phẩm và quyền của sales', async ({
  page,
}, testInfo) => {
  test.setTimeout(180000);
  // Ghi lỗi console và lỗi trang. Spec phải có 0 lỗi và 0 cảnh báo, ngoài các lỗi 422 mà test chủ động gây ra.
  const issues: string[] = [];
  let expected422Logs = 0;
  page.on('console', (message) => {
    if (message.location().url.endsWith('/favicon.ico')) return;
    // Trình duyệt tự ghi dòng "Failed to load resource" cho mỗi phản hồi 422 của bước đăng bán thiếu điều kiện.
    if (
      message.type() === 'error' &&
      message.location().url.endsWith('/status') &&
      message.text().includes('status of 422')
    ) {
      expected422Logs += 1;
      return;
    }
    if (message.type() === 'error' || message.type() === 'warning') {
      issues.push(`${message.type()}: ${message.text()}`);
    }
  });
  page.on('pageerror', (error) => issues.push(`pageerror: ${error.message}`));

  // E2E không gọi Cloudinary thật: trả kết quả tải ảnh giả và ảnh thu nhỏ 1x1.
  let uploadCount = 0;
  await page.route('https://api.cloudinary.com/**', async (route) => {
    uploadCount += 1;
    const publicId = `pickory/products/e2e-c5b-${Date.now()}-${uploadCount}`;
    await route.fulfill({
      json: {
        public_id: publicId,
        secure_url: `https://res.cloudinary.com/pickory-fake/image/upload/${publicId}`,
      },
    });
  });
  await page.route('https://res.cloudinary.com/**', (route) =>
    route.fulfill({ body: tinyPng, contentType: 'image/png' }),
  );

  await loginAdmin(page);
  const refs = await createCatalogRefs(page);
  const publishedProduct = await createDraftProduct(page, refs, 'đăng bán');
  const doomedProduct = await createDraftProduct(page, refs, 'sẽ xóa');
  const productUrl = `/admin/products/${publishedProduct.id}`;

  await page.goto(productUrl);
  await expect(page.getByRole('heading', { name: 'Trạng thái', exact: true })).toBeVisible();
  await expect(statusBadge(page)).toHaveText('Bản nháp');
  // Bản nháp chỉ chuyển được sang đang bán. Không có nút ẩn hay ngừng kinh doanh.
  await expect(page.getByRole('button', { name: 'Đăng bán', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Ẩn sản phẩm', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Ngừng kinh doanh', exact: true })).toHaveCount(0);

  await test.step('Đăng bán khi thiếu ảnh và cân nặng hiện lỗi 422 với từng lý do', async () => {
    await page.getByRole('button', { name: 'Đăng bán', exact: true }).click();
    const alert = page.locator('.status-panel').getByRole('alert');
    await expect(alert).toContainText('Sản phẩm chưa đủ điều kiện để đăng bán.');
    await expect(alert.getByRole('listitem')).toHaveCount(2);
    await expect(alert).toContainText('Sản phẩm phải có ít nhất 1 ảnh để đăng bán.');
    await expect(alert).toContainText('thiếu cân nặng hoặc kích thước đóng gói');
    await expect(statusBadge(page)).toHaveText('Bản nháp');
    await expectListAlertStyle(alert);
    expect(expected422Logs).toBe(1);
    await page
      .locator('.status-panel')
      .screenshot({ path: testInfo.outputPath('status-422-1280.png') });
  });

  await test.step('Thêm ảnh, nhập số đo và lưu: nút trạng thái khóa khi chưa lưu', async () => {
    await page
      .locator('input[type="file"]')
      .setInputFiles([
        { name: 'mat-truoc.jpg', mimeType: 'image/jpeg', buffer: Buffer.alloc(2048) },
      ]);
    await expect(page.locator('[data-image-id]')).toHaveCount(1);
    await page.getByLabel('Mô tả ảnh 1 *').fill('Mặt trước');
    await page.getByLabel('Cân nặng (g) Mặc định').fill('450');
    await page.getByLabel('Dài (cm) Mặc định').fill('20');
    await page.getByLabel('Rộng (cm) Mặc định').fill('10');
    await page.getByLabel('Cao (cm) Mặc định').fill('5');

    // Chưa lưu: API kiểm điều kiện trên dữ liệu đã lưu nên nút bị khóa kèm câu hướng dẫn.
    const publishButton = page.getByRole('button', { name: 'Đăng bán', exact: true });
    await expect(publishButton).toBeDisabled();
    await expect(page.getByText('Lưu thay đổi trước khi đổi trạng thái.')).toBeVisible();
    await page
      .locator('.status-panel')
      .screenshot({ path: testInfo.outputPath('status-unsaved-1280.png') });

    const saved = page.waitForResponse(
      (response) =>
        response.request().method() === 'PATCH' &&
        response.url().endsWith(`/api/admin/products/${publishedProduct.id}`),
    );
    await page.getByRole('button', { name: 'Lưu sản phẩm', exact: true }).click();
    expect((await saved).status()).toBe(200);
    await expect(publishButton).toBeEnabled();
    await expect(page.getByText('Lưu thay đổi trước khi đổi trạng thái.')).toHaveCount(0);
  });

  await test.step('Đăng bán đổi nhãn thành Đang bán mà không tải lại trang', async () => {
    // Cờ trên `window` mất nếu trang điều hướng hoặc tải lại.
    await page.evaluate(() => {
      (window as unknown as { __noReload: boolean }).__noReload = true;
    });
    await page.getByRole('button', { name: 'Đăng bán', exact: true }).click();
    await expect(statusBadge(page)).toHaveText('Đang bán');
    await expect(
      page.getByRole('status').filter({ hasText: 'Đã đổi trạng thái thành "Đang bán".' }),
    ).toBeVisible();
    expect(
      await page.evaluate(() => (window as unknown as { __noReload?: boolean }).__noReload),
    ).toBe(true);
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}${productUrl}`);
    // Đang bán chỉ chuyển được sang ẩn hoặc ngừng kinh doanh.
    await expect(page.getByRole('button', { name: 'Đăng bán', exact: true })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Ẩn sản phẩm', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Ngừng kinh doanh', exact: true })).toBeVisible();
    await page
      .locator('.status-panel')
      .screenshot({ path: testInfo.outputPath('status-active-1280.png') });
  });

  await test.step('Danh sách sản phẩm hiện Đang bán cho sản phẩm này', async () => {
    await page.getByRole('link', { name: 'Về danh sách', exact: true }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/products`);
    await page.getByLabel('Tên hoặc mã SKU').fill(publishedProduct.name);
    const row = page.locator('tbody tr').filter({ hasText: publishedProduct.name });
    await expect(row).toHaveCount(1);
    await expect(row.locator('.badge')).toHaveText('Đang bán');
    await page.screenshot({ path: testInfo.outputPath('list-active-1280.png') });
    await row.getByRole('link', { name: 'Chỉnh sửa', exact: true }).click();
    await expect(statusBadge(page)).toHaveText('Đang bán');
  });

  await test.step('Ẩn sản phẩm rồi bán lại: nhãn đổi đúng mỗi lần', async () => {
    await page.getByRole('button', { name: 'Ẩn sản phẩm', exact: true }).click();
    await expect(statusBadge(page)).toHaveText('Đã ẩn');
    // Từ đã ẩn, nút đăng bán đổi tên thành "Bán lại".
    await expect(page.getByRole('button', { name: 'Đăng bán', exact: true })).toHaveCount(0);
    await page.getByRole('button', { name: 'Bán lại', exact: true }).click();
    await expect(statusBadge(page)).toHaveText('Đang bán');
    await expect(page.getByRole('button', { name: 'Bán lại', exact: true })).toHaveCount(0);
  });

  await test.step('Trên 375 px khối trạng thái, lỗi 422 và hộp thoại xóa không làm trang tràn ngang', async () => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto(`/admin/products/${doomedProduct.id}`);
    await expect(statusBadge(page)).toHaveText('Bản nháp');
    await page.getByRole('button', { name: 'Đăng bán', exact: true }).click();
    await expect(page.locator('.status-panel').getByRole('alert')).toContainText(
      'Sản phẩm phải có ít nhất 1 ảnh để đăng bán.',
    );
    expect(expected422Logs).toBe(2);
    await expectNoHorizontalPageOverflow(page);
    await page
      .locator('.status-panel')
      .screenshot({ path: testInfo.outputPath('status-422-375.png') });

    await page.getByRole('button', { name: 'Xóa sản phẩm', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toContainText(doomedProduct.name);
    await expect(dialog).toContainText('không thể hoàn tác');
    await expectNoHorizontalPageOverflow(page);
    await page.screenshot({ path: testInfo.outputPath('delete-dialog-375.png') });
    await dialog.getByRole('button', { name: 'Hủy', exact: true }).click();
    await expect(dialog).toBeHidden();
    await page.setViewportSize({ width: 1280, height: 720 });
  });

  await test.step('Xóa sản phẩm bản nháp qua hộp thoại xác nhận', async () => {
    await page.getByRole('button', { name: 'Xóa sản phẩm', exact: true }).click();
    const dialog = page.getByRole('dialog');
    // Focus ở nút Hủy để phím Enter không xóa nhầm.
    await expect(dialog.getByRole('button', { name: 'Hủy', exact: true })).toBeFocused();
    await page.screenshot({ path: testInfo.outputPath('delete-dialog-1280.png') });
    const deleted = page.waitForResponse(
      (response) =>
        response.request().method() === 'DELETE' &&
        response.url().endsWith(`/api/admin/products/${doomedProduct.id}`),
    );
    await dialog.getByRole('button', { name: 'Xóa vĩnh viễn', exact: true }).click();
    expect((await deleted).status()).toBe(204);
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/products`);
    await page.getByLabel('Tên hoặc mã SKU').fill(doomedProduct.name);
    await expect(page.getByText('Không có kết quả phù hợp với bộ lọc.')).toBeVisible();
    await expect(page.locator('tbody tr').filter({ hasText: doomedProduct.name })).toHaveCount(0);
  });

  await test.step('Sales chỉ có products:read thấy nhãn, không thấy nút trạng thái và nút xóa', async () => {
    await page.goto('/admin/staff');
    await page.getByRole('button', { name: '+ Mời nhân viên', exact: true }).click();
    const invitation = page.getByRole('dialog', { name: 'Mời nhân viên mới', exact: true });
    const salesEmail = e2eEnvironment.productStatusSalesEmail;
    await invitation.getByLabel(/^Email nhân viên/).fill(salesEmail);
    await invitation.getByLabel(/^Họ và tên/).fill('Sales Trạng Thái E2E');
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
    await page.goto(productUrl);
    await expect(statusBadge(page)).toHaveText('Đang bán');
    await expect(page.locator('.status-panel button')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Xóa sản phẩm' })).toHaveCount(0);
    await page
      .locator('.status-panel')
      .screenshot({ path: testInfo.outputPath('status-sales-1280.png') });
  });

  expect(expected422Logs).toBe(2);
  expect(issues).toEqual([]);
});
