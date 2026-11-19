import { randomInt } from 'node:crypto';
import { mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test, type BrowserContext, type Page } from '@playwright/test';
import { prepareShipping, readFirstShippingAddress, seedProduct } from './admin-order-fixtures';
import { collectConsoleIssues, loginAdmin } from './catalog-admin-flow';
import { loginSeededCustomer } from './commerce-order-fixtures';
import { e2eEnvironment } from './environment';
import {
  countPdfPages,
  extractPdfPageTexts,
  readFirstPdfPageClipMargin,
  readPrintCallCount,
  stubWindowPrintOnContext,
} from './invoice-pdf-helpers';
import { activateStaffAccount } from './staff-fixtures';

const EVIDENCE_DIRECTORY = resolve(
  process.cwd(),
  '../../evidence/exports-printing/EXP-UI5/picture',
);
const SETTINGS_API_PATH = '/api/admin/commerce/settings';
const ORIGIN_HEADERS = { Origin: e2eEnvironment.webOrigin };

interface ShopProfile {
  name: string | null;
  address: string | null;
  phone: string | null;
  taxCode: string | null;
}

interface CommerceSettingsSnapshot {
  freeShippingThreshold: number;
  codMaxAmount: number;
  maxQuantityPerSku: number;
  maxCartLines: number;
  sepayAccountNumber: string | null;
  sepayBankCode: string | null;
  shop: ShopProfile;
}

interface VatInvoiceInput {
  companyName: string;
  taxCode: string;
  address: string;
  email: string;
}

const EMPTY_SHOP: ShopProfile = {
  name: null,
  address: null,
  phone: null,
  taxCode: null,
};

const FULL_SHOP: ShopProfile = {
  name: 'Pickory Flagship',
  address: '12 Nguyễn Văn Bảo, Phường 4, Quận Gò Vấp, TP. Hồ Chí Minh',
  phone: '028 3894 0390',
  taxCode: '0312345678',
};

const VAT_INVOICE_INPUT: VatInvoiceInput = {
  companyName: 'Công ty TNHH Ánh Dương',
  taxCode: '0109876543',
  address: '8 Trần Hưng Đạo, Hoàn Kiếm, Hà Nội',
  email: 'ketoan@anhduong.example.com',
};

let runSuffix = '';
let previousSettings: CommerceSettingsSnapshot | undefined;
let vatOrderCode = '';
let cancelledOrderCode = '';
let plainOrderCode = '';
let fifteenLineOrderCode = '';
let fortyLineOrderCode = '';

// Chặn tải font ngoài để lỗi mạng bên thứ ba không gây cảnh báo giả trên console.
async function stubFontsOnContext(context: BrowserContext): Promise<void> {
  await context.route(/^https:\/\/fonts\.(googleapis|gstatic)\.com\//, (route) =>
    route.fulfill({ body: '', contentType: 'text/css' }),
  );
}

// Đọc toàn bộ cấu hình bán hàng hiện tại nhằm lưu bản gốc trước khi thay đổi.
async function readCommerceSettings(adminPage: Page): Promise<CommerceSettingsSnapshot> {
  const response = await adminPage.request.get(SETTINGS_API_PATH);
  expect(response.status(), await response.text()).toBe(200);
  return (await response.json()).data as CommerceSettingsSnapshot;
}

// Gửi đủ bảy trường cấu hình bán hàng để API không từ chối khi thay đổi shop hoặc giới hạn giỏ.
async function saveCommerceSettings(
  adminPage: Page,
  base: CommerceSettingsSnapshot,
  overrides: { shop?: ShopProfile; maxCartLines?: number; codMaxAmount?: number },
): Promise<void> {
  const response = await adminPage.request.put(SETTINGS_API_PATH, {
    headers: ORIGIN_HEADERS,
    data: {
      freeShippingThreshold: base.freeShippingThreshold,
      codMaxAmount: overrides.codMaxAmount ?? base.codMaxAmount,
      maxQuantityPerSku: base.maxQuantityPerSku,
      maxCartLines: overrides.maxCartLines ?? base.maxCartLines,
      sepayAccountNumber: base.sepayAccountNumber,
      sepayBankCode: base.sepayBankCode,
      shop: overrides.shop ?? base.shop,
    },
  });
  expect(response.status(), await response.text()).toBe(200);
}

