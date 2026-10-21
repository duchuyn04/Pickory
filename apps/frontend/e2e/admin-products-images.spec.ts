import { expect, test, type Page } from '@playwright/test';
import { collectConsoleIssues, loginAdmin, tinyPng } from './catalog-admin-flow';
import { e2eEnvironment } from './environment';

// Trang không được rộng hơn khung nhìn. Bảng rộng phải cuộn trong khung riêng của nó.
async function expectNoHorizontalOverflow(page: Page) {
  const widths = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth,
  }));
  expect(widths.content).toBeLessThanOrEqual(widths.viewport);
}

interface SeededProducts {
  categoryId: string;
  withImagesName: string;
  withoutImageName: string;
  // Mô tả của ảnh đứng đầu và ảnh thứ hai của sản phẩm có ảnh.
  firstAlt: string;
  secondAlt: string;
  firstPublicId: string;
  secondPublicId: string;
}

// Tạo danh mục riêng với hai sản phẩm: một sản phẩm có hai ảnh, một sản phẩm không có ảnh.
// Spec lọc theo danh mục này, nên số dòng cố định dù DB còn sản phẩm của spec khác.
async function seedProducts(page: Page): Promise<SeededProducts> {
  const headers = { Origin: e2eEnvironment.webOrigin };
  const suffix = Date.now().toString(36);

  const category = await page.request.post('/api/admin/categories', {
    headers,
    data: { name: `Danh mục ảnh ${suffix}`, parentId: null, position: 0, attributes: [] },
  });
  expect(category.status(), await category.text()).toBe(201);
  const categoryId = (await category.json()).data.id;
  const brand = await page.request.post('/api/admin/brands', {
    headers,
    data: { name: `Hãng ảnh ${suffix}` },
  });
  expect(brand.status(), await brand.text()).toBe(201);
  const brandId = (await brand.json()).data.id;

  async function createProduct(name: string, images: { publicId: string; alt: string }[]) {
    const created = await page.request.post('/api/admin/products', {
      headers,
      data: {
        categoryId,
        brandId,
        name,
        description: { type: 'doc', content: [{ type: 'paragraph' }] },
        specs: {},
        options: [],
        variants: [
          {
            optionValues: {},
            price: 9_000_000,
            enabled: true,
            weightGrams: 400,
            lengthCm: 20,
            widthCm: 10,
            heightCm: 3,
          },
        ],
        images,
      },
    });
    expect(created.status(), await created.text()).toBe(201);
  }

  const firstPublicId = `pickory/products/e2e-ui7-first-${suffix}`;
  const secondPublicId = `pickory/products/e2e-ui7-second-${suffix}`;
  const firstAlt = `Mặt trước ${suffix}`;
  const secondAlt = `Mặt sau ${suffix}`;
  const withImagesName = `Điện thoại Ảnh ${suffix}`;
  const withoutImageName = `Áo khoác Không ảnh ${suffix}`;
  await createProduct(withImagesName, [
    { publicId: firstPublicId, alt: firstAlt },
    { publicId: secondPublicId, alt: secondAlt },
  ]);
  await createProduct(withoutImageName, []);

  return {
    categoryId,
    withImagesName,
    withoutImageName,
    firstAlt,
    secondAlt,
    firstPublicId,
    secondPublicId,
  };
}

// Giả dịch vụ ảnh Cloudinary và giữ ảnh lại tới khi test cho đi. Ghi lại URL mọi request ảnh.
async function controlImageResponses(page: Page) {
  let gate: Promise<void> | null = null;
  const requestedUrls: string[] = [];

  await page.route('https://res.cloudinary.com/**', async (route) => {
    requestedUrls.push(route.request().url());
    await gate;
    await route.fulfill({ body: tinyPng, contentType: 'image/png' });
  });

  return {
    requestedUrls,
    hold() {
      let release!: () => void;
      gate = new Promise<void>((resolve) => (release = resolve));
      return () => {
        gate = null;
        release();
      };
    },
  };
}

