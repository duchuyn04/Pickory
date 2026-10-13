import { expect, test, type Page } from '@playwright/test';
import { collectConsoleIssues, loginAdmin, tinyPng } from './catalog-admin-flow';
import { e2eEnvironment } from './environment';

const adminHeaders = { Origin: e2eEnvironment.webOrigin };

// Trang không được rộng hơn khung nhìn. Bảng rộng phải cuộn trong khung riêng của nó.
async function expectNoHorizontalOverflow(page: Page) {
  const widths = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth,
  }));
  expect(widths.content).toBeLessThanOrEqual(widths.viewport);
}

interface SeededProduct {
  name: string;
  slug: string;
}

interface AddressChoice {
  provinceName: string;
  wardName: string;
}

// Tạo sản phẩm đang bán có tồn kho bằng API admin để khách đặt được đơn COD qua luồng thật.
async function seedProduct(page: Page): Promise<SeededProduct> {
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
          price: 450_000,
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

  const stocked = await page.request.post(`/api/admin/skus/${product.variants[0]!.id}/movements`, {
    headers: adminHeaders,
    data: { delta: 50, reason: 'Nhập kho E2E đơn admin' },
  });
  expect(stocked.status(), await stocked.text()).toBe(201);
  const published = await page.request.put(`/api/admin/products/${product.id}/status`, {
    headers: adminHeaders,
    data: { status: 'active' },
  });
  expect(published.status(), await published.text()).toBe(200);

  return { name, slug: product.slug };
}

// Chọn tỉnh và phường đầu tiên của API địa chỉ, rồi cấu hình kho gửi để tính được phí vận chuyển.
async function prepareShipping(page: Page): Promise<AddressChoice> {
  const provinces = (await (await page.request.get('/api/addresses/provinces')).json()).data as {
    code: string;
    name: string;
  }[];
  const province = provinces[0]!;
  const wards = (
    await (
      await page.request.get(
        `/api/addresses/wards?provinceCode=${encodeURIComponent(province.code)}`,
      )
    ).json()
  ).data as { code: string; name: string }[];
  const ward = wards[0]!;

  const settings = await page.request.put('/api/admin/shipping/settings', {
    headers: adminHeaders,
    data: {
      senderName: 'Kho Pickory E2E',
      senderPhone: '0912345678',
      provinceCode: province.code,
      wardCode: ward.code,
      line: '12 Phố Kho',
      defaultServiceTypeId: 2,
    },
  });
  expect(settings.status(), await settings.text()).toBe(200);
  return { provinceName: province.name, wardName: ward.name };
}

async function chooseOption(page: Page, inputId: string, name: string) {
  const input = page.locator(`#${inputId}`);
  await expect(input).toBeEnabled();
  await input.fill(name);
  await page.getByRole('option', { name, exact: true }).click();
  await expect(input).toHaveValue(name);
}

