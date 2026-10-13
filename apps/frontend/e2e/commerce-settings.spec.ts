import { expect, test, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';
import { readPasswordSetupLink } from './outbox';

const SETTINGS_PATH = '/admin/commerce/settings';
const SETTINGS_API = '**/api/admin/commerce/settings';
const PAGE_TITLE = 'Cấu hình bán hàng';

interface CommerceSettings {
  freeShippingThreshold: number;
  codMaxAmount: number;
  maxQuantityPerSku: number;
  maxCartLines: number;
  sepayAccountNumber: string | null;
  sepayBankCode: string | null;
}

let previousCommerceSettings: CommerceSettings | undefined;

test.beforeEach(() => {
  previousCommerceSettings = undefined;
});

test.afterEach(async ({ browser }) => {
  if (!previousCommerceSettings) return;

  // Dùng phiên admin riêng vì test đã đổi sang sales để kiểm quyền trước khi kết thúc.
  const adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
  try {
    const adminPage = await adminContext.newPage();
    await loginAdmin(adminPage);
    // Hoàn nguyên cả sáu trường để lần chạy sau không thấy cấu hình thử nghiệm.
    const restored = await adminPage.request.put('/api/admin/commerce/settings', {
      headers: { Origin: e2eEnvironment.webOrigin },
      data: {
        freeShippingThreshold: previousCommerceSettings.freeShippingThreshold,
        codMaxAmount: previousCommerceSettings.codMaxAmount,
        maxQuantityPerSku: previousCommerceSettings.maxQuantityPerSku,
        maxCartLines: previousCommerceSettings.maxCartLines,
        sepayAccountNumber: previousCommerceSettings.sepayAccountNumber,
        sepayBankCode: previousCommerceSettings.sepayBankCode,
      },
    });
    expect(restored.status(), await restored.text()).toBe(200);
  } finally {
    await adminContext.close();
  }
});

// Thư mục ảnh chụp để người duyệt xem lại. Ngoài repo nên không lẫn vào Git.
const SCREENSHOT_DIRECTORY =
  'C:/Users/juven/AppData/Local/Temp/claude/C--Users-juven-Desktop-pickory/c0628d6e-6470-493e-8673-a94b5488add7/scratchpad/com-ui7-shots';

async function loginAdmin(page: Page) {
  await page.goto('/admin/login');
  const consent = page.getByRole('button', { name: 'Chỉ cần thiết', exact: true });
  if (await consent.isVisible()) await consent.click();
  await page.getByLabel(/^Email/).fill(e2eEnvironment.adminEmail);
  await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.adminPassword);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin`);
}

// Trang không được rộng hơn khung nhìn. Ảnh rộng hơn viewport nghĩa là người dùng phải cuộn ngang.
async function expectNoHorizontalOverflow(page: Page) {
  const widths = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth,
  }));
  expect(widths.content).toBeLessThanOrEqual(widths.viewport);
}

// Chờ form hiện xong trước khi đo và chụp, để ảnh phản ánh nội dung thật.
async function openSettingsPage(page: Page) {
  await page.goto(SETTINGS_PATH);
  await expect(
    page.getByRole('heading', { level: 1, name: PAGE_TITLE, exact: true }),
  ).toBeVisible();
  await expect(page.getByLabel('Số dòng tối đa mỗi giỏ', { exact: true })).toBeVisible();
}

async function capture(page: Page, name: string) {
  // Cuộn lên đầu để thanh menu cố định trên màn hẹp không đè lên tiêu đề trong ảnh chụp cả trang.
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: `${SCREENSHOT_DIRECTORY}/${name}.png`, fullPage: true });
}

test('COM-UI7: admin cấu hình bán hàng, lỗi hiện đúng ô, nhân viên thiếu quyền bị chặn', async ({
  page,
}) => {
  test.setTimeout(240000);

  // Mọi bước trong spec phải có 0 lỗi và 0 cảnh báo console, ngoài dòng của request cố ý gây lỗi 400.
  const consoleIssues: string[] = [];
  page.on('console', (message) => {
    // Chromium có giao diện tự gọi `favicon.ico`, còn ứng dụng chưa có favicon.
    if (message.location().url.endsWith('/favicon.ico')) return;
    if (message.type() === 'error' || message.type() === 'warning') {
      consoleIssues.push(`[${message.type()}] ${message.text()}`);
    }
  });
  page.on('pageerror', (error) => consoleIssues.push(`[pageerror] ${error.message}`));

  // Đếm request tới API cấu hình theo method, để khẳng định "không gửi PUT" và "không gọi GET khi thiếu quyền".
  const settingsRequests: string[] = [];
  const settingsRequestBodies: string[] = [];
  page.on('request', (request) => {
    if (new URL(request.url()).pathname === '/api/admin/commerce/settings') {
      settingsRequests.push(request.method());
      if (request.method() === 'PUT') settingsRequestBodies.push(request.postData() ?? '');
    }
  });
  const countRequests = (method: string) =>
    settingsRequests.filter((value) => value === method).length;

  await test.step('Admin đăng nhập và mở màn qua menu Cấu hình bán hàng', async () => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await loginAdmin(page);
    await page.getByRole('navigation').getByRole('link', { name: PAGE_TITLE, exact: true }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}${SETTINGS_PATH}`);
    await expect(
      page.getByRole('heading', { level: 1, name: PAGE_TITLE, exact: true }),
    ).toBeVisible();
  });

  // Lưu giá trị trước thao tác thử nghiệm, kể cả khi test thất bại ở bước sau.
  const previousSettingsResponse = await page.request.get('/api/admin/commerce/settings');
  expect(previousSettingsResponse.status(), await previousSettingsResponse.text()).toBe(200);
  previousCommerceSettings = (await previousSettingsResponse.json()).data as CommerceSettings;

  await test.step('Chưa lưu lần nào: form hiện giá trị mặc định và gợi ý định dạng VND', async () => {
    for (const width of [1280, 375]) {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      await openSettingsPage(page);
      await expect(page.getByLabel('Ngưỡng miễn phí giao hàng (đ)', { exact: true })).toHaveValue(
        '500000',
      );
      await expect(page.getByLabel('Hạn mức thanh toán COD (đ)', { exact: true })).toHaveValue(
        '20000000',
      );
      await expect(page.getByLabel('Số lượng tối đa mỗi sản phẩm', { exact: true })).toHaveValue(
        '5',
      );
      await expect(page.getByLabel('Số dòng tối đa mỗi giỏ', { exact: true })).toHaveValue('20');
      await expect(page.getByLabel('Số tài khoản', { exact: true })).toHaveValue('');
      await expect(page.getByLabel('Mã ngân hàng', { exact: true })).toHaveValue('');
      await expect(page.getByText('Đang nhập: 500.000 đ.')).toBeVisible();
      await expect(page.getByText('Đang nhập: 20.000.000 đ.')).toBeVisible();
      await expectNoHorizontalOverflow(page);
      await capture(page, `commerce-default-${width}`);
    }
    // Chỉ GET khi tải trang, chưa có PUT nào.
    expect(countRequests('PUT')).toBe(0);
  });

  await test.step('Nhập số âm: lỗi hiện dưới đúng ô và không gửi PUT', async () => {
    for (const width of [1280, 375]) {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      await openSettingsPage(page);
      await page.getByLabel('Ngưỡng miễn phí giao hàng (đ)', { exact: true }).fill('-1');
      await page.getByLabel('Số tài khoản', { exact: true }).fill('0123456789');

      await page.getByRole('button', { name: 'Lưu', exact: true }).click();

      await expect(page.locator('#commerce-free-shipping-threshold-error')).toHaveText(
        'Ngưỡng miễn phí giao hàng phải là số nguyên từ 0 đến 1.000.000.000 đ.',
      );
      // Số tài khoản có mà mã ngân hàng trống thì lỗi nằm ở ô mã ngân hàng.
      await expect(page.locator('#commerce-sepay-bank-code-error')).toHaveText(
        'Cần nhập cả số tài khoản và mã ngân hàng, hoặc để trống cả hai.',
      );
      await expect(page.locator('#commerce-cod-max-amount-error')).toHaveCount(0);
      await expect(page.locator('#commerce-sepay-account-number-error')).toHaveCount(0);
      await expectNoHorizontalOverflow(page);
      await capture(page, `commerce-client-errors-${width}`);
    }
    expect(countRequests('PUT')).toBe(0);
  });

  await test.step('Điền hợp lệ và lưu: hiện thông báo, mã ngân hàng đổi sang chữ hoa', async () => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await openSettingsPage(page);
    await page.getByLabel('Ngưỡng miễn phí giao hàng (đ)', { exact: true }).fill('700000');
    await page.getByLabel('Hạn mức thanh toán COD (đ)', { exact: true }).fill('15000000');
    await page.getByLabel('Số lượng tối đa mỗi sản phẩm', { exact: true }).fill('3');
    await page.getByLabel('Số dòng tối đa mỗi giỏ', { exact: true }).fill('10');
    await page.getByLabel('Số tài khoản', { exact: true }).fill('0123456789');
    await page.getByLabel('Mã ngân hàng', { exact: true }).fill('mb');

    await page.getByRole('button', { name: 'Lưu', exact: true }).click();

    await expect(page.getByRole('status')).toHaveText('Đã lưu cấu hình bán hàng.');
    await expect(page.getByLabel('Mã ngân hàng', { exact: true })).toHaveValue('MB');
    expect(countRequests('PUT')).toBe(1);
    // Body PUT chỉ có 6 trường của cấu hình. Không có khóa webhook SePay.
    const savedBody = settingsRequestBodies[0] ?? '';
    expect(JSON.parse(savedBody)).toEqual({
      freeShippingThreshold: 700000,
      codMaxAmount: 15000000,
      maxQuantityPerSku: 3,
      maxCartLines: 10,
      sepayAccountNumber: '0123456789',
      sepayBankCode: 'mb',
    });
    expect(savedBody.toLowerCase()).not.toContain('webhook');
    await expectNoHorizontalOverflow(page);
    await capture(page, 'commerce-saved-1280');
  });

  await test.step('Tải lại trang: giá trị mới còn nguyên', async () => {
    for (const width of [1280, 375]) {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      await openSettingsPage(page);
      await expect(page.getByLabel('Ngưỡng miễn phí giao hàng (đ)', { exact: true })).toHaveValue(
        '700000',
      );
      await expect(page.getByLabel('Hạn mức thanh toán COD (đ)', { exact: true })).toHaveValue(
        '15000000',
      );
      await expect(page.getByLabel('Số lượng tối đa mỗi sản phẩm', { exact: true })).toHaveValue(
        '3',
      );
      await expect(page.getByLabel('Số dòng tối đa mỗi giỏ', { exact: true })).toHaveValue('10');
      await expect(page.getByLabel('Số tài khoản', { exact: true })).toHaveValue('0123456789');
      await expect(page.getByLabel('Mã ngân hàng', { exact: true })).toHaveValue('MB');
      await expect(page.getByRole('status')).toHaveCount(0);
      await expectNoHorizontalOverflow(page);
      await capture(page, `commerce-reloaded-${width}`);
    }
  });

  await test.step('Lỗi 400 từ backend hiện dưới đúng ô', async () => {
    for (const width of [1280, 375]) {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      await openSettingsPage(page);
      // Đổi `codMaxAmount` trong body của PUT thành số âm, nên lỗi 400 do backend thật trả.
      // Request GET vẫn đi thẳng tới backend.
      await page.route(SETTINGS_API, async (route) => {
        const request = route.request();
        if (request.method() !== 'PUT') {
          await route.continue();
          return;
        }
        await route.continue({
          postData: JSON.stringify({ ...request.postDataJSON(), codMaxAmount: -5 }),
        });
      });

      await page.getByRole('button', { name: 'Lưu', exact: true }).click();

      await expect(page.locator('#commerce-cod-max-amount-error')).toHaveText(
        'Hạn mức COD phải là số nguyên từ 0 đến 1.000.000.000 đ.',
      );
      await expect(page.locator('#commerce-free-shipping-threshold-error')).toHaveCount(0);
      // Lỗi gắn được vào ô nên không có thông báo lỗi chung đầu form.
      await expect(page.locator('[role="alert"]')).toHaveCount(0);
      await expect(page.getByRole('status')).toHaveCount(0);
      await expectNoHorizontalOverflow(page);
      await capture(page, `commerce-server-error-${width}`);
      await page.unroute(SETTINGS_API);
    }
  });

  await test.step('Tạo nhân viên sales qua lời mời', async () => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/admin/staff');
    await page.getByRole('button', { name: '+ Mời nhân viên', exact: true }).click();
    const invitation = page.getByRole('dialog', { name: 'Mời nhân viên mới', exact: true });
    await invitation.getByLabel(/^Email nhân viên/).fill(e2eEnvironment.commerceSalesEmail);
    await invitation.getByLabel(/^Họ và tên/).fill('Sales Bán Hàng E2E');
    await invitation.getByRole('checkbox', { name: /\(sales\)$/ }).check();
    await invitation.getByRole('button', { name: 'Gửi lời mời', exact: true }).click();
    await expect(invitation).toBeHidden();

    await page.getByRole('button', { name: 'Đăng xuất', exact: true }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/login`);

    await page.goto(await readPasswordSetupLink(e2eEnvironment.commerceSalesEmail));
    await page
      .getByLabel('Mật khẩu mới (tối thiểu 8 ký tự)', { exact: true })
      .fill(e2eEnvironment.accountPassword);
    await page
      .getByLabel('Xác nhận mật khẩu mới', { exact: true })
      .fill(e2eEnvironment.accountPassword);
    await page.getByRole('button', { name: 'Cập nhật mật khẩu', exact: true }).click();
    await page.getByRole('link', { name: 'Đăng nhập trang quản trị', exact: true }).click();
    await page.getByLabel(/^Email/).fill(e2eEnvironment.commerceSalesEmail);
    await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.accountPassword);
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin`);
  });

  await test.step('Sales không thấy mục menu và mở thẳng URL thấy thông báo 403', async () => {
    const requestsBeforeVisit = countRequests('GET');
    await expect(
      page.getByRole('navigation').getByRole('link', { name: PAGE_TITLE, exact: true }),
    ).toHaveCount(0);

    for (const width of [1280, 375]) {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      await page.goto(SETTINGS_PATH);
      const notice = page.getByRole('alert');
      await expect(notice).toContainText('403');
      await expect(notice).toContainText('commerce-settings:manage');
      await expect(page.getByLabel('Số dòng tối đa mỗi giỏ', { exact: true })).toHaveCount(0);
      await expectNoHorizontalOverflow(page);
      await capture(page, `commerce-forbidden-${width}`);
    }
    // Thiếu quyền thì màn không được gọi API cấu hình.
    expect(countRequests('GET')).toBe(requestsBeforeVisit);
  });

  // Chromium ghi dòng lỗi cho phản hồi 400 của request cố ý gây lỗi. Chỉ bỏ dòng này, giữ mọi lỗi khác.
  const unexpectedIssues = consoleIssues.filter((issue) => !issue.includes('status of 400'));
  expect(unexpectedIssues).toEqual([]);
});
