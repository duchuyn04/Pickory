import { randomInt } from 'node:crypto';
import { copyFile, mkdir, mkdtemp, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, test, type Browser, type BrowserContext, type Page } from '@playwright/test';
import {
  prepareShipping,
  readFirstShippingAddress,
  seedProduct,
  type SeededProduct,
} from './admin-order-fixtures';
import { collectConsoleIssues, loginAdmin } from './catalog-admin-flow';
import { loginSeededCustomer } from './commerce-order-fixtures';
import { e2eEnvironment } from './environment';
import {
  countPdfPages,
  extractPdfPageTexts,
  readPrintCallCount,
  stubWindowPrintOnContext,
} from './invoice-pdf-helpers';
import { installClickHighlight } from './video-cursor';

const SETTINGS_PATH = '/admin/commerce/settings';
const SETTINGS_API_PATH = '/api/admin/commerce/settings';
const ORIGIN_HEADERS = { Origin: e2eEnvironment.webOrigin };
const ACTIVE_BATCH_SIZE = 20;
const EVIDENCE_DIRECTORY = fileURLToPath(
  new URL('../../../evidence/exports-printing/EXP-E2E2', import.meta.url),
);
const PICTURE_DIRECTORY = path.join(EVIDENCE_DIRECTORY, 'picture');
const VIDEO_DIRECTORY = path.join(EVIDENCE_DIRECTORY, 'video');

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

const EMPTY_SHOP: ShopProfile = {
  name: null,
  address: null,
  phone: null,
  taxCode: null,
};

let previousSettings: CommerceSettingsSnapshot | undefined;
let seededProduct: SeededProduct;
let runSuffix: string;
let batchSearchTerm: string;
let batchOrderCodes: string[] = [];
let cancelledOrderCode = '';
let allSpecOrderCodes: string[] = [];

// Chặn tải font ngoài trên ngữ cảnh trình duyệt để lỗi mạng không gây cảnh báo giả ở console.
async function stubFontsOnContext(context: BrowserContext): Promise<void> {
  await context.route(/^https:\/\/fonts\.(googleapis|gstatic)\.com\//, (route) =>
    route.fulfill({ body: '', contentType: 'text/css' }),
  );
}

// Chuẩn hóa khoảng trắng để so khớp chữ giải nén từ PDF (Portable Document Format) khi trình duyệt tách khối chữ.
function normalizePdfText(text: string): string {
  return text.replace(/\s+/g, '');
}

// Xóa toàn bộ sản phẩm còn sót trong giỏ hàng của khách trước khi đặt lô đơn mới.
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

// Đặt nhiều đơn thanh toán khi nhận hàng (COD) qua API thật bằng một lần tính phí để tránh vượt giới hạn tần suất.
async function placeCodOrdersBatch(
  customerPage: Page,
  product: SeededProduct,
  contactName: string,
  count: number,
  suffix: string,
): Promise<string[]> {
  await clearCustomerCart(customerPage);
  const { provinceCode, wardCode } = await readFirstShippingAddress(customerPage);
  const address = { provinceCode, wardCode, line: '15 Đường Hóa Đơn' };
  let expectedTotal: number | undefined;
  const codes: string[] = [];

  for (let index = 0; index < count; index += 1) {
    const cartResponse = await customerPage.request.post('/api/cart/items', {
      headers: ORIGIN_HEADERS,
      data: { skuId: product.skuId, quantity: 1 },
    });
    expect(cartResponse.status(), await cartResponse.text()).toBe(200);

    // Chỉ gọi preview một lần vì các đơn dùng chung sản phẩm và địa chỉ giao giống nhau.
    if (expectedTotal === undefined) {
      const previewResponse = await customerPage.request.post('/api/checkout/previews', {
        headers: ORIGIN_HEADERS,
        data: { address, paymentMethod: 'cod' },
      });
      expect(previewResponse.status(), await previewResponse.text()).toBe(200);
      expectedTotal = (await previewResponse.json()).data.total as number;
    }

    // Khóa `Idempotency-Key` riêng cho từng lượt nhằm bảo đảm mỗi yêu cầu tạo một đơn độc lập.
    const orderResponse = await customerPage.request.post('/api/orders', {
      headers: {
        ...ORIGIN_HEADERS,
        'Idempotency-Key': `inv-e2e-${suffix}-${index}-${randomInt(100000, 999999)}`,
      },
      data: {
        contact: {
          name: contactName,
          phone: '0987001133',
          email: `hoadon.${suffix}@example.com`,
        },
        address,
        note: null,
        paymentMethod: 'cod',
        invoice: null,
        expectedTotal,
      },
    });
    expect(orderResponse.status(), await orderResponse.text()).toBe(201);
    codes.push((await orderResponse.json()).data.code as string);
  }

  return codes;
}

