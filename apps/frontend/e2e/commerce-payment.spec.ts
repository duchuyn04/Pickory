import { randomInt } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';

const screenshotDirectory =
  'C:/Users/juven/AppData/Local/Temp/claude/C--Users-juven-Desktop-pickory/c0628d6e-6470-493e-8673-a94b5488add7/scratchpad/com-ui4-shots';
const accountNumber = '1017588888';
const bankCode = 'VCB';
const tinyPng = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64',
);

async function captureAtBothWidths(page: Page, state: string) {
  for (const width of [1280, 375]) {
    await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
    // Ảnh QR phải tải xong trước khi lưu bằng chứng; ảnh rỗng không xác nhận được giao diện.
    if (state === 'pending' || state === 'urgent') {
      await page.waitForFunction(() => {
        const image = document.querySelector<HTMLImageElement>('.payment-qr-image img');
        return Boolean(image?.complete && image.naturalWidth > 0);
      });
    }
    const dimensions = await page.evaluate(() => ({
      scrollWidth: document.documentElement.scrollWidth,
      clientWidth: document.documentElement.clientWidth,
    }));
    expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth);
    await page.screenshot({ path: `${screenshotDirectory}/${state}-${width}.png`, fullPage: true });
  }
}

async function loginAdmin(page: Page) {
  await page.goto('/admin/login');
  const consent = page.getByRole('button', { name: 'Chỉ cần thiết', exact: true });
  if (await consent.isVisible()) await consent.click();
  await page.getByLabel(/^Email/).fill(e2eEnvironment.adminEmail);
  await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.adminPassword);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin`);
}

test('khách quét VietQR, sao chép và nhận xác nhận chuyển khoản từ webhook', async ({
  browser,
  page,
  context,
}) => {
  test.setTimeout(180000);
  const consoleIssues: string[] = [];
  const watchConsole = (watchedPage: Page) => {
    watchedPage.on('console', (message) => {
      if (message.location().url.endsWith('/favicon.ico')) return;
      if (message.type() === 'error' || message.type() === 'warning')
        consoleIssues.push(`${message.type()}: ${message.text()}`);
    });
    watchedPage.on('pageerror', (error) => consoleIssues.push(`pageerror: ${error.message}`));
  };
  watchConsole(page);
  const adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
  const adminPage = await adminContext.newPage();
  watchConsole(adminPage);
  await loginAdmin(adminPage);
  const adminHeaders = { Origin: e2eEnvironment.webOrigin };
  // Lưu cấu hình cũ để test không đổi ngân hàng của các spec chạy sau.
  const previousSettingsResponse = await adminPage.request.get('/api/admin/commerce/settings');
  expect(previousSettingsResponse.status()).toBe(200);
  const previousSettings = (await previousSettingsResponse.json()).data as {
    freeShippingThreshold: number;
    codMaxAmount: number;
    maxQuantityPerSku: number;
    maxCartLines: number;
    sepayAccountNumber: string | null;
    sepayBankCode: string | null;
  };
  try {
    const suffix = Date.now().toString(36);

    // Tạo sản phẩm thật qua API admin để checkout giữ kho và tạo đơn giống luồng khách.
    const categoryResponse = await adminPage.request.post('/api/admin/categories', {
      headers: adminHeaders,
      data: { name: `Thanh toán ${suffix}`, parentId: null, position: 0, attributes: [] },
    });
    expect(categoryResponse.status(), await categoryResponse.text()).toBe(201);
    const categoryId = (await categoryResponse.json()).data.id as string;
    const brandResponse = await adminPage.request.post('/api/admin/brands', {
      headers: adminHeaders,
      data: { name: `Nhãn thanh toán ${suffix}` },
    });
    expect(brandResponse.status(), await brandResponse.text()).toBe(201);
    const brandId = (await brandResponse.json()).data.id as string;
    const productResponse = await adminPage.request.post('/api/admin/products', {
      headers: adminHeaders,
      data: {
        categoryId,
        brandId,
        name: `Chuột thanh toán ${suffix}`,
        description: { type: 'doc', content: [{ type: 'paragraph' }] },
        warrantyMonths: 12,
        specs: {},
        options: [],
        variants: [
          {
            optionValues: {},
            price: 500000,
            stock: 20,
            compareAtPrice: null,
            enabled: true,
            weightGrams: 500,
            lengthCm: 20,
            widthCm: 10,
            heightCm: 5,
          },
        ],
        images: [
          { publicId: `pickory/products/e2e-payment-${suffix}`, alt: `Chuột thanh toán ${suffix}` },
        ],
      },
    });
    expect(productResponse.status(), await productResponse.text()).toBe(201);
    const product = (await productResponse.json()).data as {
      id: string;
      slug: string;
      variants: { id: string }[];
    };
    const published = await adminPage.request.put(`/api/admin/products/${product.id}/status`, {
      headers: adminHeaders,
      data: { status: 'active' },
    });
    expect(published.status(), await published.text()).toBe(200);

    // Cấu hình địa chỉ gửi và ngân hàng trước khi tạo đơn để QR có đủ thông tin nhận tiền.
    const provinces = (await (await adminPage.request.get('/api/addresses/provinces')).json())
      .data as { code: string; name: string }[];
    const province = provinces[0]!;
    const wards = (
      await (
        await adminPage.request.get(`/api/addresses/wards?provinceCode=${province.code}`)
      ).json()
    ).data as { code: string; name: string }[];
    const ward = wards[0]!;
    const shipping = await adminPage.request.put('/api/admin/shipping/settings', {
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
    expect(shipping.status(), await shipping.text()).toBe(200);
    const settings = await adminPage.request.put('/api/admin/commerce/settings', {
      headers: adminHeaders,
      data: {
        freeShippingThreshold: 500000,
        codMaxAmount: 20000000,
        maxQuantityPerSku: 5,
        maxCartLines: 20,
        sepayAccountNumber: accountNumber,
        sepayBankCode: bankCode,
      },
    });
    expect(settings.status(), await settings.text()).toBe(200);
    await page.route('https://res.cloudinary.com/**', (route) =>
      route.fulfill({ body: tinyPng, contentType: 'image/png' }),
    );
    await page.goto(`/p/${product.slug}`);
    const consent = page.getByRole('button', { name: 'Chỉ cần thiết', exact: true });
    if (await consent.isVisible()) await consent.click();
    await page.getByRole('button', { name: 'Thêm vào giỏ' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Đã thêm vào giỏ' })).toBeVisible();
    await page.getByRole('button', { name: /^Giỏ hàng, / }).click();
    await page.getByRole('link', { name: 'Xem giỏ hàng' }).click();
    await page.getByRole('link', { name: 'Tiến hành đặt hàng' }).click();
    await page.locator('#checkout-address-province').fill(province.name);
    await page.getByRole('option', { name: province.name, exact: true }).click();
    await page.locator('#checkout-address-ward').fill(ward.name);
    await page.getByRole('option', { name: ward.name, exact: true }).click();
    await page.locator('#checkout-address-line').fill('25 Đường Test');
    await page.getByLabel('Họ và tên', { exact: true }).fill('Nguyễn Văn An');
    await page.getByLabel('Số điện thoại', { exact: true }).fill('0912345678');
    await page.getByLabel('Email', { exact: true }).fill('an@example.com');
    await page.getByLabel('Chuyển khoản (VietQR)').check();
    const placeOrder = page.getByRole('button', { name: /Đặt hàng$|Đang đặt hàng/ });
    await expect(placeOrder).toBeEnabled();
    await placeOrder.click();
    await expect(page).toHaveURL(/\/orders\/[^/]+\/payment\?t=.+/);
    const paymentUrl = new URL(page.url());
    const orderCode = paymentUrl.pathname.split('/')[2]!;
    const qrImage = page.getByRole('img', { name: `Mã QR chuyển khoản cho đơn ${orderCode}` });
    await expect(qrImage).toBeVisible();
    const imageUrl = new URL((await qrImage.getAttribute('src'))!);
    expect(imageUrl.origin + imageUrl.pathname).toBe('https://vietqr.app/img');
    expect(Object.fromEntries(imageUrl.searchParams)).toEqual({
      acc: accountNumber,
      bank: bankCode,
      amount: '500000',
      des: orderCode,
    });
    await captureAtBothWidths(page, 'pending');

    // Clipboard thật xác nhận cả nội dung chuyển khoản và thông báo cho trình đọc màn hình.
    await context.grantPermissions(['clipboard-read', 'clipboard-write']);
    await page.getByRole('button', { name: 'Sao chép số tài khoản' }).click();
    expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(accountNumber);
    await expect(page.locator('[aria-live="polite"]')).toContainText('Đã sao chép số tài khoản');
    await page.getByRole('button', { name: 'Sao chép nội dung chuyển khoản' }).click();
    expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(orderCode);
    await expect(page.locator('[aria-live="polite"]')).toContainText(
      'Đã sao chép nội dung chuyển khoản',
    );

    // Tách trang chụp trạng thái để không can thiệp polling thật của trang nhận webhook.
    const visualPage = await context.newPage();
    watchConsole(visualPage);
    const paymentPath = `**/api/orders/${orderCode}/payment*`;
    for (const state of ['urgent', 'expired', 'cancelled', 'error']) {
      await visualPage.route(paymentPath, async (route) => {
        if (state === 'error') {
          await route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({
              error: { code: 'NETWORK_ERROR', message: 'Không kết nối được máy chủ.' },
            }),
          });
          return;
        }
        const response = await route.fetch();
        const body = await response.json();
        // Mỗi trạng thái dùng hạn riêng để giữ bằng chứng mà không chờ 30 phút thật.
        let deadlineOffsetMs = 10 * 60000;
        if (state === 'urgent') deadlineOffsetMs = 4 * 60000;
        else if (state === 'expired') deadlineOffsetMs = -1000;
        body.data.paymentDeadline = new Date(Date.now() + deadlineOffsetMs).toISOString();
        // Hủy sớm giữ hạn tương lai để phát hiện nhãn quá hạn sai.
        if (state === 'cancelled') body.data.status = 'cancelled';
        await route.fulfill({ response, json: body });
      });
      await visualPage.goto(paymentUrl.toString());
      if (state === 'urgent') await expect(visualPage.getByRole('timer')).toBeVisible();
      if (state === 'expired')
        await expect(
          visualPage.getByRole('heading', { name: 'Thời gian thanh toán đã hết' }),
        ).toBeVisible();
      if (state === 'cancelled') {
        await expect(visualPage.getByRole('heading', { name: 'Đơn hàng đã hủy' })).toBeVisible();
        await expect(visualPage.getByRole('img', { name: /Mã QR chuyển khoản/ })).toHaveCount(0);
      }
      if (state === 'error')
        await expect(
          visualPage.getByRole('heading', { name: 'Chưa tải được thông tin thanh toán' }),
        ).toBeVisible();
      await captureAtBothWidths(visualPage, state);
      await visualPage.unrouteAll();
    }
    await visualPage.close();

    // Gửi webhook qua API thật, rồi buộc trình duyệt nhận xác nhận trong tối đa 5 giây.
    const paymentResponse = await adminPage.request.get(
      `/api/orders/${orderCode}/payment?t=${encodeURIComponent(paymentUrl.searchParams.get('t')!)}`,
    );
    expect(paymentResponse.status()).toBe(200);
    const paymentTotal = (await paymentResponse.json()).data.total as number;
    const webhook = await adminPage.request.post('/api/webhooks/sepay', {
      headers: { Authorization: `Apikey ${e2eEnvironment.sepayWebhookApiKey}` },
      data: {
        id: randomInt(100000000, 999999999),
        gateway: 'Vietcombank',
        transactionDate: '2026-10-12 10:00:00',
        accountNumber,
        subAccount: '',
        code: null,
        content: orderCode,
        transferType: 'in',
        description: '',
        transferAmount: paymentTotal,
        accumulated: 105000000,
        referenceCode: `E2E-${suffix}`,
      },
    });
    expect(webhook.status(), await webhook.text()).toBe(200);
    await expect(page).toHaveURL(new RegExp(`/orders/${orderCode}/success$`), { timeout: 5000 });
    await expect(page.getByText('Shop đã nhận tiền chuyển khoản.')).toBeVisible();
    expect(consoleIssues).toEqual([]);
  } finally {
    // Khôi phục cấu hình kể cả khi checkout hoặc webhook thất bại để giữ các test độc lập.
    const restoredSettings = await adminPage.request.put('/api/admin/commerce/settings', {
      headers: adminHeaders,
      data: {
        freeShippingThreshold: previousSettings.freeShippingThreshold,
        codMaxAmount: previousSettings.codMaxAmount,
        maxQuantityPerSku: previousSettings.maxQuantityPerSku,
        maxCartLines: previousSettings.maxCartLines,
        sepayAccountNumber: previousSettings.sepayAccountNumber,
        sepayBankCode: previousSettings.sepayBankCode,
      },
    });
    expect(restoredSettings.status(), await restoredSettings.text()).toBe(200);
    await adminContext.close();
  }
});
