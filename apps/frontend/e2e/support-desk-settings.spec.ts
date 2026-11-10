import path from 'node:path';
import { expect, test, type BrowserContext, type Locator, type Page } from '@playwright/test';
import { expectNoHorizontalOverflow } from './admin-order-fixtures';
import {
  collectConsoleIssues,
  dismissConsent,
  loginAdmin,
  type AllowedLoadFailure,
} from './catalog-admin-flow';
import { stubWebFonts } from './commerce-order-fixtures';
import { e2eEnvironment } from './environment';
import { createSupportDeskRouteMocks } from './support-desk-contract-routes';
import { findSupportDeskExample } from '../test/unit/features/support-desk/contract-fixtures';

// SD-UI5: màn cấu hình chăm sóc khách hàng, với route `settings` và `canned-responses` chưa có ở backend nên spec dùng helper SD-C2.
// Tài khoản admin có mọi quyền, nên trường hợp thiếu quyền do unit test kiểm.

const SETTINGS_FILE = 'admin-settings.json';
const CANNED_FILE = 'admin-canned-responses.json';

interface SettingsBody {
  opensAt: string;
  closesAt: string;
  holidays: string[];
  waitNudgeMinutes: number;
  autoCloseMinutes: number;
}

interface CannedBody {
  id: string;
  shortcut: string;
  body: string;
  shared: boolean;
  editable: boolean;
}

// Dữ liệu của máy chủ giả. Test đọc và ghi qua các route giả bên dưới, nên bước lưu có hiệu lực thật.
let settingsState: SettingsBody & { updatedAt: string };
let cannedState: CannedBody[];
let putBodies: unknown[];
let postBodies: unknown[];
let deleteIds: string[];
// Khi bật, lệnh `PUT` trả lỗi 400 của ví dụ mẫu dù dữ liệu gửi lên hợp lệ. Dùng để kiểm đường lỗi từ API.
let forcedSettingsError: string | null;
let nextCannedNumber: number;

function resetServerState() {
  settingsState = (
    findSupportDeskExample(SETTINGS_FILE, 'đọc cấu hình').response as { data: typeof settingsState }
  ).data;
  cannedState = (
    findSupportDeskExample(CANNED_FILE, 'danh sách mẫu').response as { data: CannedBody[] }
  ).data;
  putBodies = [];
  postBodies = [];
  deleteIds = [];
  forcedSettingsError = null;
  nextCannedNumber = 100;
}

function cannedIdFrom(url: string): string {
  return url.slice(url.lastIndexOf('/') + 1);
}

async function mockSupportSettingsRoutes(page: Page) {
  const routes = createSupportDeskRouteMocks({ page, apiOrigin: e2eEnvironment.apiOrigin });
  await routes.mockRoute(SETTINGS_FILE, 'đọc cấu hình', {
    respondWith: () => ({ body: { data: settingsState } }),
  });
  await routes.mockRoute(SETTINGS_FILE, 'lưu cấu hình', {
    respondWith: (request) => {
      putBodies.push(request.postDataJSON());
      if (forcedSettingsError !== null) {
        const errorExample = findSupportDeskExample(SETTINGS_FILE, forcedSettingsError);
        return { status: errorExample.status, body: errorExample.response };
      }
      settingsState = {
        ...(request.postDataJSON() as SettingsBody),
        updatedAt: '2026-11-03T03:00:00Z',
      };
      return { body: { data: settingsState } };
    },
  });
  await routes.mockRoute(CANNED_FILE, 'danh sách mẫu', {
    respondWith: () => ({ body: { data: cannedState } }),
  });
  await routes.mockRoute(CANNED_FILE, 'tạo mẫu cá nhân', {
    respondWith: (request) => {
      const body = request.postDataJSON() as Omit<CannedBody, 'id' | 'editable'>;
      postBodies.push(body);
      // Phím tắt `kiemdon` đã có trong dữ liệu mẫu. Trả đúng lỗi 409 của ví dụ "trùng phím tắt".
      if (
        cannedState.some(
          (entry) => entry.shortcut === body.shortcut && entry.shared === body.shared,
        )
      ) {
        const conflict = findSupportDeskExample(CANNED_FILE, 'trùng phím tắt');
        return { status: conflict.status, body: conflict.response };
      }
      const created: CannedBody = {
        id: `ca000000-0000-4000-8000-${String(nextCannedNumber++).padStart(12, '0')}`,
        shortcut: body.shortcut,
        body: body.body,
        shared: body.shared,
        editable: true,
      };
      cannedState = [...cannedState, created];
      return { status: 201, body: { data: created } };
    },
  });
  await routes.mockRoute(CANNED_FILE, 'sửa mẫu', {
    respondWith: (request) => {
      const id = cannedIdFrom(request.url());
      const changes = request.postDataJSON() as Partial<CannedBody>;
      cannedState = cannedState.map((entry) =>
        entry.id === id ? { ...entry, ...changes } : entry,
      );
      return { body: { data: cannedState.find((entry) => entry.id === id) } };
    },
  });
  // File mẫu chưa có ví dụ cho `DELETE`. Hợp đồng ghi 204 và không có thân.
  await routes.mockInlineRoute(
    {
      name: 'xóa mẫu',
      method: 'DELETE',
      path: '/api/admin/support/canned-responses/ca000000-0000-4000-8000-000000000003',
      request: null,
      status: 204,
      response: null,
    },
    {
      respondWith: (request) => {
        const id = cannedIdFrom(request.url());
        deleteIds.push(id);
        cannedState = cannedState.filter((entry) => entry.id !== id);
        return { status: 204, body: null };
      },
    },
  );
}

