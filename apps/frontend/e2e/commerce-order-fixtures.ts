import { randomInt } from 'node:crypto';
import {
  expect,
  type APIResponse,
  type Browser,
  type BrowserContext,
  type Locator,
  type Page,
} from '@playwright/test';
import { collectConsoleIssues, tinyPng, dismissConsent } from './catalog-admin-flow';
import { e2eEnvironment } from './environment';
import { readFirstShippingAddress, type SeededProduct } from './admin-order-fixtures';

// Hàm dùng chung của các spec khách mua hàng: đổi cấu hình commerce, đi tới trang đặt hàng và bắn webhook SePay.

const adminHeaders = { Origin: e2eEnvironment.webOrigin };

export interface CommerceSettings {
  freeShippingThreshold: number;
  codMaxAmount: number;
  maxQuantityPerSku: number;
  maxCartLines: number;
  sepayAccountNumber: string | null;
  sepayBankCode: string | null;
}

// Chỉ gửi đúng các trường mà API cấu hình nhận, để dữ liệu thừa của lần đọc không làm API từ chối.
export function toSettingsPayload(settings: CommerceSettings): CommerceSettings {
  return {
    freeShippingThreshold: settings.freeShippingThreshold,
    codMaxAmount: settings.codMaxAmount,
    maxQuantityPerSku: settings.maxQuantityPerSku,
    maxCartLines: settings.maxCartLines,
    sepayAccountNumber: settings.sepayAccountNumber,
    sepayBankCode: settings.sepayBankCode,
  };
}

// Đọc cấu hình hiện tại để spec trả lại đúng giá trị này khi kết thúc.
export async function readCommerceSettings(adminPage: Page): Promise<CommerceSettings> {
  const response = await adminPage.request.get('/api/admin/commerce/settings');
  expect(response.status(), await response.text()).toBe(200);
  return toSettingsPayload((await response.json()).data);
}

export async function saveCommerceSettings(adminPage: Page, settings: CommerceSettings) {
  const saved = await adminPage.request.put('/api/admin/commerce/settings', {
    headers: adminHeaders,
    data: toSettingsPayload(settings),
  });
  expect(saved.status(), await saved.text()).toBe(200);
}

// Đổi "đ" và dấu chấm ngăn cách nghìn về số nguyên. Ví dụ "-26.500 đ" thành 26500.
export async function readAmount(row: Locator): Promise<number> {
  const text = await row.locator('strong').innerText();
  return Number(text.replace(/\D/g, ''));
}

export const checkoutSummaryOf = (page: Page) =>
  page.getByRole('complementary', { name: 'Tóm tắt đơn hàng' });

export const checkoutRowFor = (page: Page) => (label: string) =>
  checkoutSummaryOf(page)
    .locator('.checkout-summary-row')
    .filter({ has: page.getByText(label, { exact: true }) });

export async function stubProductImages(page: Page) {
  await page.route('https://res.cloudinary.com/**', (route) =>
    route.fulfill({ body: tinyPng, contentType: 'image/png' }),
  );
}

// Trang tải phông chữ từ Google Fonts. Mạng chập chờn làm lỗi tải hiện trong console và test đỏ oan.
export async function stubWebFonts(page: Page) {
  // Trả CSS rỗng để test không phụ thuộc mạng ngoài. Phông chữ không ảnh hưởng điều test kiểm.
  await page.route(/^https:\/\/fonts\.(googleapis|gstatic)\.com\//, (route) =>
    route.fulfill({ body: '', contentType: 'text/css' }),
  );
}

