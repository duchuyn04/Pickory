import { expect, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';

// Ảnh PNG 1x1 trả cho mọi yêu cầu ảnh sản phẩm, để trình duyệt không ghi lỗi tải ảnh vào console.
export const tinyPng = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64',
);

// Ghi lỗi console và lỗi trang. Mỗi test phải kết thúc với 0 lỗi và 0 cảnh báo.
export function collectConsoleIssues(page: Page): string[] {
  const issues: string[] = [];
  page.on('console', (message) => {
    // Chromium có giao diện tự tải favicon dù ứng dụng chưa cung cấp tệp này.
    if (message.location().url.endsWith('/favicon.ico')) return;
    if (message.type() === 'error' || message.type() === 'warning') {
      issues.push(`${message.type()}: ${message.text()}`);
    }
  });
  page.on('pageerror', (error) => issues.push(`pageerror: ${error.message}`));
  return issues;
}

export interface ImageProviderStub {
  // Số lần trang đã "tải ảnh lên" dịch vụ giả.
  uploadCount: () => number;
}

// Chỉ giả lập dịch vụ ảnh bên ngoài. Mọi thao tác ghi catalog vẫn đi qua giao diện thật.
export async function stubImageProvider(
  page: Page,
  publicIdPrefix: string,
): Promise<ImageProviderStub> {
  let uploadCount = 0;
  await page.route('https://api.cloudinary.com/**', async (route) => {
    uploadCount += 1;
    const publicId = `pickory/products/${publicIdPrefix}-${Date.now()}-${uploadCount}`;
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
  return { uploadCount: () => uploadCount };
}

export async function dismissConsent(page: Page) {
  const consent = page.getByRole('button', { name: 'Chỉ cần thiết' });
  if (await consent.isVisible()) await consent.click();
}

export async function loginAdmin(page: Page) {
  await page.goto('/admin/login');
  await dismissConsent(page);
  await page.getByLabel(/^Email/).fill(e2eEnvironment.adminEmail);
  await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.adminPassword);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin`);
}

export interface CategoryAttributeInput {
  key: string;
  label: string;
  group: string;
  // Mặc định là `text`, giống giá trị ban đầu của biểu mẫu.
  type?: 'text' | 'number' | 'enum' | 'boolean';
  // Danh sách giá trị cách nhau bằng dấu phẩy, chỉ dùng cho kiểu `enum`.
  options?: string;
  required?: boolean;
  filterable?: boolean;
}

// Tạo danh mục gốc kèm bộ thuộc tính qua hộp thoại của trang quản trị.
export async function createCategory(
  page: Page,
  name: string,
  attributes: CategoryAttributeInput[],
) {
  await page.getByRole('link', { name: 'Danh mục', exact: true }).click();
  await page.getByRole('button', { name: '+ Thêm danh mục gốc' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Tên danh mục *').fill(name);
  for (const [index, attribute] of attributes.entries()) {
    await dialog.getByRole('button', { name: '+ Thêm thuộc tính' }).click();
    // Mỗi thuộc tính có id riêng theo vị trí, nên không nhầm ô của các thuộc tính khác nhau.
    await dialog.locator(`#attr-${index}-key`).fill(attribute.key);
    await dialog.locator(`#attr-${index}-label`).fill(attribute.label);
    await dialog.locator(`#attr-${index}-group`).fill(attribute.group);
    if (attribute.type) await dialog.locator(`#attr-${index}-type`).selectOption(attribute.type);
    if (attribute.options) await dialog.locator(`#attr-${index}-options`).fill(attribute.options);
    if (attribute.required) {
      await dialog.getByRole('checkbox', { name: 'Bắt buộc' }).nth(index).check();
    }
    if (attribute.filterable) {
      await dialog.getByRole('checkbox', { name: 'Lọc được' }).nth(index).check();
    }
  }
  await dialog.getByRole('button', { name: 'Tạo danh mục' }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByText(name, { exact: true })).toBeVisible();
}

export async function createBrand(page: Page, name: string) {
  await page.getByRole('link', { name: 'Thương hiệu', exact: true }).click();
  await page.getByRole('button', { name: '+ Thêm thương hiệu' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Tên thương hiệu *').fill(name);
  await dialog.getByRole('button', { name: 'Tạo thương hiệu' }).click();
  await expect(dialog).toBeHidden();
}