// Xóa sạch giỏ hàng của khách seed trước khi thêm danh sách sản phẩm mới cho đơn kế tiếp.
async function clearCustomerCart(customerPage: Page): Promise<void> {
  const cartResponse = await customerPage.request.get('/api/cart');
  expect(cartResponse.status(), await cartResponse.text()).toBe(200);
  const items = ((await cartResponse.json()).data?.items ?? []) as { skuId: string }[];
  for (const item of items) {
    const removed = await customerPage.request.delete(
      `/api/cart/items/${encodeURIComponent(item.skuId)}`,
      { headers: ORIGIN_HEADERS },
    );
    expect(removed.status(), await removed.text()).toBe(200);
  }
}

// Tạo một sản phẩm có nhiều SKU (mã phân loại hàng) để đặt đơn nhiều dòng hàng.
async function seedMultiVariantProduct(
  adminPage: Page,
  options: {
    variantCount: number;
    suffix: string;
    productName: string;
    buildOptionValue: (index: number) => string;
  },
): Promise<{ productId: string; skuIds: string[] }> {
  // Tạo danh mục riêng theo hậu tố để không đụng dữ liệu của các spec khác.
  const category = await adminPage.request.post('/api/admin/categories', {
    headers: ORIGIN_HEADERS,
    data: { name: `Danh mục in ${options.suffix}`, parentId: null, position: 0, attributes: [] },
  });
  expect(category.status(), await category.text()).toBe(201);

  // Tạo thương hiệu riêng làm khóa ngoại bắt buộc khi gọi API tạo sản phẩm.
  const brand = await adminPage.request.post('/api/admin/brands', {
    headers: ORIGIN_HEADERS,
    data: { name: `Hãng in ${options.suffix}` },
  });
  expect(brand.status(), await brand.text()).toBe(201);

  const optionValues = Array.from({ length: options.variantCount }, (_, index) =>
    options.buildOptionValue(index),
  );
  // Gửi đủ danh sách biến thể khớp trục tùy chọn để API tạo đủ mã SKU.
  const created = await adminPage.request.post('/api/admin/products', {
    headers: ORIGIN_HEADERS,
    data: {
      categoryId: (await category.json()).data.id,
      brandId: (await brand.json()).data.id,
      name: options.productName,
      description: { type: 'doc', content: [{ type: 'paragraph' }] },
      warrantyMonths: 12,
      specs: {},
      options: [{ name: 'Phiên bản', values: optionValues }],
      variants: optionValues.map((value, index) => ({
        code: `INV-${options.suffix.toUpperCase()}-${String(index + 1).padStart(2, '0')}`,
        optionValues: { 'Phiên bản': value },
        price: 100_000,
        stock: 50,
        compareAtPrice: null,
        enabled: true,
        weightGrams: 100,
        lengthCm: 10,
        widthCm: 8,
        heightCm: 4,
      })),
      images: [
        { publicId: `pickory/products/e2e-invoice-${options.suffix}`, alt: options.productName },
      ],
    },
  });
  expect(created.status(), await created.text()).toBe(201);

  const product = (await created.json()).data as {
    id: string;
    variants: { id: string }[];
  };
  // Chuyển sản phẩm sang trạng thái hoạt động để khách có thể thêm vào giỏ.
  const published = await adminPage.request.put(`/api/admin/products/${product.id}/status`, {
    headers: ORIGIN_HEADERS,
    data: { status: 'active' },
  });
  expect(published.status(), await published.text()).toBe(200);

  return {
    productId: product.id,
    skuIds: product.variants.map((variant) => variant.id),
  };
}

// Đặt một đơn COD (thanh toán khi nhận hàng) qua API thật từ danh sách SKU và địa chỉ chuẩn.
async function placeRealCodOrder(
  customerPage: Page,
  skuIds: string[],
  address: { provinceCode: string; wardCode: string; line: string },
  options: {
    contactName: string;
    suffix: string;
    invoice: VatInvoiceInput | null;
    cachedTotal?: number;
  },
): Promise<{ code: string; total: number }> {
  await clearCustomerCart(customerPage);

  for (const skuId of skuIds) {
    const cartResponse = await customerPage.request.post('/api/cart/items', {
      headers: ORIGIN_HEADERS,
      data: { skuId, quantity: 1 },
    });
    expect(cartResponse.status(), await cartResponse.text()).toBe(200);
  }

  // Dùng lại tổng tiền từ preview trước khi giỏ giống nhau để tránh chạm giới hạn 20 lượt mỗi phút.
  let expectedTotal = options.cachedTotal;
  if (expectedTotal === undefined) {
    const previewResponse = await customerPage.request.post('/api/checkout/previews', {
      headers: ORIGIN_HEADERS,
      data: { address, paymentMethod: 'cod' },
    });
    expect(previewResponse.status(), await previewResponse.text()).toBe(200);
    expectedTotal = (await previewResponse.json()).data.total as number;
  }

  // Đặt khóa `Idempotency-Key` riêng cho từng đơn để API không gộp các lần tạo liên tiếp.
  const orderResponse = await customerPage.request.post('/api/orders', {
    headers: {
      ...ORIGIN_HEADERS,
      'Idempotency-Key': `inv-print-${options.suffix}-${randomInt(100000, 999999)}`,
    },
    data: {
      contact: {
        name: options.contactName,
        phone: '0987001133',
        email: `hoadon.${options.suffix}@example.com`,
      },
      address,
      note: null,
      paymentMethod: 'cod',
      invoice: options.invoice,
      expectedTotal,
    },
  });
  expect(orderResponse.status(), await orderResponse.text()).toBe(201);

  return {
    code: (await orderResponse.json()).data.code as string,
    total: expectedTotal,
  };
}

