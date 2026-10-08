import { expect, test, type Page } from '@playwright/test';
import { expectListAlertStyle } from './alert-assertions';
import { e2eEnvironment } from './environment';

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

// Tạo sản phẩm bằng API trong phiên admin, để spec chỉ tập trung vào phần ảnh.
async function createProduct(page: Page): Promise<string> {
  const headers = { Origin: e2eEnvironment.webOrigin };
  const suffix = Date.now().toString(36);
  const category = await page.request.post('/api/admin/categories', {
    headers,
    data: { name: `Ảnh C6 ${suffix}`, parentId: null, position: 0, attributes: [] },
  });
  expect(category.status()).toBe(201);
  const brand = await page.request.post('/api/admin/brands', {
    headers,
    data: { name: `Hãng ảnh C6 ${suffix}` },
  });
  expect(brand.status()).toBe(201);
  const product = await page.request.post('/api/admin/products', {
    headers,
    data: {
      categoryId: (await category.json()).data.id,
      brandId: (await brand.json()).data.id,
      name: `Điện thoại ảnh C6 ${suffix}`,
      description: { type: 'doc', content: [{ type: 'paragraph' }] },
      specs: {},
      options: [],
      variants: [{ optionValues: {}, price: 100000, enabled: true }],
    },
  });
  expect(product.status()).toBe(201);
  return (await product.json()).data.id as string;
}

async function altValues(page: Page): Promise<string[]> {
  return page
    .locator('input[name^="image-alt-"]')
    .evaluateAll((inputs) => inputs.map((input) => (input as HTMLInputElement).value));
}

test('UI-C6: tải, kiểm, sắp xếp ảnh bằng bàn phím và lưu thứ tự', async ({ page }, testInfo) => {
  test.setTimeout(90000);
  // Ghi lỗi console và lỗi trang. Spec phải có 0 lỗi và 0 cảnh báo.
  const issues: string[] = [];
  page.on('console', (message) => {
    if (message.location().url.endsWith('/favicon.ico')) return;
    if (message.type() === 'error' || message.type() === 'warning') {
      issues.push(`${message.type()}: ${message.text()}`);
    }
  });
  page.on('pageerror', (error) => issues.push(`pageerror: ${error.message}`));

  // E2E không gọi Cloudinary thật: trả kết quả tải ảnh giả và ảnh thu nhỏ 1x1.
  let uploadCount = 0;
  await page.route('https://api.cloudinary.com/**', async (route) => {
    uploadCount += 1;
    const publicId = `pickory/products/e2e-c6-${Date.now()}-${uploadCount}`;
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
  const productId = await createProduct(page);
  await page.goto(`/admin/products/${productId}`);
  await expect(page.getByRole('heading', { name: 'Ảnh sản phẩm' })).toBeVisible();
  const fileInput = page.locator('input[type="file"]');

  await test.step('File sai định dạng hoặc trên 5 MB bị chặn trước khi tải', async () => {
    await fileInput.setInputFiles([
      { name: 'dong.gif', mimeType: 'image/gif', buffer: Buffer.from('GIF89a') },
      {
        name: 'qua-lon.jpg',
        mimeType: 'image/jpeg',
        buffer: Buffer.alloc(5 * 1024 * 1024 + 1),
      },
    ]);
    const alert = page.getByRole('alert').filter({ hasText: 'dong.gif' });
    await expect(alert).toContainText('Ảnh "dong.gif" phải có định dạng JPG, PNG hoặc WebP.');
    await expect(alert).toContainText('Ảnh "qua-lon.jpg" vượt quá 5 MB.');
    await expectListAlertStyle(alert);
    await alert.screenshot({ path: testInfo.outputPath('upload-errors-1280.png') });
    // Ở 375 px dòng lý do dài phải xuống hàng thẳng với chữ, không chui dưới icon.
    await page.setViewportSize({ width: 375, height: 812 });
    await alert.screenshot({ path: testInfo.outputPath('upload-errors-375.png') });
    await page.setViewportSize({ width: 1280, height: 720 });
    expect(uploadCount).toBe(0);
  });

  await test.step('Tải hai ảnh hợp lệ', async () => {
    await fileInput.setInputFiles([
      { name: 'mat-truoc.jpg', mimeType: 'image/jpeg', buffer: Buffer.alloc(2048) },
      { name: 'mat-sau.png', mimeType: 'image/png', buffer: Buffer.alloc(2048) },
    ]);
    await expect(page.locator('[data-image-id]')).toHaveCount(2);
    expect(uploadCount).toBe(2);
  });

  await test.step('Ảnh thiếu alt chặn lưu và báo lỗi dưới ô', async () => {
    await page.getByLabel('Mô tả ảnh 1 *').fill('Mặt trước');
    await page.getByRole('button', { name: 'Lưu sản phẩm' }).click();
    const secondAlt = page.getByLabel('Mô tả ảnh 2 *');
    await expect(secondAlt).toHaveAttribute('aria-invalid', 'true');
    await expect(page.locator('#image-alt-1-error')).toHaveText('Nhập mô tả ảnh (alt).');
    await secondAlt.fill('Mặt sau');
  });

  await test.step('Sắp xếp bằng bàn phím: Enter trên nút "Đưa ảnh 2 lên"', async () => {
    await page.getByRole('button', { name: 'Đưa ảnh 2 lên' }).focus();
    await page.keyboard.press('Enter');
    expect(await altValues(page)).toEqual(['Mặt sau', 'Mặt trước']);
    // Focus ở lại trên ảnh vừa di chuyển, để người dùng bàn phím di chuyển tiếp.
    const focusedImage = await page.evaluate(
      () => document.activeElement?.closest('[data-image-id]')?.querySelector('input')?.value,
    );
    expect(focusedImage).toBe('Mặt sau');
  });

  await test.step('Lưu rồi tải lại trang vẫn giữ thứ tự và alt', async () => {
    const saved = page.waitForResponse(
      (response) =>
        response.request().method() === 'PATCH' &&
        response.url().endsWith(`/api/admin/products/${productId}`),
    );
    await page.getByRole('button', { name: 'Lưu sản phẩm' }).click();
    expect((await saved).status()).toBe(200);
    await page.reload();
    await expect(page.locator('[data-image-id]')).toHaveCount(2);
    expect(await altValues(page)).toEqual(['Mặt sau', 'Mặt trước']);
  });

  await test.step('Ở 1280px và 375px, khối ảnh không làm trang tràn ngang', async () => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.getByRole('heading', { name: 'Ảnh sản phẩm' }).scrollIntoViewIfNeeded();
    await page.screenshot({ path: testInfo.outputPath('images-1280.png'), fullPage: true });
    await page.setViewportSize({ width: 375, height: 812 });
    const widths = await page.evaluate(() => ({
      viewport: document.documentElement.clientWidth,
      content: document.documentElement.scrollWidth,
    }));
    expect(widths.content).toBeLessThanOrEqual(widths.viewport);
    await page.screenshot({ path: testInfo.outputPath('images-375.png'), fullPage: true });
  });

  expect(issues).toEqual([]);
});