// Đọc cấu hình bán hàng hiện tại nhằm lưu lại trạng thái gốc trước khi ca thay đổi.
async function readCommerceSettings(adminPage: Page): Promise<CommerceSettingsSnapshot> {
  const response = await adminPage.request.get(SETTINGS_API_PATH);
  expect(response.status(), await response.text()).toBe(200);
  return (await response.json()).data as CommerceSettingsSnapshot;
}

// Ghi lại đủ bảy khóa của cấu hình bán hàng để đặt trạng thái shop độc lập giữa các ca.
async function saveCommerceSettings(
  adminPage: Page,
  base: CommerceSettingsSnapshot,
  shop: ShopProfile,
): Promise<void> {
  const response = await adminPage.request.put(SETTINGS_API_PATH, {
    headers: ORIGIN_HEADERS,
    data: {
      freeShippingThreshold: base.freeShippingThreshold,
      codMaxAmount: base.codMaxAmount,
      maxQuantityPerSku: base.maxQuantityPerSku,
      maxCartLines: base.maxCartLines,
      sepayAccountNumber: base.sepayAccountNumber,
      sepayBankCode: base.sepayBankCode,
      shop,
    },
  });
  expect(response.status(), await response.text()).toBe(200);
}

// Tạo danh sách đơn thật bằng API trước khi chạy để các ca in không phụ thuộc thứ tự chạy.
test.beforeAll(async ({ browser }) => {
  runSuffix = `${Date.now().toString(36)}${randomInt(100, 999).toString(36)}`;
  batchSearchTerm = `HD20-${runSuffix}`;

  const adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
  const customerContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
  try {
    const adminPage = await adminContext.newPage();
    await loginAdmin(adminPage);
    seededProduct = await seedProduct(adminPage);
    await prepareShipping(adminPage);

    const customerPage = await customerContext.newPage();
    await loginSeededCustomer(customerPage, e2eEnvironment.ordersCustomerEmail);
    batchOrderCodes = await placeCodOrdersBatch(
      customerPage,
      seededProduct,
      `Khách ${batchSearchTerm}`,
      ACTIVE_BATCH_SIZE,
      `${runSuffix}-b`,
    );
    const cancelledCodes = await placeCodOrdersBatch(
      customerPage,
      seededProduct,
      `Khách hủy ${runSuffix}`,
      1,
      `${runSuffix}-c`,
    );
    cancelledOrderCode = cancelledCodes[0]!;
    allSpecOrderCodes = [...batchOrderCodes, cancelledOrderCode];
  } finally {
    await customerContext.close();
    await adminContext.close();
  }
});

// Lưu cấu hình shop hiện có trước mỗi ca để khôi phục nguyên trạng khi ca kết thúc.
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

// Hoàn nguyên toàn bộ cấu hình shop sau mỗi ca để không làm ảnh hưởng các spec khác.
test.afterEach(async ({ browser }) => {
  if (!previousSettings) {
    return;
  }
  const adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
  try {
    const adminPage = await adminContext.newPage();
    await loginAdmin(adminPage);
    await saveCommerceSettings(adminPage, previousSettings, previousSettings.shop);
  } finally {
    await adminContext.close();
  }
});