// Hủy đơn hàng qua API quản trị để hóa đơn thật có trạng thái đã hủy.
async function cancelOrderByAdmin(
  adminPage: Page,
  orderCode: string,
  reason: string,
): Promise<void> {
  const response = await adminPage.request.post(
    `/api/admin/orders/${encodeURIComponent(orderCode)}/cancellation`,
    {
      headers: ORIGIN_HEADERS,
      data: { reason },
    },
  );
  expect(response.status(), await response.text()).toBe(200);
}

// Mời nhân viên qua hộp thoại giao diện để chọn đúng vai trò mới tạo theo mã vai trò.
async function inviteStaffWithRole(
  adminPage: Page,
  staff: { email: string; fullName: string; roleCode: string },
): Promise<void> {
  await adminPage.goto('/admin/staff');
  await adminPage.getByRole('button', { name: '+ Mời nhân viên', exact: true }).click();
  const invitation = adminPage.getByRole('dialog', { name: 'Mời nhân viên mới', exact: true });
  await invitation.getByLabel(/^Email nhân viên/).fill(staff.email);
  await invitation.getByLabel(/^Họ và tên/).fill(staff.fullName);
  await invitation.getByRole('checkbox', { name: `(${staff.roleCode})` }).check();
  await invitation.getByRole('button', { name: 'Gửi lời mời', exact: true }).click();
  await expect(invitation).toBeHidden();
}

