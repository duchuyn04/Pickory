import { expect, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';

// Ảnh PNG 1x1 trả cho mọi yêu cầu ảnh sản phẩm, để trình duyệt không ghi lỗi tải ảnh vào console.
const tinyPng = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64',
);

const adminHeaders = { Origin: e2eEnvironment.webOrigin };

export async function loginAdmin(page: Page) {
  await page.goto('/admin/login');
  const consent = page.getByRole('button', { name: 'Chỉ cần thiết' });
  if (await consent.isVisible()) await consent.click();
  await page.getByLabel(/^Email/).fill(e2eEnvironment.adminEmail);
  await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.adminPassword);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin`);
}

// Ghi lỗi console và lỗi trang. Mỗi test phải kết thúc với 0 lỗi và 0 cảnh báo.
export function collectConsoleIssues(page: Page): string[] {
  const issues: string[] = [];
  page.on('console', (message) => {
    if (message.location().url.endsWith('/favicon.ico')) return;
    // Trình duyệt tự ghi dòng "Failed to load resource" cho phản hồi 404 mà test chủ động gây ra.
    if (message.type() === 'error' && /status of 404/.test(message.text())) return;
    if (message.type() === 'error' || message.type() === 'warning') {
      issues.push(`${message.type()}: ${message.text()}`);
    }
  });
  page.on('pageerror', (error) => issues.push(`pageerror: ${error.message}`));
  return issues;
}

// E2E không gọi Cloudinary thật: mọi ảnh sản phẩm là PNG 1x1.
export async function stubProductImages(page: Page) {
  await page.route('https://res.cloudinary.com/**', (route) =>
    route.fulfill({ body: tinyPng, contentType: 'image/png' }),
  );
}

export async function dismissConsent(page: Page) {
  const consent = page.getByRole('button', { name: 'Chỉ cần thiết' });
  if (await consent.isVisible()) await consent.click();
}

interface ApiVariant {
  id: string;
  optionValues: Record<string, string>;
}

export interface SeededCatalog {
  suffix: string;
  categoryName: string;
  categorySlug: string;
  brandName: string;
  activeSlug: string;
  activeName: string;
  skuIds: Record<'black256' | 'black512' | 'silver256' | 'silver512', string>;
  discontinuedSlug: string;
  sparseSlug: string;
  renamedProductId: string;
  oldSlug: string;
  newSlug: string;
}

interface VariantSeed {
  optionValues: Record<string, string>;
  price: number;
  compareAtPrice?: number;
  stock: number;
  // SKU tắt vẫn thuộc tổ hợp tùy chọn nhưng khách không thấy. Mặc định là bật.
  enabled?: boolean;
}

const productDescription = {
  type: 'doc',
  content: [
    { type: 'heading', attrs: { level: 1 }, content: [{ type: 'text', text: 'Tiêu đề cấp một' }] },
    {
      type: 'paragraph',
      content: [
        { type: 'text', text: 'Chữ đậm', marks: [{ type: 'bold' }] },
        { type: 'text', text: ' và ' },
        { type: 'text', text: 'chữ nghiêng', marks: [{ type: 'italic' }] },
      ],
    },
    { type: 'heading', attrs: { level: 2 }, content: [{ type: 'text', text: 'Tiêu đề cấp hai' }] },
    { type: 'heading', attrs: { level: 3 }, content: [{ type: 'text', text: 'Tiêu đề cấp ba' }] },
  ],
};

function skuIdOf(variants: ApiVariant[], optionValues: Record<string, string>): string {
  const variant = variants.find(
    (candidate) =>
      candidate.optionValues['Màu'] === optionValues['Màu'] &&
      candidate.optionValues['Bộ nhớ'] === optionValues['Bộ nhớ'],
  );
  if (!variant) throw new Error(`Không thấy SKU ${JSON.stringify(optionValues)}`);
  return variant.id;
}

async function createProduct(
  page: Page,
  input: {
    categoryId: string;
    brandId: string;
    name: string;
    // Mã ảnh phải thuộc thư mục ảnh sản phẩm và chỉ gồm chữ thường, số, gạch ngang.
    imagePublicId: string;
    options: { name: string; values: string[] }[];
    variants: VariantSeed[];
    description?: unknown;
  },
) {
  const created = await page.request.post('/api/admin/products', {
    headers: adminHeaders,
    data: {
      categoryId: input.categoryId,
      brandId: input.brandId,
      name: input.name,
      description: input.description ?? { type: 'doc', content: [{ type: 'paragraph' }] },
      warrantyMonths: 12,
      specs: { ram: '16', screen: 14, touch: true },
      options: input.options,
      variants: input.variants.map((variant) => ({
        optionValues: variant.optionValues,
        price: variant.price,
        compareAtPrice: variant.compareAtPrice ?? null,
        enabled: variant.enabled ?? true,
        weightGrams: 1500,
        lengthCm: 35,
        widthCm: 25,
        heightCm: 3,
      })),
      // Sản phẩm cần ít nhất một ảnh mới đăng bán được. Ảnh gắn SKU được thêm sau khi có id SKU.
      images: [
        {
          publicId: input.imagePublicId,
          alt: `Ảnh ${input.name}`,
        },
      ],
    },
  });
  expect(created.status(), await created.text()).toBe(201);
  const product = (await created.json()).data as {
    id: string;
    slug: string;
    variants: ApiVariant[];
  };

  // Nhập kho theo từng SKU. Tồn 0 nghĩa là không nhập, nên SKU hết hàng.
  for (const variantSeed of input.variants) {
    if (variantSeed.stock === 0) continue;
    const skuId = skuIdOf(product.variants, variantSeed.optionValues);
    const adjusted = await page.request.post(`/api/admin/skus/${skuId}/movements`, {
      headers: adminHeaders,
      data: { delta: variantSeed.stock, reason: 'Nhập kho E2E trang sản phẩm' },
    });
    expect(adjusted.status(), await adjusted.text()).toBe(201);
  }
  return product;
}

async function changeStatus(page: Page, productId: string, statuses: string[]) {
  for (const status of statuses) {
    const changed = await page.request.put(`/api/admin/products/${productId}/status`, {
      headers: adminHeaders,
      data: { status },
    });
    expect(changed.status(), await changed.text()).toBe(200);
  }
}

// Tạo dữ liệu bằng API quản trị. Gồm danh mục có hai nhóm thuộc tính, sản phẩm bán có 4 SKU, sản phẩm ngừng kinh doanh và sản phẩm đổi tên.
export async function seedCatalog(page: Page): Promise<SeededCatalog> {
  const suffix = Date.now().toString(36);

  const category = await page.request.post('/api/admin/categories', {
    headers: adminHeaders,
    data: {
      name: `Laptop C2 ${suffix}`,
      parentId: null,
      position: 0,
      attributes: [
        {
          key: 'ram',
          label: 'RAM',
          group: 'Cấu hình',
          type: 'enum',
          unit: 'GB',
          options: ['8', '16', '32'],
          required: false,
          filterable: true,
        },
        {
          key: 'screen',
          label: 'Màn hình',
          group: 'Hiển thị',
          type: 'number',
          unit: 'inch',
          required: false,
          filterable: true,
        },
        {
          key: 'touch',
          label: 'Cảm ứng',
          group: 'Hiển thị',
          type: 'boolean',
          required: false,
          filterable: true,
        },
      ],
    },
  });
  expect(category.status(), await category.text()).toBe(201);
  const categoryData = (await category.json()).data;

  const brandName = `Hãng C2 ${suffix}`;
  const brand = await page.request.post('/api/admin/brands', {
    headers: adminHeaders,
    data: { name: brandName },
  });
  expect(brand.status()).toBe(201);
  const brandData = (await brand.json()).data;

  const colorAndMemory = [
    { name: 'Màu', values: ['Đen', 'Bạc'] },
    { name: 'Bộ nhớ', values: ['256GB', '512GB'] },
  ];
  const activeName = `Laptop Kaze C2 ${suffix}`;
  const active = await createProduct(page, {
    categoryId: categoryData.id,
    brandId: brandData.id,
    name: activeName,
    imagePublicId: `pickory/products/e2e-c2-${suffix}-main`,
    options: colorAndMemory,
    description: productDescription,
    variants: [
      { optionValues: { Màu: 'Đen', 'Bộ nhớ': '256GB' }, price: 20_000_000, stock: 5 },
      // SKU hết hàng: không nhập kho.
      { optionValues: { Màu: 'Đen', 'Bộ nhớ': '512GB' }, price: 24_000_000, stock: 0 },
      {
        optionValues: { Màu: 'Bạc', 'Bộ nhớ': '256GB' },
        price: 21_000_000,
        compareAtPrice: 25_000_000,
        stock: 5,
      },
      { optionValues: { Màu: 'Bạc', 'Bộ nhớ': '512GB' }, price: 26_000_000, stock: 5 },
    ],
  });
  const skuIds = {
    black256: skuIdOf(active.variants, { Màu: 'Đen', 'Bộ nhớ': '256GB' }),
    black512: skuIdOf(active.variants, { Màu: 'Đen', 'Bộ nhớ': '512GB' }),
    silver256: skuIdOf(active.variants, { Màu: 'Bạc', 'Bộ nhớ': '256GB' }),
    silver512: skuIdOf(active.variants, { Màu: 'Bạc', 'Bộ nhớ': '512GB' }),
  };

  // Ảnh gắn SKU cần id SKU, nên chỉ gắn được sau khi sản phẩm đã tạo.
  const imaged = await page.request.patch(`/api/admin/products/${active.id}`, {
    headers: adminHeaders,
    data: {
      images: [
        {
          publicId: `pickory/products/e2e-c2-${suffix}-den`,
          alt: 'Ảnh màu đen',
          skuId: skuIds.black256,
        },
        {
          publicId: `pickory/products/e2e-c2-${suffix}-bac`,
          alt: 'Ảnh màu bạc',
          skuId: skuIds.silver256,
        },
        { publicId: `pickory/products/e2e-c2-${suffix}-chung`, alt: 'Ảnh chung', skuId: null },
      ],
    },
  });
  expect(imaged.status(), await imaged.text()).toBe(200);
  await changeStatus(page, active.id, ['active']);

  const discontinued = await createProduct(page, {
    categoryId: categoryData.id,
    brandId: brandData.id,
    name: `Laptop Cũ C2 ${suffix}`,
    imagePublicId: `pickory/products/e2e-c2-${suffix}-old`,
    options: [{ name: 'Màu', values: ['Đen', 'Bạc'] }],
    variants: [
      { optionValues: { Màu: 'Đen' }, price: 15_000_000, stock: 3 },
      { optionValues: { Màu: 'Bạc' }, price: 16_000_000, stock: 3 },
    ],
  });
  await changeStatus(page, discontinued.id, ['active', 'discontinued']);

  // API bắt đủ 4 tổ hợp. Hai SKU bị tắt, nên khách chỉ thấy Đen 256GB và Bạc 512GB, hai SKU không chung giá trị nào.
  const sparse = await createProduct(page, {
    categoryId: categoryData.id,
    brandId: brandData.id,
    name: `Laptop Thưa C2 ${suffix}`,
    imagePublicId: `pickory/products/e2e-c2-${suffix}-sparse`,
    options: colorAndMemory,
    variants: [
      { optionValues: { Màu: 'Đen', 'Bộ nhớ': '256GB' }, price: 18_000_000, stock: 4 },
      {
        optionValues: { Màu: 'Đen', 'Bộ nhớ': '512GB' },
        price: 22_000_000,
        stock: 0,
        enabled: false,
      },
      {
        optionValues: { Màu: 'Bạc', 'Bộ nhớ': '256GB' },
        price: 19_000_000,
        stock: 0,
        enabled: false,
      },
      { optionValues: { Màu: 'Bạc', 'Bộ nhớ': '512GB' }, price: 29_000_000, stock: 4 },
    ],
  });
  await changeStatus(page, sparse.id, ['active']);

  const renamed = await createProduct(page, {
    categoryId: categoryData.id,
    brandId: brandData.id,
    name: `Laptop Tên Cũ C2 ${suffix}`,
    imagePublicId: `pickory/products/e2e-c2-${suffix}-renamed`,
    options: [],
    variants: [{ optionValues: {}, price: 12_000_000, stock: 2 }],
  });
  await changeStatus(page, renamed.id, ['active']);
  const renameResponse = await page.request.patch(`/api/admin/products/${renamed.id}`, {
    headers: adminHeaders,
    data: { name: `Laptop Tên Mới C2 ${suffix}` },
  });
  expect(renameResponse.status(), await renameResponse.text()).toBe(200);
  const renamedSlug = (await renameResponse.json()).data.slug as string;

  return {
    suffix,
    categoryName: categoryData.name,
    categorySlug: categoryData.slug,
    brandName,
    activeSlug: active.slug,
    activeName,
    skuIds,
    discontinuedSlug: discontinued.slug,
    sparseSlug: sparse.slug,
    renamedProductId: renamed.id,
    oldSlug: renamed.slug,
    newSlug: renamedSlug,
  };
}
