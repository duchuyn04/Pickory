import { randomBytes } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';
import {
  collectConsoleIssues,
  createBrand,
  createCategory,
  dismissConsent,
  loginAdmin,
  stubImageProvider,
} from './catalog-admin-flow';
import { e2eEnvironment } from './environment';

// Hậu tố riêng của lần chạy. Mọi tên, slug và mã SKU mang hậu tố này nên lần chạy sau không đụng dữ liệu lần trước.
const runId = `${Date.now().toString(36)}${randomBytes(2).toString('hex')}`;

const categoryName = `Máy ảnh E2E ${runId}`;
const brandName = `Hãng E2E ${runId}`;
const productName = `Máy ảnh mirrorless E2E ${runId}`;
const headingOne = `Giới thiệu E2E ${runId}`;
const headingTwo = `Thông số chính E2E ${runId}`;
const boldText = 'Cảm biến full frame';
// Sản phẩm thứ hai khác phân khúc, để bộ lọc có việc phải loại bớt.
const otherProductName = `Máy ảnh compact E2E ${runId}`;

// Giá của từng SKU do admin nhập. Khách phải thấy đúng giá này khi chọn đúng tổ hợp.
const variantPrices: { color: string; storage: string; price: number }[] = [
  { color: 'Đen', storage: '64GB', price: 120000 },
  { color: 'Đen', storage: '128GB', price: 135000 },
  { color: 'Bạc', storage: '64GB', price: 150000 },
  { color: 'Bạc', storage: '128GB', price: 175000 },
];

// Giá hiển thị theo định dạng của trang khách, ví dụ "120.000 đ".
const formatPrice = (price: number) => `${String(price).replace(/\B(?=(\d{3})+(?!\d))/g, '.')} đ`;

// Tạo và đăng bán một sản phẩm một SKU, cùng đường giao diện với sản phẩm chính.
async function createSimpleProduct(
  page: Page,
  name: string,
  tier: string,
  imageProvider: { uploadCount: () => number },
) {
  await page.getByRole('link', { name: 'Sản phẩm', exact: true }).click();
  await page.getByRole('link', { name: 'Tạo sản phẩm' }).click();
  await page.getByLabel('Tên sản phẩm').fill(name);
  await page.getByLabel('Danh mục lá').selectOption({ label: categoryName });
  await page.getByLabel('Thương hiệu').selectOption({ label: brandName });
  await page.getByLabel('Phân khúc').selectOption(tier);
  await page.getByLabel('Xuất xứ *').fill('Thái Lan');
  await expect(page.locator('[data-variant-row]')).toHaveCount(1);
  await page.locator('input[name="variant-price-0"]').fill('90000');
  await page.locator('input[name="variant-weight-grams-0"]').fill('300');
  await page.locator('input[name="variant-length-cm-0"]').fill('10');
  await page.locator('input[name="variant-width-cm-0"]').fill('8');
  await page.locator('input[name="variant-height-cm-0"]').fill('5');
  await page.getByRole('button', { name: 'Lưu sản phẩm' }).click();
  await expect(page).toHaveURL(/\/admin\/products\/[0-9a-f]{32}$/);

  // Chờ form sửa tải xong trước khi chọn file, vì ô chọn file chỉ hoạt động trên sản phẩm đã lưu.
  await expect(page.locator('.status-current .badge')).toHaveText('Bản nháp');
  const uploadsBefore = imageProvider.uploadCount();
  await page.locator('input[type="file"]').setInputFiles({
    name: 'compact.jpg',
    mimeType: 'image/jpeg',
    buffer: Buffer.alloc(2048),
  });
  await expect(page.locator('[data-image-id]')).toHaveCount(1);
  expect(imageProvider.uploadCount()).toBe(uploadsBefore + 1);
  await page.getByLabel('Mô tả ảnh 1 *').fill(`Mặt trước ${name}`);
  await page.getByRole('button', { name: 'Lưu sản phẩm' }).click();
  await page.getByRole('button', { name: 'Đăng bán' }).click();
  await expect(page.locator('.status-current .badge')).toHaveText('Đang bán');
}

