import { expect, test } from '@playwright/test';
import {
  collectConsoleIssues,
  createBrand,
  createCategory,
  loginAdmin,
  stubImageProvider,
} from './catalog-admin-flow';

declare global {
  interface Window {
    __checkpointC2NoReload?: boolean;
  }
}

test('Checkpoint C2: admin tạo danh mục, sản phẩm bốn SKU, ảnh và đăng bán', async ({ page }) => {
  test.setTimeout(120000);
  const consoleIssues = collectConsoleIssues(page);
  const imageProvider = await stubImageProvider(page, 'checkpoint-c2');
  await loginAdmin(page);

  // Danh mục lá có thuộc tính bắt buộc để việc đăng bán kiểm cả dữ liệu kỹ thuật.
  await createCategory(page, 'Điện thoại Checkpoint C2', [
    { key: 'origin', label: 'Xuất xứ', group: 'Thông tin', required: true },
  ]);
  await createBrand(page, 'Hãng Checkpoint C2');

  // Chọn danh mục và thương hiệu vừa tạo bằng nhãn, không lấy ID qua API.
  await page.getByRole('link', { name: 'Sản phẩm', exact: true }).click();
  await page.getByRole('link', { name: 'Tạo sản phẩm' }).click();
  await page.getByLabel('Tên sản phẩm').fill('Điện thoại Checkpoint C2');
  await page.getByLabel('Danh mục lá').selectOption({ label: 'Điện thoại Checkpoint C2' });
  await page.getByLabel('Thương hiệu').selectOption({ label: 'Hãng Checkpoint C2' });
  await page.getByLabel('Xuất xứ *').fill('Việt Nam');
  await page.getByRole('button', { name: 'Thêm trục' }).click();
  await page.getByLabel('Tên trục 1').fill('Màu');
  await page.getByLabel('Giá trị trục 1').fill('Đỏ, Xanh');
  await page.getByRole('button', { name: 'Thêm trục' }).click();
  await page.getByLabel('Tên trục 2').fill('Dung lượng');
  await page.getByLabel('Giá trị trục 2').fill('128GB, 256GB');
  await expect(page.locator('[data-variant-row]')).toHaveCount(4);
  for (let index = 0; index < 4; index += 1) {
    // Mỗi SKU phải đủ giá và số đo trước khi được phép đăng bán.
    const variant = page.locator('[data-variant-row]').nth(index);
    await variant.locator(`[name="variant-price-${index}"]`).fill(String(100000 + index * 10000));
    await variant.locator(`[name="variant-weight-grams-${index}"]`).fill('450');
    await variant.locator(`[name="variant-length-cm-${index}"]`).fill('20');
    await variant.locator(`[name="variant-width-cm-${index}"]`).fill('10');
    await variant.locator(`[name="variant-height-cm-${index}"]`).fill('5');
  }
  await page.getByRole('button', { name: 'Lưu sản phẩm' }).click();
  await expect(page).toHaveURL(/\/admin\/products\/[0-9a-f]{32}$/);
  const productUrl = page.url();
  await expect(page.locator('.status-current .badge')).toHaveText('Bản nháp');

  // Lưu ảnh và alt qua form trước khi đổi trạng thái, vì API chỉ xét dữ liệu đã lưu.
  await page.locator('input[type="file"]').setInputFiles({
    name: 'mat-truoc.jpg',
    mimeType: 'image/jpeg',
    buffer: Buffer.alloc(2048),
  });
  await expect(page.locator('[data-image-id]')).toHaveCount(1);
  expect(imageProvider.uploadCount()).toBe(1);
  await page.getByLabel('Mô tả ảnh 1 *').fill('Mặt trước điện thoại');
  await page.getByRole('button', { name: 'Lưu sản phẩm' }).click();
  await expect(page.getByRole('button', { name: 'Đăng bán' })).toBeEnabled();

  // Cờ tồn tại trong cùng tài liệu, nên mất cờ đồng nghĩa trang đã tải lại.
  await page.evaluate(() => {
    window.__checkpointC2NoReload = true;
  });
  await page.getByRole('button', { name: 'Đăng bán' }).click();
  await expect(page.locator('.status-current .badge')).toHaveText('Đang bán');
  expect(await page.evaluate(() => window.__checkpointC2NoReload)).toBe(true);
  await expect(page).toHaveURL(productUrl);

  await page.getByRole('link', { name: 'Về danh sách' }).click();
  await page.getByLabel('Tên hoặc mã SKU').fill('Điện thoại Checkpoint C2');
  const productRow = page.getByRole('row', { name: /Điện thoại Checkpoint C2/ });
  await expect(productRow.locator('.badge')).toHaveText('Đang bán');
  await productRow.getByRole('link', { name: 'Chỉnh sửa' }).click();
  // Chờ điều hướng kết thúc để `reload` tải form sửa thay vì tải lại danh sách.
  await expect(page).toHaveURL(productUrl);
  await page.reload();

  // Tải lại từ máy chủ để kiểm toàn bộ dữ liệu sống qua lần lưu và chuyển trạng thái.
  await expect(page.locator('.status-current .badge')).toHaveText('Đang bán');
  await expect(page.getByLabel('Tên trục 1')).toHaveValue('Màu');
  await expect(page.getByLabel('Giá trị trục 1')).toHaveValue('Đỏ, Xanh');
  await expect(page.getByLabel('Tên trục 2')).toHaveValue('Dung lượng');
  await expect(page.getByLabel('Giá trị trục 2')).toHaveValue('128GB, 256GB');
  await expect(page.getByLabel('Xuất xứ *')).toHaveValue('Việt Nam');
  await expect(page.locator('[data-variant-row]')).toHaveCount(4);
  for (let index = 0; index < 4; index += 1) {
    const variant = page.locator('[data-variant-row]').nth(index);
    await expect(variant.locator(`[name="variant-code-${index}"]`)).toHaveValue(/\S/);
    await expect(variant.locator(`[name="variant-price-${index}"]`)).toHaveValue(
      String(100000 + index * 10000),
    );
    await expect(variant.locator(`[name="variant-weight-grams-${index}"]`)).toHaveValue('450');
    await expect(variant.locator(`[name="variant-length-cm-${index}"]`)).toHaveValue('20');
    await expect(variant.locator(`[name="variant-width-cm-${index}"]`)).toHaveValue('10');
    await expect(variant.locator(`[name="variant-height-cm-${index}"]`)).toHaveValue('5');
  }
  await expect(page.locator('[data-image-id]')).toHaveCount(1);
  await expect(page.getByLabel('Mô tả ảnh 1 *')).toHaveValue('Mặt trước điện thoại');
  expect(consoleIssues).toEqual([]);
});
