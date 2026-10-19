import { expect, type Page } from '@playwright/test';
import { tinyPng } from './catalog-admin-flow';
import { e2eEnvironment } from './environment';

// Dữ liệu dùng chung của các spec đơn back-office. Gồm sản phẩm có tồn kho, kho gửi và một đơn COD đặt qua giao diện thật.

const adminHeaders = { Origin: e2eEnvironment.webOrigin };

// Trang không được rộng hơn khung nhìn. Bảng rộng phải cuộn trong khung riêng của nó.
export async function expectNoHorizontalOverflow(page: Page) {
  const widths = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth,
  }));
  expect(widths.content).toBeLessThanOrEqual(widths.viewport);
}

export interface SeededProduct {
  id: string;
  name: string;
  slug: string;
  skuId: string;
  price: number;
}

export interface AddressChoice {
  provinceName: string;
  wardName: string;
}

// Tạo sản phẩm đang bán có tồn kho bằng API admin để khách đặt được đơn COD qua luồng thật. Spec cần khóa phí đã lưu riêng thì truyền giá khác giá mặc định.
export async function seedProduct(page: Page, price = 450_000): Promise<SeededProduct> {
  const suffix = Date.now().toString(36);
  const category = await page.request.post('/api/admin/categories', {
    headers: adminHeaders,
    data: { name: `Đơn admin ${suffix}`, parentId: null, position: 0, attributes: [] },
  });
  expect(category.status(), await category.text()).toBe(201);
  const brand = await page.request.post('/api/admin/brands', {
    headers: adminHeaders,
    data: { name: `Hãng đơn admin ${suffix}` },
  });
  expect(brand.status(), await brand.text()).toBe(201);

  const name = `Chuột không dây ${suffix}`;
  const created = await page.request.post('/api/admin/products', {
    headers: adminHeaders,
    data: {
      categoryId: (await category.json()).data.id,
      brandId: (await brand.json()).data.id,
      name,
      description: { type: 'doc', content: [{ type: 'paragraph' }] },
      warrantyMonths: 12,
      specs: {},
      options: [],
      variants: [
        {
          optionValues: {},
          price,
          compareAtPrice: null,
          enabled: true,
          weightGrams: 200,
          lengthCm: 12,
          widthCm: 8,
          heightCm: 4,
        },
      ],
      images: [{ publicId: `pickory/products/e2e-admin-orders-${suffix}`, alt: name }],
    },
  });
  expect(created.status(), await created.text()).toBe(201);
  const product = (await created.json()).data as {
    id: string;
    slug: string;
    variants: { id: string }[];
  };

  const skuId = product.variants[0]!.id;
  const stocked = await page.request.post(`/api/admin/skus/${skuId}/movements`, {
    headers: adminHeaders,
    data: { delta: 50, reason: 'Nhập kho E2E đơn admin' },
  });
  expect(stocked.status(), await stocked.text()).toBe(201);
  const published = await page.request.put(`/api/admin/products/${product.id}/status`, {
    headers: adminHeaders,
    data: { status: 'active' },
  });
  expect(published.status(), await published.text()).toBe(200);

  return { id: product.id, name, slug: product.slug, skuId, price };
}

export interface ShippingAddressChoice extends AddressChoice {
  provinceCode: string;
  wardCode: string;
}

// Đọc tỉnh và phường đầu tiên của API địa chỉ. Mọi spec chọn cùng một địa chỉ, nên GHN giả tính được phí giống nhau.
export async function readFirstShippingAddress(page: Page): Promise<ShippingAddressChoice> {
  const provincesResponse = await page.request.get('/api/addresses/provinces');
  // Kiểm mã trạng thái trước khi đọc JSON, để lỗi API báo đúng chỗ thay vì lỗi đọc dữ liệu rỗng.
  expect(provincesResponse.status(), await provincesResponse.text()).toBe(200);
  const province = ((await provincesResponse.json()).data as { code: string; name: string }[])[0]!;

  const wardsResponse = await page.request.get(
    `/api/addresses/wards?provinceCode=${encodeURIComponent(province.code)}`,
  );
  expect(wardsResponse.status(), await wardsResponse.text()).toBe(200);
  const ward = ((await wardsResponse.json()).data as { code: string; name: string }[])[0]!;

  return {
    provinceCode: province.code,
    provinceName: province.name,
    wardCode: ward.code,
    wardName: ward.name,
  };
}

// Cấu hình kho gửi bằng địa chỉ đầu tiên để tính được phí vận chuyển. Trả tên tỉnh và phường để khách chọn khi đặt hàng.
export async function prepareShipping(page: Page): Promise<AddressChoice> {
  const { provinceCode, provinceName, wardCode, wardName } = await readFirstShippingAddress(page);

  const settings = await page.request.put('/api/admin/shipping/settings', {
    headers: adminHeaders,
    data: {
      senderName: 'Kho Pickory E2E',
      senderPhone: '0912345678',
      provinceCode,
      wardCode,
      line: '12 Phố Kho',
      defaultServiceTypeId: 2,
    },
  });
  expect(settings.status(), await settings.text()).toBe(200);
  return { provinceName, wardName };
}

// Chữ gõ vào `AddressPicker` chỉ lọc danh sách, không thành giá trị. Hàm bấm đúng dòng có tên cần chọn, để không phụ thuộc dòng nào đang sáng.
export async function chooseOption(page: Page, inputId: string, name: string) {
  const input = page.locator(`#${inputId}`);
  await expect(input).toBeEnabled();
  await input.fill(name);
  await page.getByRole('option', { name, exact: true }).click();
  await expect(input).toHaveValue(name);
}

// Khách vãng lai đặt một đơn COD qua giao diện thật và trả mã đơn. GHN dùng `FakeGhnClient` nên không gọi mạng.
export async function placeCodOrder(
  page: Page,
  product: SeededProduct,
  address: AddressChoice,
): Promise<string> {
  await page.route('https://res.cloudinary.com/**', (route) =>
    route.fulfill({ body: tinyPng, contentType: 'image/png' }),
  );
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto(`/p/${product.slug}`);
  const consent = page.getByRole('button', { name: 'Chỉ cần thiết' });
  if (await consent.isVisible()) await consent.click();
  await page.getByRole('button', { name: 'Thêm vào giỏ' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Đã thêm vào giỏ' })).toBeVisible();

  await page.getByRole('button', { name: /^Giỏ hàng, / }).click();
  await page.getByRole('link', { name: 'Xem giỏ hàng' }).click();
  await page.getByRole('link', { name: 'Tiến hành đặt hàng' }).click();

  await chooseOption(page, 'checkout-address-province', address.provinceName);
  await chooseOption(page, 'checkout-address-ward', address.wardName);
  await page.locator('#checkout-address-line').fill('15 Đường Đơn Admin');
  await page.getByLabel('Họ và tên', { exact: true }).fill('Trần Thị Bình');
  await page.getByLabel('Số điện thoại', { exact: true }).fill('0987001122');
  await page.getByLabel('Email', { exact: true }).fill('binh.admin@example.com');
  await expect(page.getByLabel('Thanh toán khi nhận hàng (COD)')).toBeChecked();
  await page.getByRole('button', { name: /Đặt hàng$/ }).click();

  await expect(page).toHaveURL(/\/orders\/[^/]+\/success$/);
  const match = page.url().match(/\/orders\/([^/]+)\/success$/);
  expect(match).not.toBeNull();
  return match![1]!;
}