test('Luồng catalog: admin tạo và đăng bán sản phẩm, khách lọc và chọn biến thể', async ({
  page,
  browser,
}) => {
  test.setTimeout(180000);
  const adminIssues = collectConsoleIssues(page);
  const imageProvider = await stubImageProvider(page, `e2e-${runId}`);

  await loginAdmin(page);

  // Danh mục có một thuộc tính `enum` lọc được và một thuộc tính bắt buộc.
  await createCategory(page, categoryName, [
    {
      key: 'tier',
      label: 'Phân khúc',
      group: 'Thông tin',
      type: 'enum',
      options: 'Cao cấp, Phổ thông',
      filterable: true,
    },
    { key: 'origin', label: 'Xuất xứ', group: 'Thông tin', required: true },
  ]);
  await createBrand(page, brandName);

  await page.getByRole('link', { name: 'Sản phẩm', exact: true }).click();
  await page.getByRole('link', { name: 'Tạo sản phẩm' }).click();
  await page.getByLabel('Tên sản phẩm').fill(productName);
  await page.getByLabel('Danh mục lá').selectOption({ label: categoryName });
  await page.getByLabel('Thương hiệu').selectOption({ label: brandName });
  await page.getByLabel('Phân khúc').selectOption('Cao cấp');
  await page.getByLabel('Xuất xứ *').fill('Nhật Bản');

  // Hai tùy chọn tạo 4 SKU. Mỗi SKU có giá riêng và đủ số đo đóng gói để đăng bán được.
  await page.getByRole('button', { name: 'Thêm trục' }).click();
  await page.getByLabel('Tên trục 1').fill('Màu');
  await page.getByLabel('Giá trị trục 1').fill('Đen, Bạc');
  await page.getByRole('button', { name: 'Thêm trục' }).click();
  await page.getByLabel('Tên trục 2').fill('Bộ nhớ');
  await page.getByLabel('Giá trị trục 2').fill('64GB, 128GB');
  await expect(page.locator('[data-variant-row]')).toHaveCount(variantPrices.length);
  for (const variant of variantPrices) {
    const combination = `${variant.color} / ${variant.storage}`;
    const row = page.locator('[data-variant-row]').filter({ hasText: combination });
    await row.getByLabel(`Giá bán ${combination}`).fill(String(variant.price));
    await row.locator('input[name^="variant-weight-grams-"]').fill('650');
    await row.locator('input[name^="variant-length-cm-"]').fill('15');
    await row.locator('input[name^="variant-width-cm-"]').fill('12');
    await row.locator('input[name^="variant-height-cm-"]').fill('8');
  }

  // Mô tả soạn bằng thanh công cụ: tiêu đề cấp 1, tiêu đề cấp 2 và một đoạn có chữ đậm.
  const editor = page.getByRole('textbox', { name: 'Nội dung mô tả chi tiết sản phẩm' });
  await editor.click();
  await page.getByRole('button', { name: 'Tiêu đề 1', exact: true }).click();
  await page.keyboard.type(headingOne);
  await page.keyboard.press('Enter');
  await page.getByRole('button', { name: 'Tiêu đề 2', exact: true }).click();
  await page.keyboard.type(headingTwo);
  await page.keyboard.press('Enter');
  await page.getByRole('button', { name: 'Đậm', exact: true }).click();
  await page.keyboard.type(boldText);
  await page.getByRole('button', { name: 'Đậm', exact: true }).click();
  await page.keyboard.type(' cho ảnh sắc nét.');
  await expect(editor.locator('h1')).toHaveText(headingOne);
  await expect(editor.locator('h2')).toHaveText(headingTwo);
  await expect(editor.locator('strong')).toHaveText(boldText);

  await page.getByRole('button', { name: 'Lưu sản phẩm' }).click();
  await expect(page).toHaveURL(/\/admin\/products\/[0-9a-f]{32}$/);
  await expect(page.locator('.status-current .badge')).toHaveText('Bản nháp');

  // Tải ảnh qua dịch vụ ảnh giả bằng ô chọn file, rồi lưu để API thấy ảnh khi đăng bán.
  await page.locator('input[type="file"]').setInputFiles({
    name: 'may-anh.jpg',
    mimeType: 'image/jpeg',
    buffer: Buffer.alloc(2048),
  });
  await expect(page.locator('[data-image-id]')).toHaveCount(1);
  expect(imageProvider.uploadCount()).toBe(1);
  await page.getByLabel('Mô tả ảnh 1 *').fill(`Mặt trước ${productName}`);
  await page.getByRole('button', { name: 'Lưu sản phẩm' }).click();
  await expect(page.getByRole('button', { name: 'Đăng bán' })).toBeEnabled();
  await page.getByRole('button', { name: 'Đăng bán' }).click();
  await expect(page.locator('.status-current .badge')).toHaveText('Đang bán');
  await createSimpleProduct(page, otherProductName, 'Phổ thông', imageProvider);
  expect(adminIssues).toEqual([]);

  // Khách là một context mới không đăng nhập, không mang cookie hay storage của admin.
  const customerContext = await browser.newContext({
    baseURL: e2eEnvironment.webOrigin,
    viewport: { width: 1280, height: 800 },
  });
  const customerPage = await customerContext.newPage();
  const customerIssues = collectConsoleIssues(customerPage);
  await stubImageProvider(customerPage, `e2e-${runId}`);

  // Slug của danh mục lấy từ API công khai, vì khách chỉ có link `/c/{slug}`.
  const categoriesResponse = await customerPage.request.get('/api/categories');
  const categories = (await categoriesResponse.json()).data as { name: string; slug: string }[];
  const category = categories.find((item) => item.name === categoryName);
  expect(category, `Không thấy danh mục ${categoryName} trong API công khai`).toBeDefined();

  await customerPage.goto(`/c/${category?.slug}`);
  await dismissConsent(customerPage);
  await expect(customerPage.getByRole('heading', { level: 1 })).toHaveText(categoryName);
  await expect(customerPage.locator('.catalog-head [role="status"]')).toHaveText('2 sản phẩm');

  // Facet hiện số đếm. Lọc theo giá trị `enum` loại sản phẩm khác phân khúc và vẫn thấy sản phẩm cần tìm.
  const tierGroup = customerPage.getByRole('group', { name: 'Phân khúc' });
  await expect(tierGroup.getByRole('checkbox', { name: 'Cao cấp (1)' })).toBeVisible();
  await expect(tierGroup.getByRole('checkbox', { name: 'Phổ thông (1)' })).toBeVisible();
  await tierGroup.getByRole('checkbox', { name: 'Cao cấp (1)' }).click();
  await expect(customerPage).toHaveURL(/attr\.tier=/);
  await expect(customerPage.locator('.catalog-head [role="status"]')).toHaveText('1 sản phẩm');
  await expect(customerPage.locator('.product-card-name')).toHaveText(productName);

  // Mở sản phẩm và chọn từng biến thể. Giá phải đúng giá admin đã nhập cho SKU đó.
  await customerPage.getByRole('link', { name: new RegExp(productName) }).click();
  await expect(customerPage.getByRole('heading', { level: 1 })).toHaveText(productName);
  for (const variant of variantPrices) {
    await customerPage
      .getByRole('group', { name: 'Màu' })
      .getByRole('radio', { name: variant.color })
      .click();
    await customerPage
      .getByRole('group', { name: 'Bộ nhớ' })
      .getByRole('radio', { name: variant.storage })
      .click();
    await expect(customerPage.locator('.product-price strong')).toHaveText(
      formatPrice(variant.price),
    );
  }

  // Mô tả: cấp 1 thành h2, cấp 2 thành h3, chữ đậm nằm trong strong. Cả trang chỉ có một h1.
  await expect(customerPage.getByRole('heading', { level: 1 })).toHaveCount(1);
  const description = customerPage.locator('.description-body');
  await expect(description.locator('h2')).toHaveText(headingOne);
  await expect(description.locator('h3')).toHaveText(headingTwo);
  await expect(description.locator('strong')).toHaveText(boldText);
  await expect(description.locator('h1')).toHaveCount(0);

  expect(customerIssues).toEqual([]);
  await customerContext.close();
});
