import { mkdirSync } from 'node:fs';
import { expect, test, type Page } from '@playwright/test';
import { collectConsoleIssues, loginAdmin } from './catalog-admin-flow';
import { e2eEnvironment } from './environment';

const SETTINGS_PATH = '/admin/commerce/settings';
const SETTINGS_API_PATH = '/api/admin/commerce/settings';
const PAGE_TITLE = 'Cấu hình bán hàng';
// Lưu ảnh bằng chứng vào thư mục cố định của task ngoài mã nguồn, giống tiền lệ các spec trước.
const EVIDENCE_DIR = 'C:/Users/juven/Desktop/pickory/evidence/exports-printing/EXP-UI7';

interface CommerceSettingsSnapshot {
  freeShippingThreshold: number;
  codMaxAmount: number;
  maxQuantityPerSku: number;
  maxCartLines: number;
  sepayAccountNumber: string | null;
  sepayBankCode: string | null;
  shop: {
    name: string | null;
    address: string | null;
    phone: string | null;
    taxCode: string | null;
  };
}

let previousSettings: CommerceSettingsSnapshot | undefined;

// Lưu cấu hình hiện có trước mỗi ca để hoàn nguyên nguyên trạng sau khi chạy xong.
test.beforeEach(async ({ browser }) => {
  previousSettings = undefined;
  const adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
  try {
    const adminPage = await adminContext.newPage();
    await loginAdmin(adminPage);
    const response = await adminPage.request.get(SETTINGS_API_PATH);
    expect(response.status(), await response.text()).toBe(200);
    previousSettings = (await response.json()).data as CommerceSettingsSnapshot;
  } finally {
    await adminContext.close();
  }
});

// Gửi PUT giữ nguyên sáu trường cũ và chỉ thay khối shop theo trạng thái mong muốn.
async function saveShopState(
  adminPage: Page,
  base: CommerceSettingsSnapshot,
  shop: CommerceSettingsSnapshot['shop'],
) {
  const response = await adminPage.request.put(SETTINGS_API_PATH, {
    headers: { Origin: e2eEnvironment.webOrigin },
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

// Hoàn nguyên đủ bảy khóa gồm cả khối shop (kể cả khi cũ là null) để không làm bẩn ca sau.
test.afterEach(async ({ browser }) => {
  if (!previousSettings) return;
  const adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
  try {
    const adminPage = await adminContext.newPage();
    await loginAdmin(adminPage);
    await saveShopState(adminPage, previousSettings, previousSettings.shop);
  } finally {
    await adminContext.close();
  }
});

// Kiểm tra chiều rộng nội dung không vượt khung nhìn để tránh thanh cuộn ngang trên màn hẹp.
async function expectNoHorizontalOverflow(page: Page) {
  const widths = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth,
  }));
  expect(widths.content).toBeLessThanOrEqual(widths.viewport);
}

// Mở trang cấu hình và chờ nhóm thông tin shop hiển thị xong trước khi thao tác.
async function openSettingsPage(page: Page) {
  await page.goto(SETTINGS_PATH);
  await expect(
    page.getByRole('heading', { level: 1, name: PAGE_TITLE, exact: true }),
  ).toBeVisible();
  await expect(page.getByLabel('Tên shop', { exact: true })).toBeVisible();
}

// Cuộn lên đầu trang trước khi chụp toàn trang để thanh menu cố định không đè lên nội dung.
async function captureEvidence(page: Page, fileName: string) {
  mkdirSync(EVIDENCE_DIR, { recursive: true });
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: `${EVIDENCE_DIR}/${fileName}`, fullPage: true });
}

