import { expect, test, type Page } from '@playwright/test';
import { collectConsoleIssues, loginAdmin, stubImageProvider, tinyPng } from './catalog-admin-flow';

interface BrandApiItem {
  id: string;
  name: string;
  logoPublicId: string | null;
  logoUrl: string | null;
}

async function readBrandByName(page: Page, name: string) {
  // Đọc lại API sau khi lưu để không chỉ kiểm tra state của trình duyệt.
  const response = await page.request.get('/api/brands');
  expect(response.ok()).toBe(true);
  const body = (await response.json()) as { data: BrandApiItem[] };
  return body.data.find((brand) => brand.name === name);
}

test('quản trị viên tạo và thay logo thương hiệu bằng file upload', async ({ page }, testInfo) => {
  test.setTimeout(120000);
  const consoleIssues = collectConsoleIssues(page);
  const suffix = Date.now().toString(36);
  const name = `Brand Upload ${suffix}`;
  const imageFile = { name: 'logo.png', mimeType: 'image/png', buffer: tinyPng };

  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAdmin(page);
  const imageProvider = await stubImageProvider(page, `logo-${suffix}`, 'pickory/brands');
  await page.goto('/admin/brands');
  await page.getByRole('button', { name: '+ Thêm thương hiệu' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByLabel('Mã logo Cloudinary (tùy chọn)')).toHaveCount(0);
  await dialog.getByLabel('Tên thương hiệu *').fill(name);

  await dialog.getByLabel('Chọn ảnh từ máy').setInputFiles(imageFile);

  const createPreview = dialog.getByRole('img', { name: `Logo của ${name}` });
  await expect(createPreview).toBeVisible();
  await expect.poll(() => imageProvider.uploadCount()).toBe(1);
  await expect(dialog.getByRole('button', { name: 'Tạo thương hiệu' })).toBeEnabled();
  await page.screenshot({
    path: testInfo.outputPath('brand-logo-create-1440.png'),
    fullPage: true,
  });
  await dialog.getByRole('button', { name: 'Tạo thương hiệu' }).click();
  await expect(dialog).toBeHidden();

  const created = await readBrandByName(page, name);
  expect(created).toBeDefined();
  expect(created!.logoPublicId).toMatch(/^pickory\/brands\/logo-/);
  expect(created!.logoUrl).toContain(created!.logoPublicId);

  const searchBox = page.getByLabel('Tìm theo tên hoặc slug thương hiệu', { exact: true });
  await searchBox.fill(name);
  await searchBox.press('Enter');
  await expect(page.locator('table.tbl tbody tr')).toHaveCount(1);
  await page.getByRole('button', { name: `Sửa ${name}`, exact: true }).click();
  const firstPreviewUrl = await createPreview.getAttribute('src');
  await page.setViewportSize({ width: 375, height: 812 });

  await dialog.getByLabel('Đổi ảnh').setInputFiles(imageFile);

  await expect.poll(() => imageProvider.uploadCount()).toBe(2);
  await expect(createPreview).not.toHaveAttribute('src', firstPreviewUrl!);
  await expect(dialog.getByRole('button', { name: 'Lưu thay đổi' })).toBeEnabled();
  const widths = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth,
  }));
  expect(widths.content).toBeLessThanOrEqual(widths.viewport);
  await page.screenshot({
    path: testInfo.outputPath('brand-logo-edit-375.png'),
    fullPage: true,
  });
  await dialog.getByRole('button', { name: 'Lưu thay đổi' }).click();
  await expect(dialog).toBeHidden();

  const updated = await readBrandByName(page, name);
  expect(updated!.logoPublicId).toMatch(/^pickory\/brands\/logo-/);
  expect(updated!.logoPublicId).not.toBe(created!.logoPublicId);
  expect(consoleIssues).toEqual([]);
});