test('FLT-UI7: màn sản phẩm hiện ảnh đầu tiên, dòng không xô khi ảnh tải xong', async ({
  page,
}, testInfo) => {
  test.setTimeout(120000);
  const consoleIssues = collectConsoleIssues(page);
  const imageService = await controlImageResponses(page);

  await loginAdmin(page);
  const seeded = await seedProducts(page);
  await page.setViewportSize({ width: 1280, height: 800 });

  const rows = page.locator('table.product-list-table tbody tr');
  const withImagesRow = rows.filter({ hasText: seeded.withImagesName });
  const withoutImageRow = rows.filter({ hasText: seeded.withoutImageName });
  const thumbnail = withImagesRow.locator('img');
  const placeholder = withoutImageRow.getByRole('img', { name: 'Chưa có ảnh' });

  // URL gốc của hai ảnh, lấy từ API. Dùng để chắc rằng trình duyệt không tải URL gốc.
  const listResponse = await page.request.get(
    `/api/admin/products?categoryId=${seeded.categoryId}&limit=20`,
  );
  expect(listResponse.status()).toBe(200);
  const listedProducts: { name: string; images: { url: string; alt: string }[] }[] = (
    await listResponse.json()
  ).data.items;
  const originalUrls = listedProducts.flatMap((product) =>
    product.images.map((image) => image.url),
  );
  expect(originalUrls).toHaveLength(2);

  // Giữ ảnh lại trước khi mở danh sách, để đo dòng khi ảnh chưa về.
  const releaseImages = imageService.hold();
  await page.goto(`/admin/products?categoryId=${seeded.categoryId}`);
  await expect(rows).toHaveCount(2);
  // Chờ trình duyệt gửi request ảnh. Request đang bị giữ, nên ảnh chưa vẽ xong.
  await expect.poll(() => imageService.requestedUrls.length).toBeGreaterThan(0);

  const rowHeightsBefore = {
    withImages: (await withImagesRow.boundingBox())!.height,
    withoutImage: (await withoutImageRow.boundingBox())!.height,
  };
  const thumbnailBoxBefore = (await thumbnail.boundingBox())!;

  await test.step('Dòng có ảnh position 0, mô tả lấy từ ảnh, URL qua Cloudinary rộng 200', async () => {
    // Chỉ ảnh đầu được tải. Ảnh thứ hai không hiện trong bảng.
    await expect(thumbnail).toHaveCount(1);
    await expect(thumbnail).toHaveAttribute('alt', seeded.firstAlt);
    await expect(thumbnail).toHaveAttribute('loading', 'lazy');
    await expect(withImagesRow.getByRole('img', { name: seeded.secondAlt })).toHaveCount(0);
    expect(imageService.requestedUrls).toHaveLength(1);
    const requestedUrl = imageService.requestedUrls[0]!;
    expect(requestedUrl).toContain(seeded.firstPublicId);
    expect(requestedUrl).not.toContain(seeded.secondPublicId);
  });

  releaseImages();
  await test.step('Ảnh đã tải xong: dòng không đổi chiều cao, ảnh giữ nguyên cỡ', async () => {
    // Chờ ảnh vẽ xong: `complete` và có chiều rộng thật. Không chờ thời gian cố định.
    await expect
      .poll(() =>
        thumbnail.evaluate((image: HTMLImageElement) => image.complete && image.naturalWidth > 0),
      )
      .toBe(true);

    expect((await withImagesRow.boundingBox())!.height).toBe(rowHeightsBefore.withImages);
    expect((await withoutImageRow.boundingBox())!.height).toBe(rowHeightsBefore.withoutImage);
    const thumbnailBoxAfter = (await thumbnail.boundingBox())!;
    expect(thumbnailBoxAfter.width).toBe(thumbnailBoxBefore.width);
    expect(thumbnailBoxAfter.height).toBe(thumbnailBoxBefore.height);
  });

  await test.step('Mọi request ảnh dùng w_200, không có request nào là URL gốc', async () => {
    expect(imageService.requestedUrls.length).toBeGreaterThan(0);
    for (const url of imageService.requestedUrls) {
      expect(url).toContain('w_200');
      expect(originalUrls).not.toContain(url);
    }
  });

  await test.step('Dòng không ảnh có khung giữ chỗ cùng cỡ với ảnh', async () => {
    await expect(placeholder).toBeVisible();
    await expect(withoutImageRow.locator('img')).toHaveCount(0);
    const placeholderBox = (await placeholder.boundingBox())!;
    const thumbnailBox = (await thumbnail.boundingBox())!;
    expect(placeholderBox.width).toBe(thumbnailBox.width);
    expect(placeholderBox.height).toBe(thumbnailBox.height);
    // Ảnh và khung giữ chỗ cùng cột trái, nên thẳng hàng theo chiều ngang.
    expect(placeholderBox.x).toBe(thumbnailBox.x);
  });

  for (const width of [1280, 375]) {
    await test.step(`Ảnh chụp ở ${width}px không tràn ngang`, async () => {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      await expect(thumbnail).toBeVisible();
      await expectNoHorizontalOverflow(page);
      await page.screenshot({
        path: testInfo.outputPath(`FLT-UI7-products-${width}.png`),
        fullPage: true,
      });
    });
  }

  expect(consoleIssues).toEqual([]);
});