test('lưu thông tin shop qua backend thật, tải lại còn nguyên và xóa trống lưu thành null', async ({
  page,
}) => {
  const consoleIssues = collectConsoleIssues(page);
  await page.setViewportSize({ width: 1280, height: 800 });
  await loginAdmin(page);
  // Đưa shop về rỗng trước khi mở trang, để ca không phụ thuộc dữ liệu sót lại từ spec khác.
  await saveShopState(page, previousSettings!, {
    name: null,
    address: null,
    phone: null,
    taxCode: null,
  });
  await openSettingsPage(page);

  await expect(page.getByLabel('Tên shop', { exact: true })).toHaveValue('');
  await expect(page.getByLabel('Địa chỉ', { exact: true })).toHaveValue('');
  await expect(page.getByLabel('Số điện thoại', { exact: true })).toHaveValue('');
  await expect(page.getByLabel('Mã số thuế', { exact: true })).toHaveValue('');
  await captureEvidence(page, '01-form-shop-trong-1280.png');

  await page.getByLabel('Tên shop', { exact: true }).fill('  Pickory Flagship  ');
  await page.getByLabel('Địa chỉ', { exact: true }).fill('123 Lê Lợi, Quận 1, TP.HCM');
  await page.getByLabel('Số điện thoại', { exact: true }).fill('0901 234 567');
  await page.getByLabel('Mã số thuế', { exact: true }).fill('0312345678');

  const saveResponsePromise = page.waitForResponse(
    (response) =>
      new URL(response.url()).pathname === SETTINGS_API_PATH &&
      response.request().method() === 'PUT',
  );
  await page.getByRole('button', { name: 'Lưu', exact: true }).click();
  const saveResponse = await saveResponsePromise;
  expect(saveResponse.status()).toBe(200);

  const sentBody = saveResponse.request().postDataJSON() as CommerceSettingsSnapshot;
  expect(sentBody.shop).toEqual({
    name: 'Pickory Flagship',
    address: '123 Lê Lợi, Quận 1, TP.HCM',
    phone: '0901 234 567',
    taxCode: '0312345678',
  });
  await expect(page.getByRole('status')).toHaveText('Đã lưu cấu hình bán hàng.');

  await page.reload();
  await expect(page.getByLabel('Tên shop', { exact: true })).toHaveValue('Pickory Flagship');
  await expect(page.getByLabel('Địa chỉ', { exact: true })).toHaveValue(
    '123 Lê Lợi, Quận 1, TP.HCM',
  );
  await expect(page.getByLabel('Số điện thoại', { exact: true })).toHaveValue('0901 234 567');
  await expect(page.getByLabel('Mã số thuế', { exact: true })).toHaveValue('0312345678');
  await captureEvidence(page, '02-form-shop-da-luu-1280.png');

  // Xóa hết bốn ô để kiểm tra backend lưu chuỗi rỗng thành null và tải lại vẫn trống.
  await page.getByLabel('Tên shop', { exact: true }).fill('');
  await page.getByLabel('Địa chỉ', { exact: true }).fill('');
  await page.getByLabel('Số điện thoại', { exact: true }).fill('');
  await page.getByLabel('Mã số thuế', { exact: true }).fill('');

  const clearResponsePromise = page.waitForResponse(
    (response) =>
      new URL(response.url()).pathname === SETTINGS_API_PATH &&
      response.request().method() === 'PUT',
  );
  await page.getByRole('button', { name: 'Lưu', exact: true }).click();
  const clearResponse = await clearResponsePromise;
  expect(clearResponse.status()).toBe(200);

  const clearedBody = clearResponse.request().postDataJSON() as CommerceSettingsSnapshot;
  expect(clearedBody.shop).toEqual({
    name: null,
    address: null,
    phone: null,
    taxCode: null,
  });
  await expect(page.getByRole('status')).toHaveText('Đã lưu cấu hình bán hàng.');

  await page.reload();
  await expect(page.getByLabel('Tên shop', { exact: true })).toHaveValue('');
  await expect(page.getByLabel('Địa chỉ', { exact: true })).toHaveValue('');
  await expect(page.getByLabel('Số điện thoại', { exact: true })).toHaveValue('');
  await expect(page.getByLabel('Mã số thuế', { exact: true })).toHaveValue('');
  expect(consoleIssues).toEqual([]);
});

