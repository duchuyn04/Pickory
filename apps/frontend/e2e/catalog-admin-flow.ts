import { expect, type ConsoleMessage, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';

// Ảnh PNG 1x1 trả cho mọi yêu cầu ảnh sản phẩm, để trình duyệt không ghi lỗi tải ảnh vào console.
export const tinyPng = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64',
);

// Một lỗi tải mà test chủ động gây ra. Trình duyệt ghi dòng "Failed to load resource" kèm mã trạng thái cho phản hồi đó.
export interface AllowedLoadFailure {
  urlPattern: RegExp;
  status: number;
}

// Dòng console có đúng là dòng "Failed to load resource" của lỗi tải đã cho hay không.
export function isAllowedLoadFailure(message: ConsoleMessage, failure: AllowedLoadFailure) {
  return (
    message.type() === 'error' &&
    failure.urlPattern.test(message.location().url) &&
    message.text().includes(`status of ${failure.status}`)
  );
}

// Ghi lỗi console và lỗi trang, để mỗi test kết thúc với 0 lỗi và 0 cảnh báo.
// Chỉ bỏ qua lỗi tải khớp cả URL lẫn mã trạng thái trong `allowedFailures`.
export function collectConsoleIssues(
  page: Page,
  allowedFailures: AllowedLoadFailure[] = [],
): string[] {
  const issues: string[] = [];
  page.on('console', (message) => {
    // Chromium có giao diện tự tải favicon dù ứng dụng chưa cung cấp tệp này.
    if (message.location().url.endsWith('/favicon.ico')) return;
    // Bỏ qua lỗi tải mà test chủ động gây ra. Mọi dòng khác vẫn tính là lỗi.
    if (allowedFailures.some((failure) => isAllowedLoadFailure(message, failure))) return;
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
  folder = 'pickory/products',
): Promise<ImageProviderStub> {
  let uploadCount = 0;
  await page.route('https://api.cloudinary.com/**', async (route) => {
    uploadCount += 1;
    // Thư mục của ảnh giả phải khớp loại ảnh đang test để payload có thể được kiểm tra lại.
    const publicId = `${folder}/${publicIdPrefix}-${Date.now()}-${uploadCount}`;
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

interface AdminProductVariant {
  id: string;
  code: string;
  optionValues: Record<string, string>;
  price: number;
  compareAtPrice: number | null;
  stock: number;
  enabled: boolean;
  weightGrams: number | null;
  lengthCm: number | null;
  widthCm: number | null;
  heightCm: number | null;
}

interface AdminProductDetail {
  id: string;
  variants: AdminProductVariant[];
}

async function readAdminProduct(page: Page, productId: string): Promise<AdminProductDetail> {
  const response = await page.request.get(`/api/admin/products/${productId}`);
  expect(response.status(), await response.text()).toBe(200);
  return (await response.json()).data as AdminProductDetail;
}

// Đọc số bán được hiện tại của một biến thể qua API quản trị.
export async function readSkuStock(page: Page, productId: string, skuId: string): Promise<number> {
  const product = await readAdminProduct(page, productId);
  const variant = product.variants.find((candidate) => candidate.id === skuId);
  expect(variant, `Không thấy SKU ${skuId} trong sản phẩm ${productId}`).toBeTruthy();
  return variant!.stock;
}

// Đặt số lượng đích cho một biến thể. PATCH gửi đủ biến thể vì API yêu cầu cả danh sách.
export async function setSkuStock(
  page: Page,
  productId: string,
  skuId: string,
  stock: number,
): Promise<void> {
  const product = await readAdminProduct(page, productId);
  const variants = product.variants.map((variant) => ({
    optionValues: variant.optionValues,
    code: variant.code,
    price: variant.price,
    compareAtPrice: variant.compareAtPrice,
    stock: variant.id === skuId ? stock : variant.stock,
    enabled: variant.enabled,
    weightGrams: variant.weightGrams,
    lengthCm: variant.lengthCm,
    widthCm: variant.widthCm,
    heightCm: variant.heightCm,
  }));
  const response = await page.request.patch(`/api/admin/products/${productId}`, {
    headers: { Origin: e2eEnvironment.webOrigin },
    data: { variants },
  });
  expect(response.status(), await response.text()).toBe(200);
}