// Khách vãng lai đặt một đơn COD qua giao diện thật và trả mã đơn. GHN dùng `FakeGhnClient` nên không gọi mạng.
async function placeCodOrder(
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

test('COM-UI5: nhân viên tìm đơn theo mã, mở chi tiết bằng bàn phím, không tràn trang', async ({
  browser,
  page,
}, testInfo) => {
  test.setTimeout(240000);
  const adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
  const adminPage = await adminContext.newPage();
  const consoleIssues = collectConsoleIssues(adminPage);

  await loginAdmin(adminPage);
  const product = await seedProduct(adminPage);
  const address = await prepareShipping(adminPage);

  let orderCode = '';
  await test.step('Khách đặt một đơn COD', async () => {
    orderCode = await placeCodOrder(page, product, address);
  });

  await test.step('Tìm theo mã đơn, URL giữ từ khóa, mở chi tiết bằng phím Enter', async () => {
    await adminPage.setViewportSize({ width: 1280, height: 800 });
    await adminPage.goto('/admin/orders');
    await expect(
      adminPage.getByRole('heading', { level: 1, name: 'Quản lý đơn hàng' }),
    ).toBeVisible();

    const searchBox = adminPage.getByLabel('Tìm theo mã đơn, tên, số điện thoại hoặc email', {
      exact: true,
    });
    await searchBox.fill(orderCode);
    // Ô tìm kiếm chờ 300 ms sau lần gõ cuối rồi mới ghi từ khóa lên URL.
    await expect(adminPage).toHaveURL(/[?&]search=/);
    const rows = adminPage.locator('table.order-table tbody tr');
    await expect(rows).toHaveCount(1);
    await expect(rows.first()).toContainText('Trần Thị Bình');
    await expect(rows.first()).toContainText('Chờ xác nhận');

    // Tải lại giữ từ khóa và danh sách.
    await adminPage.reload();
    await expect(searchBox).toHaveValue(orderCode);
    await expect(rows).toHaveCount(1);

    // Mã đơn là liên kết: đưa focus vào rồi Enter để chứng minh dùng được bằng bàn phím.
    const link = rows.first().getByRole('link', { name: orderCode, exact: true });
    await link.focus();
    await adminPage.keyboard.press('Enter');
    await expect(adminPage).toHaveURL(`${e2eEnvironment.webOrigin}/admin/orders/${orderCode}`);
    await expect(
      adminPage.getByRole('heading', { level: 1, name: `Đơn hàng ${orderCode}` }),
    ).toBeVisible();
    await expect(adminPage.getByText('Trần Thị Bình')).toBeVisible();
    await expect(adminPage.getByText('15 Đường Đơn Admin')).toBeVisible();
    await expect(adminPage.getByText(product.name)).toBeVisible();
  });

  await test.step('Tab trạng thái lọc danh sách và mũi tên chuyển focus', async () => {
    await adminPage.goto('/admin/orders');
    const pendingTab = adminPage.getByRole('tab', { name: 'Chờ xác nhận', exact: true });
    await pendingTab.click();
    await expect(adminPage).toHaveURL(/[?&]status=pending_confirmation(&|$)/);
    await expect(pendingTab).toHaveAttribute('aria-selected', 'true');

    await pendingTab.focus();
    await adminPage.keyboard.press('ArrowRight');
    await expect(adminPage.getByRole('tab', { name: 'Đã xác nhận', exact: true })).toBeFocused();
  });

  await test.step('Nhân viên xác nhận đơn và thấy trạng thái Đã xác nhận', async () => {
    await adminPage.setViewportSize({ width: 1280, height: 800 });
    await adminPage.goto(`/admin/orders/${orderCode}`);
    const overview = adminPage.getByLabel('Tổng quan đơn');
    await expect(overview).toContainText('Chờ xác nhận');

    await adminPage.getByRole('button', { name: 'Xác nhận đơn', exact: true }).click();
    const dialog = adminPage.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Xác nhận', exact: true }).click();

    await expect(dialog).toBeHidden();
    await expect(overview).toContainText('Đã xác nhận');
    await expect(
      adminPage.getByRole('status').filter({ hasText: 'Đã xác nhận đơn.' }),
    ).toBeVisible();
  });

  for (const width of [1280, 375]) {
    await test.step(`Hủy đơn bắt buộc lý do, ảnh có nút và hộp thoại ở ${width}px`, async () => {
      await adminPage.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      await adminPage.goto(`/admin/orders/${orderCode}`);
      await expect(adminPage.getByLabel('Tổng quan đơn')).toContainText('Đã xác nhận');
      await adminPage.screenshot({
        path: testInfo.outputPath(`actions-${width}.png`),
        fullPage: true,
      });

      const cancelButton = adminPage.getByRole('button', { name: 'Hủy đơn', exact: true });
      await cancelButton.click();
      const dialog = adminPage.getByRole('dialog');
      await expect(dialog).toBeVisible();
      // Bấm thực hiện khi chưa nhập lý do thì thấy lỗi dưới ô và hộp thoại còn mở.
      await dialog.getByRole('button', { name: 'Xác nhận hủy đơn', exact: true }).click();
      await expect(dialog.getByText('Nhập lý do hủy đơn.')).toBeVisible();
      await expectNoHorizontalOverflow(adminPage);
      await adminPage.screenshot({ path: testInfo.outputPath(`cancel-dialog-${width}.png`) });

      // Escape đóng hộp thoại và trả focus về nút đã mở.
      await adminPage.keyboard.press('Escape');
      await expect(dialog).toBeHidden();
      await expect(cancelButton).toBeFocused();
    });
  }

  await test.step('Tạo vận đơn với kiện điền sẵn rồi thấy trạng thái Đang giao hàng', async () => {
    await adminPage.setViewportSize({ width: 1280, height: 800 });
    await adminPage.goto(`/admin/orders/${orderCode}`);
    await adminPage.getByRole('button', { name: 'Tạo vận đơn', exact: true }).click();
    const dialog = adminPage.getByRole('dialog');
    await expect(dialog).toBeVisible();
    // Kiện gợi ý do backend dựng từ dòng hàng, nên bốn ô đều có giá trị.
    for (const label of ['Cân nặng (g)', 'Dài (cm)', 'Rộng (cm)', 'Cao (cm)']) {
      await expect(dialog.getByLabel(label)).not.toHaveValue('');
    }
    await dialog.getByRole('button', { name: 'Tạo vận đơn', exact: true }).click();

    await expect(dialog).toBeHidden();
    await expect(adminPage.getByLabel('Tổng quan đơn')).toContainText('Đang giao hàng');
    await expect(adminPage.getByRole('button', { name: 'Hủy vận đơn', exact: true })).toBeVisible();
  });

  for (const width of [1280, 375]) {
    await test.step(`Ảnh chụp danh sách và chi tiết ở ${width}px không tràn ngang`, async () => {
      await adminPage.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      const screens = [
        { path: `/admin/orders?search=${encodeURIComponent(orderCode)}`, name: 'list' },
        { path: `/admin/orders/${orderCode}`, name: 'detail' },
      ];
      for (const screen of screens) {
        await adminPage.goto(screen.path);
        await expect(adminPage.getByText(/^Đang nạp/)).toHaveCount(0);
        await expect(adminPage.getByText(orderCode).first()).toBeVisible();
        await expectNoHorizontalOverflow(adminPage);
        await adminPage.screenshot({
          path: testInfo.outputPath(`${screen.name}-${width}.png`),
          fullPage: true,
        });
      }
    });
  }

  expect(consoleIssues).toEqual([]);
  await adminContext.close();
});