test('lỗi 400 thật từ backend cho số điện thoại và mã số thuế hiện dưới đúng ô', async ({
  page,
}) => {
  const consoleIssues = collectConsoleIssues(page, [
    { urlPattern: /\/api\/admin\/commerce\/settings$/, status: 400 },
  ]);
  let badRequestCount = 0;
  page.on('response', (response) => {
    if (new URL(response.url()).pathname === SETTINGS_API_PATH && response.status() === 400) {
      badRequestCount += 1;
    }
  });

  await page.setViewportSize({ width: 1280, height: 800 });
  await loginAdmin(page);
  await openSettingsPage(page);

  await page.getByLabel('Tên shop', { exact: true }).fill('Pickory Test');
  await page.getByLabel('Địa chỉ', { exact: true }).fill('Hà Nội');
  await page.getByLabel('Số điện thoại', { exact: true }).fill('abc123');
  await page.getByLabel('Mã số thuế', { exact: true }).fill('123');

  const errorResponsePromise = page.waitForResponse(
    (response) =>
      new URL(response.url()).pathname === SETTINGS_API_PATH &&
      response.request().method() === 'PUT',
  );
  await page.getByRole('button', { name: 'Lưu', exact: true }).click();
  const errorResponse = await errorResponsePromise;
  expect(errorResponse.status()).toBe(400);
  expect(badRequestCount).toBe(1);

  await expect(page.locator('#commerce-shop-phone-error')).toHaveText(
    'Số điện thoại shop dài tối đa 20 ký tự và chỉ gồm chữ số, dấu cách, +, ., -.',
  );
  await expect(page.locator('#commerce-shop-tax-code-error')).toHaveText(
    'Mã số thuế shop phải gồm 10 hoặc 13 chữ số.',
  );
  await expect(page.locator('#commerce-shop-name-error')).toHaveCount(0);
  await expect(page.locator('#commerce-shop-address-error')).toHaveCount(0);
  await expect(page.locator('[role="alert"]')).toHaveCount(0);
  await expect(page.getByRole('status')).toHaveCount(0);

  await expect(page.getByLabel('Số điện thoại', { exact: true })).toHaveAttribute(
    'aria-invalid',
    'true',
  );
  await expect(page.getByLabel('Số điện thoại', { exact: true })).toHaveAttribute(
    'aria-describedby',
    'commerce-shop-phone-error',
  );
  await expect(page.getByLabel('Mã số thuế', { exact: true })).toHaveAttribute(
    'aria-invalid',
    'true',
  );
  await expect(page.getByLabel('Mã số thuế', { exact: true })).toHaveAttribute(
    'aria-describedby',
    'commerce-shop-tax-code-error',
  );

  // Dữ liệu vừa nhập phải giữ nguyên để người dùng sửa lại mà không mất công gõ lại.
  await expect(page.getByLabel('Tên shop', { exact: true })).toHaveValue('Pickory Test');
  await expect(page.getByLabel('Địa chỉ', { exact: true })).toHaveValue('Hà Nội');
  await expect(page.getByLabel('Số điện thoại', { exact: true })).toHaveValue('abc123');
  await expect(page.getByLabel('Mã số thuế', { exact: true })).toHaveValue('123');

  await captureEvidence(page, '03-form-shop-loi-400-1280.png');
  expect(consoleIssues).toEqual([]);
});

test('maxLength ở trình duyệt giữ tối đa 120 ký tự khi gõ 130 ký tự vào Tên shop', async ({
  page,
}) => {
  const consoleIssues = collectConsoleIssues(page);
  await page.setViewportSize({ width: 1280, height: 800 });
  await loginAdmin(page);
  // Đưa shop về rỗng trước khi mở trang, để ca không phụ thuộc dữ liệu sót lại từ spec khác.
  await saveShopState(page, previousSettings!, {
    name: null,
    address: null,
    phone: null,
    taxCode: null,
  });
  await openSettingsPage(page);

  const nameInput = page.getByLabel('Tên shop', { exact: true });
  await nameInput.fill('');
  await nameInput.pressSequentially('a'.repeat(130));
  await expect(nameInput).toHaveValue('a'.repeat(120));
  expect(consoleIssues).toEqual([]);
});

test('ở 375 px nhóm thông tin shop không tràn ngang và các ô dùng được', async ({ page }) => {
  const consoleIssues = collectConsoleIssues(page);
  await page.setViewportSize({ width: 375, height: 812 });
  await loginAdmin(page);
  await openSettingsPage(page);

  await expectNoHorizontalOverflow(page);
  await expect(
    page.getByRole('heading', { level: 2, name: 'Thông tin in trên hóa đơn', exact: true }),
  ).toBeVisible();

  await page.getByLabel('Tên shop', { exact: true }).fill('Shop Di Động');
  await page.getByLabel('Địa chỉ', { exact: true }).fill('45 Hai Bà Trưng');
  await page.getByLabel('Số điện thoại', { exact: true }).fill('0912345678');
  await page.getByLabel('Mã số thuế', { exact: true }).fill('0101234567');

  const saveButton = page.getByRole('button', { name: 'Lưu', exact: true });
  await expect(saveButton).toBeVisible();
  await expect(saveButton).toBeEnabled();
  await saveButton.click();
  await expect(page.getByRole('status')).toHaveText('Đã lưu cấu hình bán hàng.');

  await expectNoHorizontalOverflow(page);
  await captureEvidence(page, '04-form-shop-375.png');
  expect(consoleIssues).toEqual([]);
});