// Mở ngữ cảnh quản trị đã chặn font ngoài và thay `window.print` để sẵn sàng mở tab in.
async function openAdminPrintContext(browser: Browser): Promise<{
  context: BrowserContext;
  page: Page;
  consoleIssues: string[];
}> {
  const context = await browser.newContext({
    baseURL: e2eEnvironment.webOrigin,
    viewport: { width: 1280, height: 800 },
  });
  await stubFontsOnContext(context);
  await stubWindowPrintOnContext(context);
  const page = await context.newPage();
  const consoleIssues = collectConsoleIssues(page);
  await loginAdmin(page);
  return { context, page, consoleIssues };
}

test('lưu thông tin shop, in một đơn từ chi tiết ra PDF A4 1 trang', async ({ browser }) => {
  // Cho phép 180 giây vì ca mở nhiều ngữ cảnh, quay video và xuất tệp PDF.
  test.setTimeout(180000);
  const videoTempDir = await mkdtemp(path.join(os.tmpdir(), 'pickory-invoice-video-'));
  const shopData = {
    name: `Shop E2E ${runSuffix}`,
    address: '123 Lê Lợi, Quận 1, TP.HCM',
    phone: '0901234567',
    taxCode: '0312345678',
  };
  const singleOrderCode = batchOrderCodes[0]!;

  // Quay video ở độ phân giải 1280x720 kèm con trỏ và nhãn thao tác trong thư mục tạm ngoài kho mã nguồn.
  const context = await browser.newContext({
    baseURL: e2eEnvironment.webOrigin,
    viewport: { width: 1280, height: 720 },
    recordVideo: {
      dir: videoTempDir,
      size: { width: 1280, height: 720 },
      showActions: { cursor: 'pointer', position: 'top-right', duration: 800, fontSize: 24 },
    },
  });
  let contextClosed = false;
  // Gắn vòng tròn đỏ và gợn sóng khi bấm chỉ cho ngữ cảnh quay video này.
  await installClickHighlight(context);
  await stubFontsOnContext(context);
  await stubWindowPrintOnContext(context);
  const page = await context.newPage();
  const consoleIssues = collectConsoleIssues(page);
  const popupConsoleIssues: string[] = [];

  try {
    await test.step('Đăng nhập quản trị và đóng thanh cookie để khung hình video thông thoáng', async () => {
      await page.goto('/admin/login');
      const consentButton = page.getByRole('button', { name: 'Chỉ cần thiết' });
      // Chờ nút cookie hiện rồi bấm đóng hẳn. Thanh đáy không được che khung hình video.
      await consentButton.click();
      await expect(consentButton).toBeHidden();

      await page.getByLabel(/^Email/).fill(e2eEnvironment.adminEmail);
      await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.adminPassword);
      await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
      await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin`);
    });

    await test.step('Nhập và lưu thông tin shop trên trang cấu hình bán hàng', async () => {
      // Đưa shop về trống trước khi mở trang để ca không phụ thuộc dữ liệu sót lại.
      await saveCommerceSettings(page, previousSettings!, EMPTY_SHOP);
      await page.goto(SETTINGS_PATH);
      await expect(page.getByLabel('Tên shop', { exact: true })).toBeVisible();

      // Hiện thẻ chương trước khi điền bốn ô shop để người xem video theo dõi đúng ngữ cảnh.
      await page.screencast.showChapter('Nhập thông tin shop trên hóa đơn', { duration: 1500 });
      await page.getByLabel('Tên shop', { exact: true }).fill(shopData.name);
      await page.getByLabel('Địa chỉ', { exact: true }).fill(shopData.address);
      await page.getByLabel('Số điện thoại', { exact: true }).fill(shopData.phone);
      await page.getByLabel('Mã số thuế', { exact: true }).fill(shopData.taxCode);

      // Hiện thẻ chương trước khi bấm Lưu để đánh dấu rõ thao tác ghi cấu hình.
      await page.screencast.showChapter('Lưu cấu hình thông tin shop', { duration: 1500 });
      const saveResponsePromise = page.waitForResponse(
        (response) =>
          new URL(response.url()).pathname === SETTINGS_API_PATH &&
          response.request().method() === 'PUT',
      );
      await page.getByRole('button', { name: 'Lưu', exact: true }).click();
      const saveResponse = await saveResponsePromise;
      expect(saveResponse.status()).toBe(200);
      await expect(page.getByRole('status')).toHaveText('Đã lưu cấu hình bán hàng.');
      // Dừng hai giây sau khi lưu thành công để người xem video thấy rõ bốn ô và thông báo.
      await page.waitForTimeout(2000);
    });

    const popupVideo =
      await test.step('Mở chi tiết đơn và bấm In hóa đơn để mở tab in thật', async () => {
        // Hiện thẻ chương trước khi mở chi tiết đơn để tách bạch phần cấu hình và phần đơn hàng.
        await page.screencast.showChapter('Mở chi tiết đơn hàng', { duration: 1500 });
        await page.goto(`/admin/orders/${encodeURIComponent(singleOrderCode)}`);
        await expect(
          page.getByRole('heading', { level: 1, name: `Đơn hàng ${singleOrderCode}` }),
        ).toBeVisible();
        await page.waitForTimeout(2000);

        const printLink = page.getByRole('link', { name: 'In hóa đơn', exact: true });
        await expect(printLink).toBeVisible();
        // Hiện thẻ chương ngay trước lúc bấm nút để người xem nhận ra hành động mở tab in.
        await page.screencast.showChapter('Bấm nút In hóa đơn', { duration: 1500 });

        // Đăng ký chờ sự kiện mở tab trước khi bấm để tránh bỏ lỡ tab mới mở quá nhanh.
        const popupPromise = context.waitForEvent('page');
        await printLink.click();
        const popup = await popupPromise;
        // Giữ tham chiếu video của tab in trước khi đóng tab để chép sang thư mục bằng chứng.
        const recordedPopupVideo = popup.video();
        // Dừng ngắn trên tab chi tiết sau khi bấm để video đầu ghi rõ con trỏ và gợn sóng.
        await page.waitForTimeout(1500);

        // Chỉ ghi lỗi console từ lúc nhận được đối tượng `popup` vì sự kiện mở tab chạy trước đó.
        const trackedIssues = collectConsoleIssues(popup);
        // Đặt bề rộng 1280 px cho tab in để ảnh chụp bằng chứng đúng kích thước quy định.
        await popup.setViewportSize({ width: 1280, height: 800 });

        await expect(popup).toHaveURL(
          `${e2eEnvironment.webOrigin}/admin/orders/print?codes=${encodeURIComponent(singleOrderCode)}`,
        );

        const sheet = popup.getByTestId(`invoice-${singleOrderCode}`);
        await expect(sheet).toBeVisible();
        await expect(popup.locator('article.invoice-sheet')).toHaveCount(1);
        // Chờ lượt gọi in bằng 1 vì trang chỉ gọi `window.print` sau khi logo và font tải xong.
        await expect.poll(() => readPrintCallCount(popup)).toBe(1);

        // Kiểm khối đầu tiên của hóa đơn là phần tiêu đề chứa tên shop trước thông tin khách hàng.
        const firstBlock = sheet.locator(':scope > *').first();
        await expect(firstBlock).toHaveClass(/invoice-header/);
        const shopInfo = firstBlock.locator('.invoice-shop-info');
        await expect(shopInfo.locator(':scope > *').first()).toHaveClass(/invoice-shop-name/);
        await expect(shopInfo.locator('.invoice-shop-name')).toHaveText(shopData.name);
        await expect(shopInfo).toContainText(`Địa chỉ: ${shopData.address}`);
        await expect(shopInfo).toContainText(`Điện thoại: ${shopData.phone}`);
        await expect(shopInfo).toContainText(`Mã số thuế: ${shopData.taxCode}`);

        // Kiểm vị trí hiển thị dọc để khối shop nằm phía trên khối thông tin khách hàng.
        const shopBox = await shopInfo.boundingBox();
        const partiesBox = await sheet.locator('.invoice-parties').boundingBox();
        expect(shopBox).not.toBeNull();
        expect(partiesBox).not.toBeNull();
        expect(shopBox!.y).toBeLessThan(partiesBox!.y);

        // Phép đối chứng cho đơn chưa hủy: hóa đơn không được hiện dấu đơn đã hủy.
        await expect(sheet).not.toContainText('ĐƠN ĐÃ HỦY');

        await mkdir(PICTURE_DIRECTORY, { recursive: true });
        await popup.screenshot({
          path: path.join(PICTURE_DIRECTORY, '01-hoa-don-co-thong-tin-shop.png'),
          fullPage: true,
        });

        // Dừng hai giây trên tab in trước khi chuyển chế độ in để video thứ hai ghi đủ hóa đơn.
        await popup.waitForTimeout(2000);

        // Bật kiểu in để áp dụng CSS khi in, đồng thời xuất PDF khổ trang theo CSS không nền.
        await popup.emulateMedia({ media: 'print' });
        const pdfBuffer = await popup.pdf({
          format: 'A4',
          printBackground: false,
          preferCSSPageSize: true,
        });
        expect(countPdfPages(pdfBuffer)).toBe(1);

        const pdfPages = extractPdfPageTexts(pdfBuffer);
        expect(pdfPages).toHaveLength(1);
        const pageText = pdfPages[0]!;
        expect(pageText).toContain(singleOrderCode);
        expect(normalizePdfText(pageText)).toContain(normalizePdfText(shopData.name));
        expect(normalizePdfText(pageText)).not.toContain(normalizePdfText('ĐƠN ĐÃ HỦY'));

        // Bảo đảm trang PDF của một đơn không lẫn mã đơn khác thuộc cùng lượt chạy.
        for (const otherCode of allSpecOrderCodes) {
          if (otherCode !== singleOrderCode) {
            expect(pageText).not.toContain(otherCode);
          }
        }

        // Đọc lại số lần gọi in sau khi xuất PDF để chắc chắn trang không gọi in lần hai.
        expect(await readPrintCallCount(popup)).toBe(1);

        // Lưu danh sách lỗi console của tab in để kiểm tra tập trung ở cuối ca.
        popupConsoleIssues.push(...trackedIssues);
        // Đóng tab in sau khi kiểm tra xong toàn bộ dữ liệu và ảnh chụp của lượt in.
        await popup.close();
        return recordedPopupVideo;
      });

    expect(consoleIssues).toEqual([]);
    expect(popupConsoleIssues).toEqual([]);

    const video = page.video();
    await context.close();
    contextClosed = true;

    // Xác nhận Playwright đã ghi nhận cả hai đối tượng video trước khi sao chép sang thư mục bằng chứng.
    expect(video).not.toBeNull();
    expect(popupVideo).not.toBeNull();
    await mkdir(VIDEO_DIRECTORY, { recursive: true });
    await copyFile(await video!.path(), path.join(VIDEO_DIRECTORY, '01-luong-in-hoa-don.webm'));
    await copyFile(await popupVideo!.path(), path.join(VIDEO_DIRECTORY, '02-tab-in-hoa-don.webm'));
  } finally {
    if (!contextClosed) {
      await context.close();
    }
    await rm(videoTempDir, { recursive: true, force: true });
  }
});

test('lọc và in 20 đơn từ danh sách ra PDF 20 trang, mỗi trang một mã khác nhau', async ({
  browser,
}) => {
  // Cho phép 180 giây vì ca đăng nhập nhiều ngữ cảnh và xuất tệp PDF 20 trang.
  test.setTimeout(180000);
  const { context, page, consoleIssues } = await openAdminPrintContext(browser);
  const popupConsoleIssues: string[] = [];

  try {
    // Đặt cấu hình shop cố định trước khi mở danh sách để ca không phụ thuộc thứ tự chạy.
    await saveCommerceSettings(page, previousSettings!, {
      name: `Shop Lô ${runSuffix}`,
      address: '45 Hai Bà Trưng, Hà Nội',
      phone: '0912345678',
      taxCode: '0101234567',
    });

    let displayedCodes: string[] = [];
    await test.step('Lọc theo hậu tố duy nhất và chọn đủ 20 đơn trên danh sách', async () => {
      await page.goto('/admin/orders');
      await expect(page.getByRole('heading', { level: 1, name: 'Quản lý đơn hàng' })).toBeVisible();

      const searchInput = page.getByLabel('Tìm theo mã đơn, tên, số điện thoại hoặc email', {
        exact: true,
      });
      await searchInput.fill(batchSearchTerm);
      // Nhấn Enter để gửi truy vấn tìm kiếm ngay thay vì chờ bộ đếm trễ khi gõ.
      await searchInput.press('Enter');

      await expect(page).toHaveURL(new RegExp(`search=${encodeURIComponent(batchSearchTerm)}`));
      await expect(page.locator('table.order-table tbody tr')).toHaveCount(ACTIVE_BATCH_SIZE);

      const rawCodes = await page
        .locator('table.order-table tbody tr th[scope="row"] a')
        .allInnerTexts();
      displayedCodes = rawCodes.map((code) => code.trim());
      expect(displayedCodes).toHaveLength(ACTIVE_BATCH_SIZE);
      expect(new Set(displayedCodes)).toEqual(new Set(batchOrderCodes));

      await page
        .getByRole('checkbox', { name: 'Chọn tất cả đơn trong trang', exact: true })
        .click();
      const selectionBar = page.locator('.order-selection-bar');
      await expect(selectionBar).toContainText(`Đã chọn ${ACTIVE_BATCH_SIZE} đơn`);
      await expect(
        selectionBar.getByRole('button', { name: 'In hóa đơn', exact: true }),
      ).toBeEnabled();

      await mkdir(PICTURE_DIRECTORY, { recursive: true });
      await page.screenshot({
        path: path.join(PICTURE_DIRECTORY, '02-danh-sach-chon-20-don.png'),
        fullPage: true,
      });
    });

    await test.step('Bấm In hóa đơn từ danh sách và đối chiếu PDF 20 trang theo đúng thứ tự', async () => {
      const printButton = page
        .locator('.order-selection-bar')
        .getByRole('button', { name: 'In hóa đơn', exact: true });
      // Đăng ký chờ sự kiện mở tab trước khi bấm để tránh bỏ lỡ tab mới mở quá nhanh.
      const popupPromise = context.waitForEvent('page');
      await printButton.click();
      const popup = await popupPromise;
      // Chỉ ghi lỗi console từ lúc nhận được đối tượng `popup` vì sự kiện mở tab chạy trước đó.
      const trackedIssues = collectConsoleIssues(popup);
      // Đặt bề rộng 1280 px cho tab in để ảnh chụp bằng chứng đúng kích thước quy định.
      await popup.setViewportSize({ width: 1280, height: 800 });

      const expectedCodesQuery = displayedCodes.map((code) => encodeURIComponent(code)).join(',');
      await expect(popup).toHaveURL(
        `${e2eEnvironment.webOrigin}/admin/orders/print?codes=${expectedCodesQuery}`,
      );

      const sheets = popup.locator('article.invoice-sheet');
      await expect(sheets).toHaveCount(ACTIVE_BATCH_SIZE);
      // Chờ lượt gọi in bằng 1 vì trang chỉ gọi `window.print` sau khi logo và font tải xong.
      await expect.poll(() => readPrintCallCount(popup)).toBe(1);

      // Kiểm thứ tự hóa đơn trên DOM (cây phần tử của trang) khớp thứ tự dòng trên danh sách.
      const renderedCodes = await sheets.evaluateAll((elements) =>
        elements.map(
          (element) => element.getAttribute('data-testid')?.replace(/^invoice-/, '') ?? '',
        ),
      );
      expect(renderedCodes).toEqual(displayedCodes);

      const sheetText = await popup.locator('.invoice-print-sheets').innerText();
      expect(sheetText).not.toContain('ĐƠN ĐÃ HỦY');

      await popup.screenshot({
        path: path.join(PICTURE_DIRECTORY, '03-tab-in-20-don.png'),
        fullPage: false,
      });

      // Bật kiểu in để áp dụng CSS khi in, đồng thời xuất PDF khổ trang theo CSS không nền.
      await popup.emulateMedia({ media: 'print' });
      const pdf20 = await popup.pdf({
        format: 'A4',
        printBackground: false,
        preferCSSPageSize: true,
      });
      expect(countPdfPages(pdf20)).toBe(ACTIVE_BATCH_SIZE);

      const pdfPages = extractPdfPageTexts(pdf20);
      expect(pdfPages).toHaveLength(ACTIVE_BATCH_SIZE);

      for (let index = 0; index < ACTIVE_BATCH_SIZE; index += 1) {
        const pageText = pdfPages[index]!;
        const expectedCode = displayedCodes[index]!;
        // Trích mọi mã đơn dạng `PK` kèm tám ký tự hoa trên trang để phát hiện mã lạ lọt vào.
        const foundCodesOnPage = [...new Set(pageText.match(/PK[A-Z0-9]{8}/g) ?? [])];
        expect(foundCodesOnPage).toEqual([expectedCode]);
        expect(normalizePdfText(pageText)).not.toContain(normalizePdfText('ĐƠN ĐÃ HỦY'));
      }

      // Đọc lại số lần gọi in sau khi xuất PDF để chắc chắn trang không gọi in lần hai.
      expect(await readPrintCallCount(popup)).toBe(1);

      // Lưu danh sách lỗi console của tab in để kiểm tra tập trung ở cuối ca.
      popupConsoleIssues.push(...trackedIssues);
      // Đóng tab in sau khi kiểm tra xong toàn bộ dữ liệu và ảnh chụp của lượt in.
      await popup.close();
    });

    expect(consoleIssues).toEqual([]);
    expect(popupConsoleIssues).toEqual([]);
  } finally {
    await context.close();
  }
});

test('đơn đã hủy hiện dấu ĐƠN ĐÃ HỦY ở đầu hóa đơn và trên trang 1 PDF', async ({ browser }) => {
  // Cho phép 180 giây vì ca đăng nhập nhiều ngữ cảnh và xuất tệp PDF.
  test.setTimeout(180000);
  const { context, page, consoleIssues } = await openAdminPrintContext(browser);
  const popupConsoleIssues: string[] = [];

  try {
    // Đặt cấu hình shop cố định cho ca hủy để kết quả không phụ thuộc ca chạy trước.
    await saveCommerceSettings(page, previousSettings!, {
      name: `Shop Hủy ${runSuffix}`,
      address: '99 Nguyễn Huệ, Quận 1, TP.HCM',
      phone: '0909888777',
      taxCode: '0309876543',
    });

    await test.step('Hủy đơn qua API thật và xác nhận trạng thái trước khi mở trang chi tiết', async () => {
      // Hủy đơn qua API thật rồi đọc lại chi tiết để xác nhận đơn đã sang `cancelled` trước khi in.
      const cancelResponse = await page.request.post(
        `/api/admin/orders/${encodeURIComponent(cancelledOrderCode)}/cancellation`,
        {
          headers: ORIGIN_HEADERS,
          data: { reason: 'Kiểm thử in hóa đơn đã hủy' },
        },
      );
      expect(cancelResponse.status(), await cancelResponse.text()).toBe(200);

      const detailResponse = await page.request.get(
        `/api/admin/orders/${encodeURIComponent(cancelledOrderCode)}`,
      );
      expect(detailResponse.status(), await detailResponse.text()).toBe(200);
      expect((await detailResponse.json()).data.status).toBe('cancelled');
    });

    await test.step('In đơn đã hủy từ trang chi tiết và kiểm dấu ĐƠN ĐÃ HỦY trên DOM cùng PDF', async () => {
      await page.goto(`/admin/orders/${encodeURIComponent(cancelledOrderCode)}`);
      await expect(
        page.getByRole('heading', { level: 1, name: `Đơn hàng ${cancelledOrderCode}` }),
      ).toBeVisible();

      // Đăng ký chờ sự kiện mở tab trước khi bấm để tránh bỏ lỡ tab mới mở quá nhanh.
      const popupPromise = context.waitForEvent('page');
      await page.getByRole('link', { name: 'In hóa đơn', exact: true }).click();
      const popup = await popupPromise;
      // Chỉ ghi lỗi console từ lúc nhận được đối tượng `popup` vì sự kiện mở tab chạy trước đó.
      const trackedIssues = collectConsoleIssues(popup);
      // Đặt bề rộng 1280 px cho tab in để ảnh chụp bằng chứng đúng kích thước quy định.
      await popup.setViewportSize({ width: 1280, height: 800 });

      await expect(popup).toHaveURL(
        `${e2eEnvironment.webOrigin}/admin/orders/print?codes=${encodeURIComponent(cancelledOrderCode)}`,
      );

      const sheet = popup.getByTestId(`invoice-${cancelledOrderCode}`);
      await expect(sheet).toBeVisible();
      // Chờ lượt gọi in bằng 1 vì trang chỉ gọi `window.print` sau khi logo và font tải xong.
      await expect.poll(() => readPrintCallCount(popup)).toBe(1);

      const stamp = sheet.getByTestId('invoice-cancelled-stamp');
      await expect(stamp).toHaveText('ĐƠN ĐÃ HỦY');

      // Kiểm dấu hủy là phần tử đầu tiên của hóa đơn và nằm phía trên phần đầu thông tin shop.
      await expect(sheet.locator(':scope > *').first()).toHaveClass(/invoice-cancelled-banner/);
      const stampBox = await stamp.boundingBox();
      const headerBox = await sheet.locator('.invoice-header').boundingBox();
      expect(stampBox).not.toBeNull();
      expect(headerBox).not.toBeNull();
      expect(stampBox!.y).toBeLessThan(headerBox!.y);

      await mkdir(PICTURE_DIRECTORY, { recursive: true });
      await popup.screenshot({
        path: path.join(PICTURE_DIRECTORY, '04-hoa-don-don-da-huy.png'),
        fullPage: true,
      });

      // Bật kiểu in để áp dụng CSS khi in, đồng thời xuất PDF khổ trang theo CSS không nền.
      await popup.emulateMedia({ media: 'print' });
      const pdfBuffer = await popup.pdf({
        format: 'A4',
        printBackground: false,
        preferCSSPageSize: true,
      });
      expect(countPdfPages(pdfBuffer)).toBe(1);

      const pdfPages = extractPdfPageTexts(pdfBuffer);
      expect(pdfPages).toHaveLength(1);
      expect(pdfPages[0]!).toContain(cancelledOrderCode);
      expect(normalizePdfText(pdfPages[0]!)).toContain(normalizePdfText('ĐƠN ĐÃ HỦY'));

      // Đọc lại số lần gọi in sau khi xuất PDF để chắc chắn trang không gọi in lần hai.
      expect(await readPrintCallCount(popup)).toBe(1);

      // Lưu danh sách lỗi console của tab in để kiểm tra tập trung ở cuối ca.
      popupConsoleIssues.push(...trackedIssues);
      // Đóng tab in sau khi kiểm tra xong toàn bộ dữ liệu và ảnh chụp của lượt in.
      await popup.close();
    });

    expect(consoleIssues).toEqual([]);
    expect(popupConsoleIssues).toEqual([]);
  } finally {
    await context.close();
  }
});