test.describe('trang in hóa đơn bán hàng', () => {
  // Tạo sẵn đơn hàng thật một lần để mọi ca chạy độc lập mà không chạm giới hạn preview.
  test.beforeAll(async ({ browser }) => {
    // Cho phép tối đa 120 giây để tạo sản phẩm, đặt năm đơn hàng và hoàn nguyên cấu hình.
    test.setTimeout(120000);
    mkdirSync(EVIDENCE_DIRECTORY, { recursive: true });
    runSuffix = `${Date.now().toString(36)}${randomInt(100, 999).toString(36)}`;

    const adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    const customerContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    let initialSettings: CommerceSettingsSnapshot | undefined;
    let customerPage: Page | undefined;

    try {
      const adminPage = await adminContext.newPage();
      await loginAdmin(adminPage);
      initialSettings = await readCommerceSettings(adminPage);

      const singleProduct = await seedProduct(adminPage);
      const longVariantProduct = await seedMultiVariantProduct(adminPage, {
        variantCount: 15,
        suffix: `${runSuffix}-k8`,
        productName: `Bàn phím cơ không dây Keychron K8 Pro ${runSuffix}`,
        buildOptionValue: (index) =>
          `Switch Brown, Keycap PBT, ANSI ${String(index + 1).padStart(2, '0')}`,
      });
      const fortyVariantProduct = await seedMultiVariantProduct(adminPage, {
        variantCount: 40,
        suffix: `${runSuffix}-40`,
        productName: `Phụ kiện hóa đơn ${runSuffix}`,
        buildOptionValue: (index) => `Phiên bản ${String(index + 1).padStart(2, '0')}`,
      });
      await prepareShipping(adminPage);

      // Nâng tạm `maxCartLines` lên 40 để giỏ hàng chứa đủ 40 SKU khác nhau khi tạo đơn PDF (Portable Document Format).
      await saveCommerceSettings(adminPage, initialSettings, {
        maxCartLines: Math.max(initialSettings.maxCartLines, 40),
        codMaxAmount: Math.max(initialSettings.codMaxAmount, 20_000_000),
      });

      customerPage = await customerContext.newPage();
      await loginSeededCustomer(customerPage, e2eEnvironment.ordersCustomerEmail);
      const { provinceCode, wardCode } = await readFirstShippingAddress(customerPage);
      const address = { provinceCode, wardCode, line: '15 Đường Hóa Đơn' };

      // Đặt đơn đầu kèm thông tin VAT (thuế giá trị gia tăng) để kiểm khối hóa đơn công ty.
      const vatOrder = await placeRealCodOrder(customerPage, [singleProduct.skuId], address, {
        contactName: `Khách VAT ${runSuffix}`,
        suffix: `${runSuffix}-vat`,
        invoice: VAT_INVOICE_INPUT,
      });
      vatOrderCode = vatOrder.code;

      // Dùng lại `cachedTotal` vì giỏ hàng giống hệt đơn đầu, tránh chạm giới hạn preview.
      const cancelledOrder = await placeRealCodOrder(customerPage, [singleProduct.skuId], address, {
        contactName: `Khách hủy ${runSuffix}`,
        suffix: `${runSuffix}-can`,
        invoice: null,
        cachedTotal: vatOrder.total,
      });
      cancelledOrderCode = cancelledOrder.code;
      // Hủy đơn qua API quản trị để hóa đơn thật mang trạng thái đã hủy khi in.
      await cancelOrderByAdmin(adminPage, cancelledOrderCode, 'Khách đổi ý khi kiểm thử in');

      // Đặt đơn thường không VAT và không hủy để kiểm trường hợp thông tin shop trống.
      const plainOrder = await placeRealCodOrder(customerPage, [singleProduct.skuId], address, {
        contactName: `Khách thường ${runSuffix}`,
        suffix: `${runSuffix}-pln`,
        invoice: null,
        cachedTotal: vatOrder.total,
      });
      plainOrderCode = plainOrder.code;

      // Đặt đơn 15 biến thể tên dài kèm thông tin VAT để kiểm giới hạn một trang A4.
      const fifteenLineOrder = await placeRealCodOrder(
        customerPage,
        longVariantProduct.skuIds,
        address,
        {
          contactName: `Khách 15 dòng ${runSuffix}`,
          suffix: `${runSuffix}-15`,
          invoice: VAT_INVOICE_INPUT,
        },
      );
      fifteenLineOrderCode = fifteenLineOrder.code;
      // Hủy đơn 15 dòng để hóa đơn mang thêm dấu hủy ở trường hợp chiều cao lớn nhất.
      await cancelOrderByAdmin(adminPage, fifteenLineOrderCode, 'Kiểm thử 15 dòng vừa một trang');

      // Đặt đơn 40 SKU khác nhau để buộc bảng sản phẩm sang trang thứ hai khi xuất PDF.
      const fortyLineOrder = await placeRealCodOrder(
        customerPage,
        fortyVariantProduct.skuIds,
        address,
        {
          contactName: `Khách 40 dòng ${runSuffix}`,
          suffix: `${runSuffix}-40`,
          invoice: null,
        },
      );
      fortyLineOrderCode = fortyLineOrder.code;
    } finally {
      // Dọn giỏ hàng khách seed nếu ca lỗi giữa chừng để không làm bẩn các spec thương mại khác.
      if (customerPage) {
        try {
          await clearCustomerCart(customerPage);
        } catch {
          // Tiếp tục hoàn nguyên cấu hình và đóng ngữ cảnh kể cả khi giỏ không đọc được.
        }
      }
      // Hoàn nguyên `maxCartLines` và cấu hình gốc ngay sau khi tạo đơn, kể cả khi gặp lỗi.
      if (initialSettings) {
        try {
          const restorePage = await adminContext.newPage();
          await saveCommerceSettings(restorePage, initialSettings, {});
          await restorePage.close();
        } catch {
          // Đảm bảo vẫn đóng hai ngữ cảnh trình duyệt dù hoàn nguyên cấu hình gặp lỗi.
        }
      }
      await customerContext.close();
      await adminContext.close();
    }
  });

  // Đọc cấu hình bán hàng hiện tại trước mỗi ca để trả lại nguyên trạng sau khi ca kết thúc.
  test.beforeEach(async ({ browser }) => {
    previousSettings = undefined;
    const adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    try {
      const adminPage = await adminContext.newPage();
      await loginAdmin(adminPage);
      previousSettings = await readCommerceSettings(adminPage);
    } finally {
      await adminContext.close();
    }
  });

  // Hoàn nguyên cấu hình bán hàng dùng chung sau mỗi ca, kể cả khi ca thất bại giữa chừng.
  test.afterEach(async ({ browser }) => {
    if (!previousSettings) {
      return;
    }
    const adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    try {
      const adminPage = await adminContext.newPage();
      await loginAdmin(adminPage);
      await saveCommerceSettings(adminPage, previousSettings, {
        shop: previousSettings.shop,
        maxCartLines: previousSettings.maxCartLines,
        codMaxAmount: previousSettings.codMaxAmount,
      });
    } finally {
      await adminContext.close();
    }
  });

  test('hiển thị hai hóa đơn ngoài khung admin, in đen trắng một lần và không tràn ngang ở 375px', async ({
    page,
  }) => {
    const consoleIssues = collectConsoleIssues(page);
    await stubFontsOnContext(page.context());
    await stubWindowPrintOnContext(page.context());
    await loginAdmin(page);

    // Đặt cấu hình shop đủ bốn dòng để kiểm tra hiển thị đầy đủ phần đầu và khối VAT.
    await saveCommerceSettings(page, previousSettings!, { shop: FULL_SHOP });

    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto(
      `/admin/orders/print?codes=${encodeURIComponent(vatOrderCode)},${encodeURIComponent(cancelledOrderCode)}`,
    );

    const firstSheet = page.getByTestId(`invoice-${vatOrderCode}`);
    const secondSheet = page.getByTestId(`invoice-${cancelledOrderCode}`);
    await expect(firstSheet).toBeVisible();
    await expect(secondSheet).toBeVisible();

    // Route tĩnh không bị bắt bởi trang chi tiết đơn và không dựng thanh bên quản trị.
    await expect(page.locator('#admin-sidebar')).toHaveCount(0);
    await expect(page.getByRole('heading', { name: /Đơn hàng/ })).toHaveCount(0);
    await expect(secondSheet).toContainText('ĐƠN ĐÃ HỦY');

    // Đơn có yêu cầu xuất hóa đơn phải hiện khối VAT; đơn không yêu cầu không được hiện khối này.
    const firstVatBlock = firstSheet.locator('.invoice-vat');
    await expect(firstVatBlock).toBeVisible();
    await expect(firstVatBlock).toContainText(VAT_INVOICE_INPUT.companyName);
    await expect(firstVatBlock).toContainText(VAT_INVOICE_INPUT.taxCode);
    await expect(secondSheet.locator('.invoice-vat')).toHaveCount(0);
    await expect(secondSheet).not.toContainText('Thông tin xuất hóa đơn VAT');

    await expect.poll(() => readPrintCallCount(page)).toBe(1);

    await page.screenshot({
      path: resolve(EVIDENCE_DIRECTORY, '01-man-hinh-hai-hoa-don.png'),
      fullPage: true,
    });

    // Kiểm tra giao diện ở độ rộng 375 px không tràn ngang ngoài khung giấy.
    await page.setViewportSize({ width: 375, height: 812 });
    const overflow375 = await page.evaluate(
      () => document.documentElement.scrollWidth > window.innerWidth,
    );
    expect(overflow375).toBe(false);
    await page.screenshot({
      path: resolve(EVIDENCE_DIRECTORY, '07-man-hinh-375.png'),
      fullPage: true,
    });

    // Chuyển sang chế độ in để kiểm ẩn thanh công cụ và màu sắc đen trắng thuần.
    await page.setViewportSize({ width: 1280, height: 1120 });
    await page.emulateMedia({ media: 'print' });

    await expect(page.locator('.invoice-print-toolbar')).toBeHidden();
    await expect(firstSheet).toBeVisible();

    const printStyleCheck = await page.evaluate(() => {
      const sheets = Array.from(document.querySelectorAll('.invoice-sheet'));
      const nonWhiteBackgrounds: string[] = [];
      const nonBlackTexts: string[] = [];
      const shadowElements: string[] = [];
      const nonBlackHeaderBorders: string[] = [];

      for (const sheet of sheets) {
        const elements = [sheet, ...Array.from(sheet.querySelectorAll('*'))];
        for (const element of elements) {
          const style = window.getComputedStyle(element);
          const bg = style.backgroundColor;
          if (bg !== 'rgba(0, 0, 0, 0)' && bg !== 'transparent' && bg !== 'rgb(255, 255, 255)') {
            nonWhiteBackgrounds.push(`${element.tagName}.${element.className}:${bg}`);
          }
          if (style.color !== 'rgb(0, 0, 0)') {
            nonBlackTexts.push(`${element.tagName}.${element.className}:${style.color}`);
          }
          if (style.boxShadow !== 'none') {
            shadowElements.push(`${element.tagName}.${element.className}:${style.boxShadow}`);
          }
        }
        for (const headerCell of sheet.querySelectorAll('.invoice-items-table th')) {
          const borderTop = window.getComputedStyle(headerCell).borderTopColor;
          if (borderTop !== 'rgb(0, 0, 0)') {
            nonBlackHeaderBorders.push(borderTop);
          }
        }
      }
      const logoFilters = Array.from(document.querySelectorAll('.invoice-logo')).map(
        (logo) => window.getComputedStyle(logo).filter,
      );
      return {
        nonWhiteBackgrounds,
        nonBlackTexts,
        shadowElements,
        nonBlackHeaderBorders,
        logoFilters,
      };
    });

    expect(printStyleCheck.nonWhiteBackgrounds).toEqual([]);
    expect(printStyleCheck.nonBlackTexts).toEqual([]);
    expect(printStyleCheck.shadowElements).toEqual([]);
    expect(printStyleCheck.nonBlackHeaderBorders).toEqual([]);
    expect(printStyleCheck.logoFilters.every((value) => value.includes('grayscale'))).toBe(true);

    // Chụp đúng phần tử hóa đơn 1 theo chiều cao nguyên để tránh dính 1 px viền đầu của tờ 2.
    const firstBox = await firstSheet.boundingBox();
    expect(firstBox).not.toBeNull();
    await page.screenshot({
      path: resolve(EVIDENCE_DIRECTORY, '02-ban-in-hoa-don-vat.png'),
      clip: {
        x: Math.ceil(firstBox!.x),
        y: Math.ceil(firstBox!.y),
        width: Math.floor(firstBox!.width),
        height: Math.floor(firstBox!.height) - 1,
      },
    });
    await secondSheet.screenshot({
      path: resolve(EVIDENCE_DIRECTORY, '03-ban-in-don-da-huy.png'),
    });

    expect(consoleIssues).toEqual([]);
  });

  test('in tên Pickory và ẩn dòng trống khi shop chưa nhập thông tin', async ({ page }) => {
    const consoleIssues = collectConsoleIssues(page);
    await stubFontsOnContext(page.context());
    await stubWindowPrintOnContext(page.context());
    await loginAdmin(page);

    // Xóa bốn trường shop về `null` trên DB để kiểm tra nhánh dự phòng tên Pickory.
    await saveCommerceSettings(page, previousSettings!, { shop: EMPTY_SHOP });

    await page.setViewportSize({ width: 1280, height: 1000 });
    await page.goto(`/admin/orders/print?codes=${encodeURIComponent(plainOrderCode)}`);

    const shopInfo = page.locator('.invoice-shop-info');
    await expect(shopInfo).toBeVisible();
    await expect(shopInfo).toContainText('Pickory');
    await expect(shopInfo).not.toContainText('Địa chỉ:');
    await expect(shopInfo).not.toContainText('Điện thoại:');
    await expect(shopInfo).not.toContainText('Mã số thuế:');

    await page.emulateMedia({ media: 'print' });
    await page.screenshot({
      path: resolve(EVIDENCE_DIRECTORY, '04-ban-in-shop-trong.png'),
      fullPage: true,
    });

    expect(consoleIssues).toEqual([]);
  });

  test('lỗi 404 hiện mã đơn thiếu và không gọi window.print', async ({ page }) => {
    const missingCode = 'PKZZZZZZZZ';
    const consoleIssues = collectConsoleIssues(page, [
      { urlPattern: /\/api\/admin\/order-invoices/, status: 404 },
    ]);
    await stubFontsOnContext(page.context());
    await stubWindowPrintOnContext(page.context());
    await loginAdmin(page);

    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto(`/admin/orders/print?codes=${encodeURIComponent(vatOrderCode)},${missingCode}`);

    const alert = page.getByRole('alert');
    await expect(alert).toBeVisible();
    await expect(alert).toContainText('Không tìm thấy một số đơn hàng.');
    await expect(alert).toContainText(missingCode);
    await expect(alert).not.toContainText(vatOrderCode);
    expect(await readPrintCallCount(page)).toBe(0);

    await page.screenshot({
      path: resolve(EVIDENCE_DIRECTORY, '05-loi-404.png'),
      fullPage: true,
    });

    expect(consoleIssues).toEqual([]);
  });

  test('lỗi 403 hiện ForbiddenNotice và không gọi window.print', async ({ browser }) => {
    const roleCode = `inv_noperms_${runSuffix}`;
    const staffEmail = `e2e-inv-noperms-${runSuffix}@pickory.local`;

    const adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    const staffContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    let createdRoleId: string | undefined;
    let createdStaffId: string | undefined;

    try {
      const adminPage = await adminContext.newPage();
      await loginAdmin(adminPage);

      // Tạo vai trò chỉ có quyền `knowledge:read` để tài khoản nhân viên bị từ chối quyền `orders:read`.
      const roleResponse = await adminPage.request.post('/api/admin/roles', {
        headers: ORIGIN_HEADERS,
        data: {
          code: roleCode,
          name: `Xem tri thức ${runSuffix}`,
          permissions: ['knowledge:read'],
        },
      });
      expect(roleResponse.status(), await roleResponse.text()).toBe(201);
      createdRoleId = (await roleResponse.json()).data.id as string;

      await inviteStaffWithRole(adminPage, {
        email: staffEmail,
        fullName: `Nhân viên hạn chế ${runSuffix}`,
        roleCode,
      });

      // Tìm nhân viên vừa mời theo email để lấy `id` phục vụ bước gỡ vai trò khi dọn dẹp.
      const staffLookup = await adminPage.request.get(
        `/api/admin/staff?search=${encodeURIComponent(staffEmail)}`,
      );
      expect(staffLookup.status(), await staffLookup.text()).toBe(200);
      const staffItems = ((await staffLookup.json()).data?.items ?? []) as {
        id: string;
        email: string;
      }[];
      createdStaffId = staffItems.find((item) => item.email === staffEmail)?.id;
      expect(createdStaffId).toBeDefined();

      const staffPage = await staffContext.newPage();
      const consoleIssues = collectConsoleIssues(staffPage);
      await stubFontsOnContext(staffContext);
      await stubWindowPrintOnContext(staffContext);
      await activateStaffAccount(staffPage, staffEmail);

      await staffPage.setViewportSize({ width: 1280, height: 800 });
      await staffPage.goto(`/admin/orders/print?codes=${encodeURIComponent(vatOrderCode)}`);

      const alert = staffPage.getByRole('alert');
      await expect(alert).toBeVisible();
      await expect(alert).toContainText('403 - Không có quyền truy cập');
      expect(await readPrintCallCount(staffPage)).toBe(0);

      // Xác nhận trực tiếp API trả 403 vì giao diện đã chặn gọi API khi thiếu quyền `orders:read`.
      const directApiResponse = await staffPage.request.get(
        `/api/admin/order-invoices?codes=${encodeURIComponent(vatOrderCode)}`,
      );
      expect(directApiResponse.status(), await directApiResponse.text()).toBe(403);

      await staffPage.screenshot({
        path: resolve(EVIDENCE_DIRECTORY, '06-loi-403.png'),
        fullPage: true,
      });

      expect(consoleIssues).toEqual([]);
    } finally {
      try {
        const cleanupPage = await adminContext.newPage();
        if (createdStaffId) {
          // Gỡ hết vai trò khỏi nhân viên tạm trước để không vướng ràng buộc khi xóa vai trò.
          const unassignResponse = await cleanupPage.request.put(
            `/api/admin/staff/${encodeURIComponent(createdStaffId)}/roles`,
            {
              headers: ORIGIN_HEADERS,
              data: { roleCodes: [] },
            },
          );
          expect(unassignResponse.status(), await unassignResponse.text()).toBe(200);

          // Khóa tài khoản tạm sau khi xong ca vì hệ thống không có route xóa nhân viên.
          const lockResponse = await cleanupPage.request.patch(
            `/api/admin/staff/${encodeURIComponent(createdStaffId)}`,
            {
              headers: ORIGIN_HEADERS,
              data: { locked: true },
            },
          );
          expect(lockResponse.status(), await lockResponse.text()).toBe(200);
        }
        if (createdRoleId) {
          const deletedRole = await cleanupPage.request.delete(
            `/api/admin/roles/${encodeURIComponent(createdRoleId)}`,
            { headers: ORIGIN_HEADERS },
          );
          expect(deletedRole.status(), await deletedRole.text()).toBe(200);
        }
      } finally {
        await staffContext.close();
        await adminContext.close();
      }
    }
  });

  test('chưa đăng nhập chuyển hướng về trang đăng nhập quản trị', async ({ page }) => {
    const consoleIssues = collectConsoleIssues(page);
    await page.goto('/admin/orders/print?codes=PK7KQ2M9XA');
    await expect(page).toHaveURL(/\/admin\/login$/);
    expect(consoleIssues).toEqual([]);
  });

  test('xuất PDF A4: 15 dòng biến thể dài nhất vừa 1 trang, 40 dòng ra 2 trang lặp tiêu đề và 2 đơn ra 2 trang', async ({
    page,
  }) => {
    test.setTimeout(120000);
    const consoleIssues = collectConsoleIssues(page);
    await stubFontsOnContext(page.context());
    await stubWindowPrintOnContext(page.context());
    await loginAdmin(page);

    // Ca 15 dòng dùng tên sản phẩm 49 ký tự và 15 tùy chọn 33 ký tự, kèm shop 4 dòng, VAT và dấu hủy.
    await saveCommerceSettings(page, previousSettings!, { shop: FULL_SHOP });
    await page.goto(`/admin/orders/print?codes=${encodeURIComponent(fifteenLineOrderCode)}`);
    await expect(page.locator('tbody tr')).toHaveCount(15);
    await expect(page.getByTestId('invoice-cancelled-stamp')).toBeVisible();
    await expect(page.locator('.invoice-vat')).toBeVisible();
    await page.addStyleTag({
      content: '.invoice-items-table thead th { font-family: monospace !important; }',
    });
    await page.emulateMedia({ media: 'print' });
    const pdf15 = await page.pdf({ preferCSSPageSize: true });
    expect(countPdfPages(pdf15)).toBe(1);
    const pdf15Margin = readFirstPdfPageClipMargin(pdf15);
    expect(pdf15Margin).not.toBeNull();
    // Lề 12 mm trong hệ tọa độ 0.24 point của Chromium tương đương 141.7 đơn vị.
    expect(pdf15Margin!.x).toBeGreaterThan(130);
    expect(pdf15Margin!.y).toBeGreaterThan(130);
    const pdf15Pages = extractPdfPageTexts(pdf15);
    expect(pdf15Pages).toHaveLength(1);
    expect(pdf15Pages.filter((text) => text.includes('STT'))).toHaveLength(1);

    // Đặt shop trống cho ca 40 dòng nhằm giảm số dòng tiêu đề trên trang đầu khi xuất PDF.
    await saveCommerceSettings(page, previousSettings!, { shop: EMPTY_SHOP });
    await page.emulateMedia({ media: 'screen' });
    await page.goto(`/admin/orders/print?codes=${encodeURIComponent(fortyLineOrderCode)}`);
    await expect(page.locator('tbody tr')).toHaveCount(40);
    await page.addStyleTag({
      content: '.invoice-items-table thead th { font-family: monospace !important; }',
    });
    await page.emulateMedia({ media: 'print' });

    const tablePrintRules = await page.evaluate(() => {
      const thead = document.querySelector('.invoice-items-table thead');
      const rows = Array.from(document.querySelectorAll('.invoice-items-table tbody tr'));
      return {
        theadDisplay: thead ? window.getComputedStyle(thead).display : null,
        allRowsAvoidBreak: rows.every(
          (row) => window.getComputedStyle(row).breakInside === 'avoid',
        ),
      };
    });
    expect(tablePrintRules.theadDisplay).toBe('table-header-group');
    expect(tablePrintRules.allRowsAvoidBreak).toBe(true);

    const pdf40 = await page.pdf({ preferCSSPageSize: true });
    expect(countPdfPages(pdf40)).toBe(2);
    const pdf40Pages = extractPdfPageTexts(pdf40);
    expect(pdf40Pages).toHaveLength(2);
    // Cả trang 1 và trang 2 đều phải chứa tiêu đề `STT` của `thead`.
    expect(pdf40Pages.filter((text) => text.includes('STT'))).toHaveLength(2);

    // Ca đối chứng: tắt lặp tiêu đề bảng thì chữ `STT` chỉ còn xuất hiện ở trang đầu tiên.
    await page.addStyleTag({
      content: '.invoice-items-table thead { display: table-row-group !important; }',
    });
    const pdf40NoRepeat = await page.pdf({ preferCSSPageSize: true });
    expect(extractPdfPageTexts(pdf40NoRepeat).filter((text) => text.includes('STT'))).toHaveLength(
      1,
    );

    // Đặt lại shop đủ bốn dòng để kiểm tra in hai đơn thật tách thành đúng hai trang.
    await saveCommerceSettings(page, previousSettings!, { shop: FULL_SHOP });
    await page.emulateMedia({ media: 'screen' });
    await page.goto(
      `/admin/orders/print?codes=${encodeURIComponent(vatOrderCode)},${encodeURIComponent(cancelledOrderCode)}`,
    );
    await expect(page.locator('article.invoice-sheet')).toHaveCount(2);
    await page.addStyleTag({
      content: '.invoice-items-table thead th { font-family: monospace !important; }',
    });
    await page.emulateMedia({ media: 'print' });
    const pdfTwoOrders = await page.pdf({ preferCSSPageSize: true });
    expect(countPdfPages(pdfTwoOrders)).toBe(2);
    const pdfTwoOrderPages = extractPdfPageTexts(pdfTwoOrders);
    expect(pdfTwoOrderPages).toHaveLength(2);
    expect(pdfTwoOrderPages.filter((text) => text.includes('STT'))).toHaveLength(2);

    expect(consoleIssues).toEqual([]);
  });
});