// Đi từ trang sản phẩm qua mini cart và trang giỏ tới trang đặt hàng, như khách thật.
export async function addToCartAndOpenCheckout(page: Page, product: SeededProduct) {
  await page.goto(`/p/${product.slug}`);
  await dismissConsent(page);
  await page.getByRole('button', { name: 'Thêm vào giỏ' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Đã thêm vào giỏ' })).toBeVisible();
  await page.getByRole('button', { name: /^Giỏ hàng, / }).click();
  await page.getByRole('link', { name: 'Xem giỏ hàng' }).click();
  await expect(page).toHaveURL(/\/cart$/);
  await page.getByRole('link', { name: 'Tiến hành đặt hàng' }).click();
  await expect(page).toHaveURL(/\/checkout$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Đặt hàng' })).toBeVisible();
}

export interface SepayWebhookInput {
  accountNumber: string;
  orderCode: string;
  amount: number;
}

// Bắn webhook giả lập như SePay: khóa API của môi trường E2E, nội dung chuyển khoản là mã đơn.
// Trả response để spec tự kiểm mã trạng thái.
export async function sendSepayWebhook(
  page: Page,
  { accountNumber, orderCode, amount }: SepayWebhookInput,
): Promise<APIResponse> {
  return page.request.post('/api/webhooks/sepay', {
    headers: { Authorization: `Apikey ${e2eEnvironment.sepayWebhookApiKey}` },
    data: {
      // Mã giao dịch ngẫu nhiên để lần bắn sau không bị coi là giao dịch trùng.
      id: randomInt(100000000, 999999999),
      gateway: 'Vietcombank',
      transactionDate: '2026-10-12 10:00:00',
      accountNumber,
      subAccount: '',
      code: null,
      content: orderCode,
      transferType: 'in',
      description: '',
      transferAmount: amount,
      accumulated: 105000000,
      referenceCode: `E2E-${orderCode}`,
    },
  });
}

// Đặt trạng thái gốc của một vận đơn trong `FakeGhnClient`, như GHN đổi trạng thái sau khi shipper giao hàng.
// Route chỉ có khi API chạy bằng `e2e:start` và thiếu `GHN_TOKEN`, còn đơn trong DB chỉ đổi khi webhook tới.
export async function setFakeGhnStatus(
  page: Page,
  trackingCode: string,
  rawStatus: string,
): Promise<APIResponse> {
  return page.request.post(`/api/e2e/ghn/orders/${encodeURIComponent(trackingCode)}/status`, {
    // Route ghi nên phải có `Origin` của web, giống mọi route ghi ngoài webhook.
    headers: { Origin: e2eEnvironment.webOrigin },
    data: { status: rawStatus },
  });
}

// Gọi webhook GHN thật như GHN làm. Body chỉ mang mã vận đơn, API tự hỏi trạng thái thật từ client GHN.
export async function sendGhnWebhook(page: Page, trackingCode: string): Promise<APIResponse> {
  return page.request.post('/api/webhooks/ghn', {
    headers: { 'X-Pickory-Webhook-Secret': e2eEnvironment.ghnWebhookSecret },
    data: { OrderCode: trackingCode },
  });
}

export interface IsolatedSession {
  context: BrowserContext;
  page: Page;
  consoleIssues: string[];
}

// Mở một trình duyệt riêng với phông chữ và ảnh đã chặn, để phiên đăng nhập của các vai trò không lẫn nhau.
export async function openIsolatedSession(browser: Browser): Promise<IsolatedSession> {
  const context = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
  const page = await context.newPage();
  const consoleIssues = collectConsoleIssues(page);
  // Chặn phông chữ ngoài và ảnh Cloudinary để lỗi mạng ngoài không làm console có lỗi oan.
  await stubWebFonts(page);
  await stubProductImages(page);
  return { context, page, consoleIssues };
}

// Đăng nhập khách đã seed bằng API phiên, như `commerce-orders.spec.ts`. Cookie phiên nằm trong context của `page`.
export async function loginSeededCustomer(page: Page, email: string) {
  const session = await page.request.post('/api/auth/sessions', {
    headers: { Origin: e2eEnvironment.webOrigin },
    data: { email, password: e2eEnvironment.accountPassword },
  });
  expect(session.status(), await session.text()).toBe(201);
}

// Khách đã đăng nhập đặt một đơn COD bằng API và nhận mã đơn. COM-E2E1 đã kiểm giao diện đặt hàng, nên bước dựng dữ liệu đi bằng API.
export async function placeCodOrderByApi(page: Page, product: SeededProduct): Promise<string> {
  const headers = { Origin: e2eEnvironment.webOrigin };

  // Dùng cùng địa chỉ đầu tiên với `prepareShipping`, nên GHN giả tính được phí.
  const { provinceCode, wardCode } = await readFirstShippingAddress(page);
  const address = { provinceCode, wardCode, line: '15 Đường Đánh Giá' };

  const cart = await page.request.post('/api/cart/items', {
    headers,
    data: { skuId: product.skuId, quantity: 1 },
  });
  expect(cart.status(), await cart.text()).toBe(200);

  // Tổng dự kiến lấy từ preview, giống khách xem trước khi bấm đặt. API từ chối nếu tổng lệch.
  const preview = await page.request.post('/api/checkout/previews', {
    headers,
    data: { address, paymentMethod: 'cod' },
  });
  expect(preview.status(), await preview.text()).toBe(200);
  const expectedTotal = (await preview.json()).data.total as number;

  const placed = await page.request.post('/api/orders', {
    headers: {
      ...headers,
      // Khóa ngẫu nhiên cho mỗi lần đặt, để hai đơn của cùng spec không bị coi là một.
      'Idempotency-Key': `e2e-reviews-${Date.now().toString(36)}-${randomInt(100000, 999999)}`,
    },
    data: {
      contact: { name: 'Lê Văn Đánh', phone: '0987001133', email: 'danh.review@example.com' },
      address,
      note: null,
      paymentMethod: 'cod',
      invoice: null,
      expectedTotal,
    },
  });
  expect(placed.status(), await placed.text()).toBe(201);
  return (await placed.json()).data.code as string;
}

// Kiện hàng hợp lệ để tạo vận đơn bằng API. GHN giả không kiểm cỡ kiện nên giá trị chỉ cần đúng dạng.
const shipmentParcel = { weightGrams: 500, lengthCm: 20, widthCm: 15, heightCm: 10 };

// Admin xác nhận đơn COD rồi tạo vận đơn bằng API. Đơn sang `shipping`, trả mã vận đơn của GHN giả.
export async function confirmAndShipOrderByApi(
  adminPage: Page,
  orderCode: string,
): Promise<string> {
  const headers = { Origin: e2eEnvironment.webOrigin };

  const confirmation = await adminPage.request.post(`/api/admin/orders/${orderCode}/confirmation`, {
    headers,
  });
  expect(confirmation.status(), await confirmation.text()).toBe(200);

  const shipment = await adminPage.request.post(`/api/admin/orders/${orderCode}/shipment`, {
    headers,
    data: { parcel: shipmentParcel },
  });
  expect(shipment.status(), await shipment.text()).toBe(200);

  // Đọc lại đơn để lấy mã vận đơn, và xác nhận đơn đã sang `shipping`.
  const order = await readAdminOrder(adminPage, orderCode);
  expect(order.status).toBe('shipping');
  return order.shipment!.trackingCode;
}

// Đưa vận đơn tới `delivered` như thật: GHN giả đổi trạng thái, rồi webhook GHN báo cho API.
export async function deliverShipmentByWebhook(adminPage: Page, trackingCode: string) {
  const statusResponse = await setFakeGhnStatus(adminPage, trackingCode, 'delivered');
  expect(statusResponse.status(), await statusResponse.text()).toBe(200);
  const webhookResponse = await sendGhnWebhook(adminPage, trackingCode);
  expect(webhookResponse.status(), await webhookResponse.text()).toBe(200);
}

export interface AdminOrderSummary {
  status: string;
  shipment: { trackingCode: string } | null;
}

// Đọc đơn bằng API admin, để spec kiểm trạng thái đơn mà không phụ thuộc giao diện.
export async function readAdminOrder(
  adminPage: Page,
  orderCode: string,
): Promise<AdminOrderSummary> {
  const detail = await adminPage.request.get(`/api/admin/orders/${orderCode}`);
  expect(detail.status(), await detail.text()).toBe(200);
  return (await detail.json()).data as AdminOrderSummary;
}