// Ảnh chụp vào thư mục ngoài repo khi có biến môi trường. Không có biến thì Playwright giữ ảnh trong thư mục kết quả.
function screenshotPath(testInfo: { outputPath: (name: string) => string }, fileName: string) {
  const directory = process.env.SUPPORT_DESK_SHOTS_DIR;
  return directory ? path.join(directory, fileName) : testInfo.outputPath(fileName);
}

// Khung bao của phần tử. Phần tử không có khung thì case đỏ ngay, thay vì so với 0 cho qua.
async function readBox(locator: Locator) {
  const box = await locator.boundingBox();
  expect(box).not.toBeNull();
  return box!;
}

function opensAtField(page: Page) {
  return page.getByLabel('Giờ mở cửa', { exact: true });
}

function closesAtField(page: Page) {
  return page.getByLabel('Giờ đóng cửa', { exact: true });
}

function waitNudgeField(page: Page) {
  return page.getByLabel('Nhắc lại phiên chờ sau (phút)', { exact: true });
}

function autoCloseField(page: Page) {
  return page.getByLabel('Tự đóng phiên im lặng sau (phút)', { exact: true });
}

function holidayDateField(page: Page) {
  return page.getByLabel('Thêm ngày nghỉ', { exact: true });
}

function saveButton(page: Page) {
  return page.getByRole('button', { name: /^(Lưu cấu hình|Đang lưu\.\.\.)$/ });
}

function saveStatus(page: Page) {
  return page.locator('.support-settings-status');
}

function cannedRow(page: Page, shortcut: string) {
  return page.locator('.canned-row').filter({ hasText: `/${shortcut}` });
}

// Câu lỗi của ô theo quy ước `<id ô>-error` của `FieldError`.
function errorOf(page: Page, inputId: string) {
  return page.locator(`#${inputId}-error`);
}

test.describe.configure({ mode: 'serial' });

