import { expect, test, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';
import { readPasswordSetupLink } from './outbox';

const SETTINGS_PATH = '/admin/shipping/settings';
const SETTINGS_API = '**/api/admin/shipping/settings';

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

// Chờ màn tải xong trước khi đo và chụp, để ảnh phản ánh nội dung thật.
async function openSettingsPage(page: Page) {
  await page.goto(SETTINGS_PATH);
  await expect(
    page.getByRole('heading', { level: 1, name: 'Cấu hình vận chuyển', exact: true }),
  ).toBeVisible();
  await expect(page.getByLabel('Tên người gửi', { exact: true })).toBeVisible();
  await expect(page.getByRole('status')).toHaveCount(0);
}

async function chooseByKeyboard(page: Page, inputId: string, typed: string, optionName: string) {
  const input = page.locator(`#${inputId}`);
  await expect(input).toBeEnabled();
  await input.fill(typed);
  // Gõ xong, dòng đầu của danh sách lọc đang được trỏ. Enter chọn dòng đó.
  await expect(page.getByRole('option', { name: optionName, exact: true })).toBeVisible();
  await page.keyboard.press('Enter');
  await expect(input).toHaveValue(optionName);
}

test('UI-S2: admin cấu hình vận chuyển, lỗi hiện đúng ô, nhân viên thiếu quyền bị chặn', async ({
  page,
}, testInfo) => {
  test.setTimeout(240000);

  // Ghi lỗi console và lỗi trang. Mọi bước trong spec phải có 0 lỗi và 0 cảnh báo ngoài dòng của request cố ý chặn.
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
  page.on('request', (request) => {
    if (new URL(request.url()).pathname === '/api/admin/shipping/settings') {
      settingsRequests.push(request.method());
    }
  });
  const countRequests = (method: string) =>
    settingsRequests.filter((value) => value === method).length;

  await test.step('Admin đăng nhập và mở màn qua menu Vận chuyển', async () => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await loginAdmin(page);
    await page
      .getByRole('navigation')
      .getByRole('link', { name: 'Vận chuyển', exact: true })
      .click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}${SETTINGS_PATH}`);
    await expect(
      page.getByRole('heading', { level: 1, name: 'Cấu hình vận chuyển', exact: true }),
    ).toBeVisible();
  });

  await test.step('Form trống: bấm Lưu thì lỗi hiện dưới đúng ô và không gửi PUT', async () => {
    for (const width of [1280, 375]) {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      await openSettingsPage(page);
      await expectNoHorizontalOverflow(page);
      await page.screenshot({
        path: testInfo.outputPath(`shipping-empty-${width}.png`),
        fullPage: true,
      });

      await page.getByRole('button', { name: 'Lưu', exact: true }).click();
      await expect(page.locator('#shipping-sender-name-error')).toHaveText(
        'Cần nhập tên người gửi.',
      );
      await expect(page.locator('#shipping-sender-phone-error')).toHaveText(
        'Cần nhập số điện thoại người gửi.',
      );
      await expect(page.locator('#shipping-address-province-error')).toHaveText(
        'Cần chọn tỉnh/thành phố.',
      );
      await expect(page.locator('#shipping-address-line-error')).toHaveText(
        'Cần nhập số nhà, tên đường.',
      );
      await expectNoHorizontalOverflow(page);
      await page.screenshot({
        path: testInfo.outputPath(`shipping-client-errors-${width}.png`),
        fullPage: true,
      });
    }
    expect(countRequests('PUT')).toBe(0);
  });

  await test.step('Điền đủ, chọn tỉnh/thành bằng bàn phím, lưu thành công', async () => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await openSettingsPage(page);
    await page.getByLabel('Tên người gửi', { exact: true }).fill('Kho Pickory E2E');
    // Số dạng +84 để kiểm backend chuẩn hóa về `0xxxxxxxxx`.
    await page.getByLabel('Số điện thoại người gửi', { exact: true }).fill('+84 912 345 678');
    await chooseByKeyboard(page, 'shipping-address-province', 'ha noi', 'Hà Nội');
    await chooseByKeyboard(page, 'shipping-address-ward', 'ba dinh', 'Phường Ba Đình');
    await page.getByLabel('Số nhà, tên đường', { exact: true }).fill('12 Phố Test');
    await page
      .getByLabel('Gói dịch vụ mặc định', { exact: true })
      .selectOption({ label: 'Hàng nặng' });

    await page.getByRole('button', { name: 'Lưu', exact: true }).click();

    await expect(page.getByText('Đã lưu cấu hình vận chuyển.', { exact: true })).toBeVisible();
    await expect(page.getByLabel('Số điện thoại người gửi', { exact: true })).toHaveValue(
      '0912345678',
    );
    expect(countRequests('PUT')).toBe(1);
    await expectNoHorizontalOverflow(page);
    await page.screenshot({ path: testInfo.outputPath('shipping-saved-1280.png'), fullPage: true });
  });

  await test.step('Tải lại trang: dữ liệu còn, số điện thoại đã chuẩn hóa', async () => {
    for (const width of [1280, 375]) {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      await openSettingsPage(page);
      await expect(page.getByLabel('Tên người gửi', { exact: true })).toHaveValue(
        'Kho Pickory E2E',
      );
      await expect(page.getByLabel('Số điện thoại người gửi', { exact: true })).toHaveValue(
        '0912345678',
      );
      await expect(page.locator('#shipping-address-province')).toHaveValue('Hà Nội');
      await expect(page.locator('#shipping-address-ward')).toHaveValue('Phường Ba Đình');
      await expect(page.getByLabel('Số nhà, tên đường', { exact: true })).toHaveValue(
        '12 Phố Test',
      );
      await expect(page.getByLabel('Gói dịch vụ mặc định', { exact: true })).toHaveValue('5');
      await expectNoHorizontalOverflow(page);
      await page.screenshot({
        path: testInfo.outputPath(`shipping-reloaded-${width}.png`),
        fullPage: true,
      });
    }
  });

  await test.step('Lỗi từ backend hiện dưới đúng ô phường/xã', async () => {
    // Lấy một phường của Hồ Chí Minh từ API công khai. Cấu hình đã lưu đang ở Hà Nội nên cặp này sai vùng.
    const provincesResponse = await page.request.get('/api/addresses/provinces');
    const provinces = (await provincesResponse.json()).data as { code: string; name: string }[];
    const otherProvince = provinces.find((province) => province.name === 'Hồ Chí Minh');
    expect(otherProvince).toBeDefined();
    const wardsResponse = await page.request.get(
      `/api/addresses/wards?provinceCode=${otherProvince?.code}`,
    );
    const otherProvinceWardCode = ((await wardsResponse.json()).data as { code: string }[])[0]
      ?.code;
    expect(otherProvinceWardCode).toBeTruthy();

    for (const width of [1280, 375]) {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      await openSettingsPage(page);
      // Đổi mã phường trong body của PUT thành mã của tỉnh/thành khác, nên lỗi 400 do backend thật trả.
      // Request GET vẫn đi thẳng tới backend.
      await page.route(SETTINGS_API, async (route) => {
        const request = route.request();
        if (request.method() !== 'PUT') {
          await route.continue();
          return;
        }
        await route.continue({
          postData: JSON.stringify({ ...request.postDataJSON(), wardCode: otherProvinceWardCode }),
        });
      });

      await page.getByRole('button', { name: 'Lưu', exact: true }).click();

      await expect(page.locator('#shipping-address-ward-error')).toHaveText(
        'Phường/xã không thuộc tỉnh/thành đã chọn.',
      );
      await expect(page.locator('#shipping-address-province-error')).toHaveCount(0);
      // Lỗi gắn được vào ô nên không có thông báo lỗi chung đầu form.
      // Cảnh báo thiếu `GHN_SHOP_ID` của môi trường E2E vẫn hiện, nên chỉ đếm khối lỗi.
      await expect(page.locator('[role="alert"]:not(.alert-warn)')).toHaveCount(0);
      await expectNoHorizontalOverflow(page);
      // Cuộn lên đầu để thanh menu cố định trên màn hẹp không đè lên tiêu đề trong ảnh chụp cả trang.
      await page.evaluate(() => window.scrollTo(0, 0));
      await page.screenshot({
        path: testInfo.outputPath(`shipping-server-error-${width}.png`),
        fullPage: true,
      });
      await page.unroute(SETTINGS_API);
    }
  });

  await test.step('Tạo nhân viên sales qua lời mời', async () => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/admin/staff');
    await page.getByRole('button', { name: '+ Mời nhân viên', exact: true }).click();
    const invitation = page.getByRole('dialog', { name: 'Mời nhân viên mới', exact: true });
    await invitation.getByLabel(/^Email nhân viên/).fill(e2eEnvironment.shippingSalesEmail);
    await invitation.getByLabel(/^Họ và tên/).fill('Sales Vận Chuyển E2E');
    await invitation.getByRole('checkbox', { name: /\(sales\)$/ }).check();
    await invitation.getByRole('button', { name: 'Gửi lời mời', exact: true }).click();
    await expect(invitation).toBeHidden();

    await page.getByRole('button', { name: 'Đăng xuất', exact: true }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/login`);

    await page.goto(await readPasswordSetupLink(e2eEnvironment.shippingSalesEmail));
    await page
      .getByLabel('Mật khẩu mới (tối thiểu 8 ký tự)', { exact: true })
      .fill(e2eEnvironment.accountPassword);
    await page
      .getByLabel('Xác nhận mật khẩu mới', { exact: true })
      .fill(e2eEnvironment.accountPassword);
    await page.getByRole('button', { name: 'Cập nhật mật khẩu', exact: true }).click();
    await page.getByRole('link', { name: 'Đăng nhập trang quản trị', exact: true }).click();
    await page.getByLabel(/^Email/).fill(e2eEnvironment.shippingSalesEmail);
    await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.accountPassword);
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin`);
  });

  await test.step('Sales không thấy mục Vận chuyển và mở thẳng URL thấy thông báo 403', async () => {
    const requestsBeforeVisit = countRequests('GET');
    await expect(
      page.getByRole('navigation').getByRole('link', { name: 'Vận chuyển', exact: true }),
    ).toHaveCount(0);

    for (const width of [1280, 375]) {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      await page.goto(SETTINGS_PATH);
      const notice = page.getByRole('alert');
      await expect(notice).toContainText('403');
      await expect(notice).toContainText('shipping-settings:manage');
      await expect(page.getByLabel('Tên người gửi', { exact: true })).toHaveCount(0);
      await expectNoHorizontalOverflow(page);
      await page.screenshot({
        path: testInfo.outputPath(`shipping-forbidden-${width}.png`),
        fullPage: true,
      });
    }
    // Thiếu quyền thì màn không được gọi API cấu hình.
    expect(countRequests('GET')).toBe(requestsBeforeVisit);
  });

  // Chromium ghi dòng lỗi cho phản hồi 400 của request cố ý chặn. Chỉ bỏ dòng này, giữ mọi lỗi khác.
  const unexpectedIssues = consoleIssues.filter((issue) => !issue.includes('status of 400'));
  expect(unexpectedIssues).toEqual([]);
});