test.describe('SD-UI5: màn cấu hình CSKH', () => {
  let context: BrowserContext;
  let page: Page;
  let consoleIssues: string[];
  // Lỗi tải mà một case chủ động gây ra. Case thêm vào, và `beforeEach` dọn đi, nên mọi case khác vẫn đòi console sạch.
  const allowedFailures: AllowedLoadFailure[] = [];

  test.beforeAll(async ({ browser }) => {
    test.setTimeout(120000);
    context = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    page = await context.newPage();
    consoleIssues = collectConsoleIssues(page, allowedFailures);
    // Chặn phông chữ ngoài để lỗi mạng ngoài không làm console có lỗi oan.
    await stubWebFonts(page);
    await loginAdmin(page);
    await dismissConsent(page);
    await mockSupportSettingsRoutes(page);
    consoleIssues.length = 0;
  });

  test.afterAll(async () => {
    await context?.close();
  });

  test.beforeEach(async () => {
    resetServerState();
    allowedFailures.length = 0;
    await page.setViewportSize({ width: 1280, height: 800 });
    consoleIssues.length = 0;
  });

  test.afterEach(() => {
    // Mỗi case phải kết thúc với 0 lỗi và 0 cảnh báo trong console.
    expect(consoleIssues, 'Console phải sạch').toEqual([]);
  });

  async function openSettingsPage() {
    await page.goto('/admin/support/settings');
    await expect(page.getByRole('heading', { level: 1, name: 'Cấu hình CSKH' })).toBeVisible();
    // Giá trị của máy chủ giả hiện ra nghĩa là cấu hình đã nạp xong.
    await expect(opensAtField(page)).toHaveValue('08:00');
  }

  test('admin thấy mục Cấu hình CSKH trong menu, mở trang và thấy dữ liệu mẫu', async () => {
    await page.goto('/admin');
    const menuLink = page.getByRole('link', { name: 'Cấu hình CSKH', exact: true });
    await expect(menuLink).toBeVisible();

    await menuLink.click();

    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/support/settings`);
    await expect(page.getByRole('heading', { level: 1, name: 'Cấu hình CSKH' })).toBeVisible();
    await expect(opensAtField(page)).toHaveValue('08:00');
    await expect(closesAtField(page)).toHaveValue('21:00');
    await expect(waitNudgeField(page)).toHaveValue('10');
    await expect(autoCloseField(page)).toHaveValue('30');
    await expect(page.locator('.holiday-date')).toHaveText(['06/02/2027', '07/02/2027']);
    await expect(saveStatus(page)).toHaveAttribute('role', 'status');
    await expect(saveStatus(page)).toHaveText('');
  });

  test('sửa giờ và mốc phút rồi lưu: PUT không có updatedAt, hiện thông báo, focus về nút lưu', async () => {
    await openSettingsPage();
    await opensAtField(page).fill('08:30');
    await waitNudgeField(page).fill('15');

    await saveButton(page).click();

    await expect(saveStatus(page)).toContainText('Đã lưu cấu hình.');
    await expect(saveStatus(page)).toContainText('Cập nhật lần cuối 10:00 03/11/2026');
    expect(putBodies).toEqual([
      {
        opensAt: '08:30',
        closesAt: '21:00',
        holidays: ['2027-02-06', '2027-02-07'],
        waitNudgeMinutes: 15,
        autoCloseMinutes: 30,
      },
    ]);
    expect(Object.keys(putBodies[0] as object)).not.toContain('updatedAt');
    await expect(saveButton(page)).toBeFocused();

    // Sửa ô tiếp theo thì thông báo đã lưu biến mất.
    await closesAtField(page).fill('20:00');
    await expect(saveStatus(page)).toHaveText('');
  });

  test('giờ mở cửa sau giờ đóng cửa và mốc nhắc lại không trước mốc tự đóng: lỗi hiện dưới đúng ô, không gửi request', async () => {
    await openSettingsPage();
    await opensAtField(page).fill('22:00');
    await waitNudgeField(page).fill('30');

    await saveButton(page).click();

    await expect(errorOf(page, 'support-settings-opens-at')).toHaveText(
      'Giờ mở cửa phải trước giờ đóng cửa.',
    );
    await expect(errorOf(page, 'support-settings-wait-nudge')).toHaveText(
      'Mốc nhắc lại phải nhỏ hơn mốc tự đóng.',
    );
    await expect(errorOf(page, 'support-settings-closes-at')).toHaveCount(0);
    await expect(errorOf(page, 'support-settings-auto-close')).toHaveCount(0);
    await expect(opensAtField(page)).toHaveAttribute('aria-invalid', 'true');
    // Ô lỗi đầu tiên nhận focus để bàn phím sửa ngay được.
    await expect(opensAtField(page)).toBeFocused();
    expect(putBodies).toEqual([]);

    // Sửa ô nào thì lỗi của ô đó biến mất, lỗi ô khác còn nguyên.
    await opensAtField(page).fill('07:00');
    await expect(errorOf(page, 'support-settings-opens-at')).toHaveCount(0);
    await expect(errorOf(page, 'support-settings-wait-nudge')).toHaveCount(1);
  });

  test('lỗi 400 của API theo ví dụ mẫu hiện dưới ô mốc nhắc lại, không dưới ô khác', async () => {
    // Trình duyệt ghi một dòng lỗi console cho mỗi phản hồi 400 mà case này chủ động gây ra.
    allowedFailures.push({ urlPattern: /\/api\/admin\/support\/settings$/, status: 400 });
    forcedSettingsError = 'mốc nhắc lại không trước mốc tự đóng';
    await openSettingsPage();

    await saveButton(page).click();

    await expect(errorOf(page, 'support-settings-wait-nudge')).toHaveText(
      'Mốc nhắc lại phải nhỏ hơn mốc tự đóng.',
    );
    await expect(waitNudgeField(page)).toBeFocused();
    await expect(page.locator('.form-error')).toHaveCount(1);
    expect(putBodies).toHaveLength(1);

    forcedSettingsError = 'giờ mở cửa sau giờ đóng cửa';
    await saveButton(page).click();
    await expect(errorOf(page, 'support-settings-opens-at')).toHaveText(
      'Giờ mở cửa phải trước giờ đóng cửa.',
    );
    await expect(page.locator('.form-error')).toHaveCount(1);
  });

  test('ngày nghỉ: chọn bằng ô ngày có nhãn, báo trùng, xóa từng ngày rồi lưu', async () => {
    await openSettingsPage();
    const dateField = holidayDateField(page);

    await dateField.fill('2027-01-01');
    await page.getByRole('button', { name: 'Thêm ngày nghỉ', exact: true }).click();
    await expect(page.locator('.holiday-date')).toHaveText([
      '01/01/2027',
      '06/02/2027',
      '07/02/2027',
    ]);

    await dateField.fill('2027-02-06');
    await page.getByRole('button', { name: 'Thêm ngày nghỉ', exact: true }).click();
    await expect(errorOf(page, 'support-settings-holiday-input')).toHaveText(
      'Ngày này đã có trong danh sách.',
    );
    await expect(page.locator('.holiday-date')).toHaveCount(3);

    await page.getByRole('button', { name: 'Xóa ngày nghỉ 06/02/2027' }).click();
    await expect(page.locator('.holiday-date')).toHaveText(['01/01/2027', '07/02/2027']);

    // Ô ngày còn giá trị `2027-02-06` chưa thêm: bấm lưu bị chặn, hiện lỗi dưới ô ngày, không gửi request.
    await saveButton(page).click();
    await expect(errorOf(page, 'support-settings-holiday-input')).toHaveText(
      'Bấm Thêm ngày nghỉ để thêm ngày này, hoặc xóa ô ngày.',
    );
    await expect(dateField).toBeFocused();
    expect(putBodies).toEqual([]);
    await dateField.fill('');

    await saveButton(page).click();
    await expect(saveStatus(page)).toContainText('Đã lưu cấu hình.');
    expect((putBodies[0] as SettingsBody).holidays).toEqual(['2027-01-01', '2027-02-07']);
  });

  test('mẫu dùng chung: mẫu editable false chỉ xem, mẫu sửa được có nút sửa và xóa', async () => {
    await openSettingsPage();

    const readOnlyRow = cannedRow(page, 'camon');
    await expect(readOnlyRow).toContainText('Chỉ xem');
    await expect(readOnlyRow.getByRole('button')).toHaveCount(0);
    // Mẫu cá nhân `kiemdon` không thuộc khối dùng chung.
    await expect(cannedRow(page, 'kiemdon')).toHaveCount(0);

    await page.getByRole('button', { name: 'Thêm mẫu', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Phím tắt').fill('/baohanh');
    // Dấu `/` người dùng gõ vào đầu ô tự bị bỏ.
    await expect(dialog.getByLabel('Phím tắt')).toHaveValue('baohanh');
    await dialog.getByLabel('Nội dung').fill('Sản phẩm được bảo hành 12 tháng.');
    await expect(dialog.getByText('32/2000')).toBeVisible();
    await dialog.getByRole('button', { name: 'Lưu mẫu' }).click();

    await expect(dialog).toHaveCount(0);
    const createdRow = cannedRow(page, 'baohanh');
    await expect(createdRow).toContainText('Sản phẩm được bảo hành 12 tháng.');
    await expect(page.locator('.canned-status')).toHaveText('Đã lưu mẫu /baohanh.');
    expect(postBodies).toEqual([
      { shortcut: 'baohanh', body: 'Sản phẩm được bảo hành 12 tháng.', shared: true },
    ]);
    await expect(createdRow.getByRole('button', { name: 'Sửa mẫu /baohanh' })).toBeVisible();
    await expect(createdRow.getByRole('button', { name: 'Xóa mẫu /baohanh' })).toBeVisible();
  });

  test('mẫu dùng chung: trùng phím tắt hiện lỗi dưới ô phím tắt và giữ dữ liệu đã nhập', async () => {
    // Trình duyệt ghi một dòng lỗi console cho phản hồi 409 mà case này chủ động gây ra.
    allowedFailures.push({ urlPattern: /\/api\/admin\/support\/canned-responses$/, status: 409 });
    cannedState = [
      ...cannedState,
      {
        id: 'ca000000-0000-4000-8000-000000000009',
        shortcut: 'giaohang',
        body: 'Đơn giao trong 3 ngày.',
        shared: true,
        editable: true,
      },
    ];
    await openSettingsPage();

    await page.getByRole('button', { name: 'Thêm mẫu', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Phím tắt').fill('giaohang');
    await dialog.getByLabel('Nội dung').fill('Nội dung giữ lại.');
    await dialog.getByRole('button', { name: 'Lưu mẫu' }).click();

    await expect(errorOf(page, 'canned-response-shortcut')).toHaveText(
      'Bạn đã có mẫu dùng phím tắt này.',
    );
    await expect(dialog.getByLabel('Phím tắt')).toBeFocused();
    await expect(dialog.getByLabel('Nội dung')).toHaveValue('Nội dung giữ lại.');
    await expect(dialog).toBeVisible();

    // Phím tắt chữ hoa bị chặn ngay ở client, không tốn request.
    const postsBefore = postBodies.length;
    await dialog.getByLabel('Phím tắt').fill('GiaoHang');
    await dialog.getByRole('button', { name: 'Lưu mẫu' }).click();
    await expect(errorOf(page, 'canned-response-shortcut')).toContainText('chữ thường không dấu');
    expect(postBodies).toHaveLength(postsBefore);

    await dialog.getByRole('button', { name: 'Hủy' }).click();
    await expect(dialog).toHaveCount(0);
  });

  test('mẫu dùng chung: sửa chỉ gửi trường đã đổi, xóa phải qua hộp thoại xác nhận', async () => {
    cannedState = [
      ...cannedState,
      {
        id: 'ca000000-0000-4000-8000-000000000009',
        shortcut: 'giaohang',
        body: 'Đơn giao trong 3 ngày.',
        shared: true,
        editable: true,
      },
    ];
    await openSettingsPage();

    await page.getByRole('button', { name: 'Sửa mẫu /giaohang' }).click();
    const editDialog = page.getByRole('dialog');
    await editDialog.getByLabel('Nội dung').fill('Đơn giao trong 2 ngày.');
    await editDialog.getByRole('button', { name: 'Lưu mẫu' }).click();
    await expect(editDialog).toHaveCount(0);
    await expect(cannedRow(page, 'giaohang')).toContainText('Đơn giao trong 2 ngày.');

    await page.getByRole('button', { name: 'Xóa mẫu /giaohang' }).click();
    const confirmDialog = page.getByRole('dialog');
    await expect(confirmDialog.getByRole('heading', { name: 'Xóa mẫu /giaohang?' })).toBeVisible();
    // Chưa bấm xác nhận thì chưa có lệnh xóa nào.
    expect(deleteIds).toEqual([]);

    await confirmDialog.getByRole('button', { name: 'Xóa mẫu', exact: true }).click();

    await expect(confirmDialog).toHaveCount(0);
    await expect(cannedRow(page, 'giaohang')).toHaveCount(0);
    expect(deleteIds).toEqual(['ca000000-0000-4000-8000-000000000009']);
    await expect(page.locator('.canned-status')).toHaveText('Đã xóa mẫu /giaohang.');
    // Xóa xong, focus về nút Thêm mẫu thay vì rơi về đầu trang.
    await expect(page.getByRole('button', { name: 'Thêm mẫu', exact: true })).toBeFocused();
  });

  test('bàn phím: Tab đi từ ô số sang nút lưu, hộp thoại giữ focus bên trong, Esc trả focus về nút đã mở', async () => {
    await openSettingsPage();

    await waitNudgeField(page).focus();
    await page.keyboard.press('Tab');
    await expect(autoCloseField(page)).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(saveButton(page)).toBeFocused();
    await page.keyboard.press('Shift+Tab');
    await expect(autoCloseField(page)).toBeFocused();

    const addButton = page.getByRole('button', { name: 'Thêm mẫu', exact: true });
    await addButton.focus();
    await page.keyboard.press('Enter');
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    // Focus vào ô đầu tiên của hộp thoại, và Tab nhiều lần không đưa focus ra ngoài hộp thoại.
    await expect(dialog.getByLabel('Phím tắt')).toBeFocused();
    for (let step = 0; step < 8; step++) {
      await page.keyboard.press('Tab');
      await expect(dialog.locator(':focus')).toHaveCount(1);
    }
    await page.keyboard.press('Escape');
    await expect(dialog).toHaveCount(0);
    await expect(addButton).toBeFocused();
  });

  for (const viewport of [
    { name: '1280', width: 1280, height: 800 },
    { name: '375', width: 375, height: 812 },
  ]) {
    test(`ảnh chụp ${viewport.name}px: trang cấu hình, lỗi dưới ô và hộp thoại thêm mẫu không tràn ngang`, async () => {
      const testInfo = test.info();
      await page.setViewportSize({ width: viewport.width, height: viewport.height });
      await openSettingsPage();
      await expect(cannedRow(page, 'camon')).toBeVisible();
      await expectNoHorizontalOverflow(page);

      // Số đo của trang đầy đủ: mọi thẻ, ô và nút nằm trong khung nhìn theo chiều ngang.
      const opensBox = await readBox(opensAtField(page));
      const closesBox = await readBox(closesAtField(page));
      const nudgeBox = await readBox(waitNudgeField(page));
      const autoBox = await readBox(autoCloseField(page));
      const dateBox = await readBox(holidayDateField(page));
      const saveBox = await readBox(saveButton(page));
      const cardBoxes = await page.locator('.support-settings-page .card').evaluateAll((cards) =>
        cards.map((card) => {
          const rect = card.getBoundingClientRect();
          return { x: rect.x, width: rect.width };
        }),
      );
      const layout = await page.evaluate(() => ({
        innerWidth: window.innerWidth,
        scrollWidth: document.documentElement.scrollWidth,
      }));
      await testInfo.attach(`measurements-page-${viewport.name}`, {
        body: JSON.stringify(
          { viewport, opensBox, closesBox, nudgeBox, autoBox, dateBox, saveBox, cardBoxes, layout },
          null,
          2,
        ),
        contentType: 'application/json',
      });
      expect(layout.scrollWidth).toBeLessThanOrEqual(layout.innerWidth);
      for (const box of [opensBox, closesBox, nudgeBox, autoBox, dateBox, saveBox]) {
        expect(box.x).toBeGreaterThanOrEqual(0);
        expect(box.x + box.width).toBeLessThanOrEqual(viewport.width);
      }
      for (const card of cardBoxes) {
        expect(card.x).toBeGreaterThanOrEqual(0);
        expect(card.x + card.width).toBeLessThanOrEqual(viewport.width);
      }
      if (viewport.width >= 1280) {
        // Màn rộng: hai ô giờ đứng cạnh nhau cùng hàng.
        expect(Math.abs(opensBox.y - closesBox.y)).toBeLessThan(2);
        expect(closesBox.x).toBeGreaterThan(opensBox.x + opensBox.width - 1);
        // Trang rộng tối đa 720px dù khung nhìn rộng hơn.
        expect(Math.max(...cardBoxes.map((card) => card.width))).toBeLessThanOrEqual(720);
      } else {
        // Màn hẹp: hai ô giờ xếp dọc, mỗi ô chiếm gần hết chiều rộng của thẻ.
        expect(closesBox.y).toBeGreaterThan(opensBox.y + opensBox.height - 1);
        expect(opensBox.width).toBeGreaterThan(viewport.width * 0.7);
        expect(saveBox.width).toBeGreaterThan(viewport.width * 0.7);
      }
      await page.screenshot({
        path: screenshotPath(testInfo, `settings-${viewport.name}.png`),
        fullPage: true,
      });

      // Thông báo "Đã lưu cấu hình." hiện ra không được đẩy khối mẫu bên dưới.
      const cannedTitle = page.locator('#support-shared-canned-title');
      // Đo theo tọa độ của trang. Lưu xong focus về nút lưu có thể cuộn trang, làm tọa độ theo khung nhìn đổi dù bố cục không đổi.
      const documentTop = (locator: Locator) =>
        locator.evaluate((element) => element.getBoundingClientRect().top + window.scrollY);
      const cannedTopBeforeSave = await documentTop(cannedTitle);
      await saveButton(page).click();
      await expect(saveStatus(page)).toContainText('Đã lưu cấu hình.');
      const cannedTopAfterSave = await documentTop(cannedTitle);
      const statusBox = await readBox(saveStatus(page));
      const saveBoxAfterSave = await readBox(saveButton(page));
      await testInfo.attach(`measurements-saved-${viewport.name}`, {
        body: JSON.stringify(
          { viewport, cannedTopBeforeSave, cannedTopAfterSave, statusBox, saveBoxAfterSave },
          null,
          2,
        ),
        contentType: 'application/json',
      });
      expect(cannedTopAfterSave).toBe(cannedTopBeforeSave);
      expect(statusBox.x + statusBox.width).toBeLessThanOrEqual(viewport.width);
      await expectNoHorizontalOverflow(page);
      await page.screenshot({
        path: screenshotPath(testInfo, `settings-saved-${viewport.name}.png`),
        fullPage: true,
      });

      // Trang với lỗi dưới ô.
      await opensAtField(page).fill('22:00');
      await waitNudgeField(page).fill('30');
      await saveButton(page).click();
      await expect(errorOf(page, 'support-settings-wait-nudge')).toBeVisible();
      await expectNoHorizontalOverflow(page);
      await page.screenshot({
        path: screenshotPath(testInfo, `settings-errors-${viewport.name}.png`),
        fullPage: true,
      });

      // Hộp thoại thêm mẫu, kèm lỗi dưới ô để chụp cả trạng thái lỗi.
      await page.getByRole('button', { name: 'Thêm mẫu', exact: true }).click();
      const dialog = page.getByRole('dialog');
      await dialog.getByRole('button', { name: 'Lưu mẫu' }).click();
      await expect(errorOf(page, 'canned-response-shortcut')).toBeVisible();
      await expectNoHorizontalOverflow(page);
      const dialogBox = await readBox(dialog);
      expect(dialogBox.x).toBeGreaterThanOrEqual(0);
      expect(dialogBox.x + dialogBox.width).toBeLessThanOrEqual(viewport.width);
      expect(dialogBox.y).toBeGreaterThanOrEqual(0);
      expect(dialogBox.y + dialogBox.height).toBeLessThanOrEqual(viewport.height);
      // Mở hộp thoại thì trang nền không cuộn được.
      const bodyOverflow = await page.evaluate(() => document.body.style.overflow);
      expect(bodyOverflow).toBe('hidden');
      await testInfo.attach(`measurements-dialog-${viewport.name}`, {
        body: JSON.stringify({ viewport, dialogBox, bodyOverflow }, null, 2),
        contentType: 'application/json',
      });
      await page.screenshot({ path: screenshotPath(testInfo, `canned-form-${viewport.name}.png`) });
      await page.keyboard.press('Escape');
      await expect(dialog).toHaveCount(0);
      await page.setViewportSize({ width: 1280, height: 800 });
    });
  }
});
