import path from 'node:path';
import {
  expect,
  test,
  type BrowserContext,
  type Locator,
  type Page,
  type Route,
} from '@playwright/test';
import { expectNoHorizontalOverflow } from './admin-order-fixtures';
import {
  collectConsoleIssues,
  dismissConsent,
  loginAdmin,
  type AllowedLoadFailure,
} from './catalog-admin-flow';
import { stubWebFonts } from './commerce-order-fixtures';
import { e2eEnvironment } from './environment';
import { activateStaffAccount } from './staff-fixtures';
import {
  createCannedResponseByApi,
  deleteCannedResponseByApi,
  deleteCannedResponsesWithSuffix,
  makeRunSuffix,
  SUPPORT_API_HEADERS,
} from './support-desk-canned-api';

// SD-UI5: màn cấu hình chăm sóc khách hàng chạy với route `settings` và `canned-responses` thật của backend (SD-10).
// Admin có mọi quyền, còn nhân viên chỉ có `support-settings:manage` có describe riêng ở cuối file.

const SETTINGS_PATH = '/api/admin/support/settings';
const CANNED_PATH = '/api/admin/support/canned-responses';
const SETTINGS_URL_PATTERN = /\/api\/admin\/support\/settings$/;

interface SettingsBody {
  opensAt: string;
  closesAt: string;
  holidays: string[];
  waitNudgeMinutes: number;
  autoCloseMinutes: number;
}

// Cấu hình là một bản ghi dùng chung cho cả DB E2E. Mỗi case bắt đầu từ giá trị này, nên case xanh khi chạy riêng và khi chạy cả spec.
const BASELINE_SETTINGS: SettingsBody = {
  opensAt: '08:00',
  closesAt: '21:00',
  holidays: ['2027-02-06', '2027-02-07'],
  waitNudgeMinutes: 10,
  autoCloseMinutes: 30,
};

// Hậu tố riêng của lần chạy. Mọi phím tắt spec tạo đều chứa hậu tố này, nên lần chạy sau không gặp lỗi 409 và `afterAll` dọn đúng mẫu của spec.
const runSuffix = makeRunSuffix();
// Email của nhân viên chỉ có `support-settings:manage`. Hậu tố của lần chạy giúp lời mời không đụng lỗi trùng email khi `beforeAll` chạy lại.
const managerEmail = `e2e-sd-settings-manage-${runSuffix}@pickory.local`;

// Các request ghi mà trang gửi đi. Spec ghi lại từ sự kiện `request` của trang, nên lời gọi API dựng dữ liệu không lẫn vào.
let putBodies: unknown[];
let postBodies: unknown[];
let patchBodies: unknown[];
let deleteIds: string[];

// Đổi thời điểm ISO sang chuỗi `HH:mm dd/MM/yyyy` theo giờ Việt Nam, đúng dạng màn cấu hình hiện.
function formatVietnamTimestamp(isoTimestamp: string): string {
  const parts = new Intl.DateTimeFormat('en-GB', {
    timeZone: 'Asia/Ho_Chi_Minh',
    hourCycle: 'h23',
    hour: '2-digit',
    minute: '2-digit',
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  }).formatToParts(new Date(isoTimestamp));
  const part = (type: string) => parts.find((entry) => entry.type === type)?.value ?? '';
  return `${part('hour')}:${part('minute')} ${part('day')}/${part('month')}/${part('year')}`;
}

function recordPageWrites(page: Page) {
  page.on('request', (request) => {
    const { pathname } = new URL(request.url());
    if (pathname === SETTINGS_PATH && request.method() === 'PUT') {
      putBodies.push(request.postDataJSON());
    }
    if (pathname === CANNED_PATH && request.method() === 'POST') {
      postBodies.push(request.postDataJSON());
    }
    if (pathname.startsWith(`${CANNED_PATH}/`) && request.method() === 'PATCH') {
      patchBodies.push(request.postDataJSON());
    }
    if (pathname.startsWith(`${CANNED_PATH}/`) && request.method() === 'DELETE') {
      deleteIds.push(pathname.slice(CANNED_PATH.length + 1));
    }
  });
}

async function readSettingsByApi(page: Page): Promise<SettingsBody> {
  const response = await page.request.get(SETTINGS_PATH);
  expect(response.status(), await response.text()).toBe(200);
  const body = (await response.json()) as { data: SettingsBody & { updatedAt: string } };
  // Bỏ `updatedAt` vì `PUT` không nhận trường này.
  const { opensAt, closesAt, holidays, waitNudgeMinutes, autoCloseMinutes } = body.data;
  return { opensAt, closesAt, holidays, waitNudgeMinutes, autoCloseMinutes };
}

async function saveSettingsByApi(page: Page, settings: SettingsBody): Promise<void> {
  const response = await page.request.put(SETTINGS_PATH, {
    headers: SUPPORT_API_HEADERS,
    data: settings,
  });
  expect(response.status(), await response.text()).toBe(200);
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

  // Cấu hình ban đầu của DB E2E. `afterAll` trả lại để spec khác không thấy giá trị của spec này.
  let originalSettings: SettingsBody | null = null;

  test.beforeAll(async ({ browser }) => {
    test.setTimeout(120000);
    context = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    page = await context.newPage();
    consoleIssues = collectConsoleIssues(page, allowedFailures);
    // Chặn phông chữ ngoài để lỗi mạng ngoài không làm console có lỗi oan.
    await stubWebFonts(page);
    await loginAdmin(page);
    await dismissConsent(page);
    recordPageWrites(page);
    originalSettings = await readSettingsByApi(page);
    consoleIssues.length = 0;
  });

  test.afterAll(async () => {
    try {
      // Chỉ dọn khi `beforeAll` đã chạy xong, vì lúc đó `page` mới có phiên đăng nhập admin.
      if (originalSettings !== null) {
        await saveSettingsByApi(page, originalSettings);
        await deleteCannedResponsesWithSuffix(page, runSuffix);
      }
    } finally {
      // Đóng context cả khi dọn dữ liệu lỗi, để trình duyệt không bị treo.
      await context?.close();
    }
  });

  test.beforeEach(async () => {
    putBodies = [];
    postBodies = [];
    patchBodies = [];
    deleteIds = [];
    allowedFailures.length = 0;
    // Mỗi case tự đặt cấu hình nền trước khi kiểm, vì cấu hình là một bản ghi dùng chung.
    await saveSettingsByApi(page, BASELINE_SETTINGS);
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
    // Giá trị nền đã đặt bằng API hiện ra nghĩa là cấu hình đã nạp xong.
    await expect(opensAtField(page)).toHaveValue(BASELINE_SETTINGS.opensAt);
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

    // Bắt phản hồi của `PUT` để lấy `updatedAt` thật của backend.
    const saveResponse = page.waitForResponse(
      (response) =>
        new URL(response.url()).pathname === SETTINGS_PATH && response.request().method() === 'PUT',
    );
    await saveButton(page).click();
    const savedBody: { data: { updatedAt: string } } = await (await saveResponse).json();

    await expect(saveStatus(page)).toContainText('Đã lưu cấu hình.');
    // Giao diện hiện giờ Việt Nam, nên chuỗi mong đợi đổi `updatedAt` sang múi giờ này.
    await expect(saveStatus(page)).toContainText(
      `Cập nhật lần cuối ${formatVietnamTimestamp(savedBody.data.updatedAt)}`,
    );
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

  test('lỗi 400 của backend hiện dưới ô mốc nhắc lại, không dưới ô khác', async () => {
    // Trình duyệt ghi một dòng lỗi console cho mỗi phản hồi 400 mà case này chủ động gây ra.
    allowedFailures.push({ urlPattern: SETTINGS_URL_PATTERN, status: 400 });
    await openSettingsPage();

    // Client chặn dữ liệu sai trước khi gửi. Route chỉ sửa body của request rồi chuyển tới backend thật, nên phản hồi 400 vẫn do backend dựng.
    let bodyOverride: Partial<SettingsBody> = { waitNudgeMinutes: 30 };
    async function forwardWithOverride(route: Route) {
      const request = route.request();
      if (request.method() !== 'PUT') {
        await route.fallback();
        return;
      }
      const forwardedBody = { ...(request.postDataJSON() as SettingsBody), ...bodyOverride };
      await route.continue({ postData: JSON.stringify(forwardedBody) });
    }
    await page.route(SETTINGS_URL_PATTERN, forwardWithOverride);

    try {
      await saveButton(page).click();

      await expect(errorOf(page, 'support-settings-wait-nudge')).toHaveText(
        'Mốc nhắc lại phải nhỏ hơn mốc tự đóng.',
      );
      await expect(waitNudgeField(page)).toBeFocused();
      await expect(page.locator('.form-error')).toHaveCount(1);
      expect(putBodies).toHaveLength(1);

      bodyOverride = { opensAt: '22:00' };
      await saveButton(page).click();
      await expect(errorOf(page, 'support-settings-opens-at')).toHaveText(
        'Giờ mở cửa phải trước giờ đóng cửa.',
      );
      await expect(page.locator('.form-error')).toHaveCount(1);
    } finally {
      await page.unroute(SETTINGS_URL_PATTERN, forwardWithOverride);
    }
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

  test('mẫu dùng chung: chỉ hiện mẫu dùng chung, mẫu sửa được có nút sửa và xóa', async () => {
    const sharedShortcut = `camon-${runSuffix}`;
    const personalShortcut = `kiemdon-${runSuffix}`;
    const createdShortcut = `baohanh-${runSuffix}`;
    await createCannedResponseByApi(page, {
      shortcut: sharedShortcut,
      body: 'Cảm ơn bạn đã liên hệ Pickory.',
      shared: true,
    });
    await createCannedResponseByApi(page, {
      shortcut: personalShortcut,
      body: 'Mình kiểm đơn giúp bạn nhé.',
      shared: false,
    });
    await openSettingsPage();

    // Admin có `support-settings:manage`, nên mẫu dùng chung sửa được: có hai nút và không có nhãn Chỉ xem.
    const sharedRow = cannedRow(page, sharedShortcut);
    await expect(sharedRow).toContainText('Cảm ơn bạn đã liên hệ Pickory.');
    await expect(sharedRow).not.toContainText('Chỉ xem');
    await expect(
      sharedRow.getByRole('button', { name: `Sửa mẫu /${sharedShortcut}` }),
    ).toBeVisible();
    await expect(
      sharedRow.getByRole('button', { name: `Xóa mẫu /${sharedShortcut}` }),
    ).toBeVisible();
    // Mẫu cá nhân của chính admin không thuộc khối dùng chung.
    await expect(cannedRow(page, personalShortcut)).toHaveCount(0);

    await page.getByRole('button', { name: 'Thêm mẫu', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Phím tắt').fill(`/${createdShortcut}`);
    // Dấu `/` người dùng gõ vào đầu ô tự bị bỏ.
    await expect(dialog.getByLabel('Phím tắt')).toHaveValue(createdShortcut);
    await dialog.getByLabel('Nội dung').fill('Sản phẩm được bảo hành 12 tháng.');
    await expect(dialog.getByText('32/2000')).toBeVisible();
    await dialog.getByRole('button', { name: 'Lưu mẫu' }).click();

    await expect(dialog).toHaveCount(0);
    const createdRow = cannedRow(page, createdShortcut);
    await expect(createdRow).toContainText('Sản phẩm được bảo hành 12 tháng.');
    await expect(page.locator('.canned-status')).toHaveText(`Đã lưu mẫu /${createdShortcut}.`);
    expect(postBodies).toEqual([
      { shortcut: createdShortcut, body: 'Sản phẩm được bảo hành 12 tháng.', shared: true },
    ]);
    await expect(
      createdRow.getByRole('button', { name: `Sửa mẫu /${createdShortcut}` }),
    ).toBeVisible();
    await expect(
      createdRow.getByRole('button', { name: `Xóa mẫu /${createdShortcut}` }),
    ).toBeVisible();
  });

  test('mẫu dùng chung: trùng phím tắt hiện lỗi dưới ô phím tắt và giữ dữ liệu đã nhập', async () => {
    // Trình duyệt ghi một dòng lỗi console cho phản hồi 409 mà case này chủ động gây ra.
    allowedFailures.push({ urlPattern: /\/api\/admin\/support\/canned-responses$/, status: 409 });
    const takenShortcut = `giaohang-${runSuffix}`;
    await createCannedResponseByApi(page, {
      shortcut: takenShortcut,
      body: 'Đơn giao trong 3 ngày.',
      shared: true,
    });
    await openSettingsPage();

    await page.getByRole('button', { name: 'Thêm mẫu', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Phím tắt').fill(takenShortcut);
    await dialog.getByLabel('Nội dung').fill('Nội dung giữ lại.');
    await dialog.getByRole('button', { name: 'Lưu mẫu' }).click();

    // Backend trả câu riêng cho mẫu dùng chung. Frontend hiện nguyên câu đó.
    await expect(errorOf(page, 'canned-response-shortcut')).toHaveText(
      'Đã có mẫu dùng chung dùng phím tắt này.',
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
    const editedShortcut = `suaxoa-${runSuffix}`;
    const created = await createCannedResponseByApi(page, {
      shortcut: editedShortcut,
      body: 'Đơn giao trong 3 ngày.',
      shared: true,
    });
    await openSettingsPage();

    await page.getByRole('button', { name: `Sửa mẫu /${editedShortcut}` }).click();
    const editDialog = page.getByRole('dialog');
    await editDialog.getByLabel('Nội dung').fill('Đơn giao trong 2 ngày.');
    await editDialog.getByRole('button', { name: 'Lưu mẫu' }).click();
    await expect(editDialog).toHaveCount(0);
    await expect(cannedRow(page, editedShortcut)).toContainText('Đơn giao trong 2 ngày.');
    expect(patchBodies).toEqual([{ body: 'Đơn giao trong 2 ngày.' }]);

    await page.getByRole('button', { name: `Xóa mẫu /${editedShortcut}` }).click();
    const confirmDialog = page.getByRole('dialog');
    await expect(
      confirmDialog.getByRole('heading', { name: `Xóa mẫu /${editedShortcut}?` }),
    ).toBeVisible();
    // Chưa bấm xác nhận thì chưa có lệnh xóa nào.
    expect(deleteIds).toEqual([]);

    await confirmDialog.getByRole('button', { name: 'Xóa mẫu', exact: true }).click();

    await expect(confirmDialog).toHaveCount(0);
    await expect(cannedRow(page, editedShortcut)).toHaveCount(0);
    expect(deleteIds).toEqual([created.id]);
    await expect(page.locator('.canned-status')).toHaveText(`Đã xóa mẫu /${editedShortcut}.`);
    // Xóa xong, focus về nút Thêm mẫu thay vì rơi về đầu trang.
    await expect(page.getByRole('button', { name: 'Thêm mẫu', exact: true })).toBeFocused();
  });

  test('mẫu dùng chung: sửa mẫu đã bị xóa ở nơi khác hiện lỗi 404 của backend và bỏ dòng cũ', async () => {
    // Trình duyệt ghi một dòng lỗi console cho phản hồi 404 mà case này chủ động gây ra.
    allowedFailures.push({ urlPattern: /\/api\/admin\/support\/canned-responses\//, status: 404 });
    const vanishedShortcut = `matroi-${runSuffix}`;
    const created = await createCannedResponseByApi(page, {
      shortcut: vanishedShortcut,
      body: 'Mẫu sắp bị xóa.',
      shared: true,
    });
    await openSettingsPage();
    await expect(cannedRow(page, vanishedShortcut)).toBeVisible();
    // Xóa bằng API khi trang còn hiện dòng cũ. Backend trả 404 thật cho lần sửa kế tiếp.
    await deleteCannedResponseByApi(page, created.id);

    await page.getByRole('button', { name: `Sửa mẫu /${vanishedShortcut}` }).click();
    const editDialog = page.getByRole('dialog');
    await editDialog.getByLabel('Nội dung').fill('Nội dung sửa sau khi mẫu mất.');
    await editDialog.getByRole('button', { name: 'Lưu mẫu' }).click();

    await expect(editDialog).toHaveCount(0);
    await expect(
      page.getByRole('alert').filter({ hasText: 'Không tìm thấy mẫu trả lời nhanh.' }),
    ).toBeVisible();
    await expect(cannedRow(page, vanishedShortcut)).toHaveCount(0);
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
      const screenshotShortcut = `camon-${runSuffix}-${viewport.name}`;
      await createCannedResponseByApi(page, {
        shortcut: screenshotShortcut,
        body: 'Cảm ơn bạn đã liên hệ Pickory.',
        shared: true,
      });
      await openSettingsPage();
      await expect(cannedRow(page, screenshotShortcut)).toBeVisible();
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

test.describe('SD-UI5: nhân viên chỉ có support-settings:manage', () => {
  let adminContext: BrowserContext;
  let adminPage: Page;
  let managerContext: BrowserContext;
  let managerPage: Page;
  let managerConsoleIssues: string[];
  const sharedShortcut = `chung-quanly-${runSuffix}`;

  test.beforeAll(async ({ browser }) => {
    // Tạo vai trò, mời và kích hoạt nhân viên qua giao diện thật nên cần nhiều thời gian hơn mặc định.
    test.setTimeout(180000);
    adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    adminPage = await adminContext.newPage();
    await stubWebFonts(adminPage);
    await loginAdmin(adminPage);
    await dismissConsent(adminPage);

    // Vai trò chỉ có quyền cấu hình, không có `conversations:reply`. Đây là người dùng của quyết định Q1.
    const roleCode = `settings-only-${runSuffix}`;
    const role = await adminPage.request.post('/api/admin/roles', {
      headers: SUPPORT_API_HEADERS,
      data: {
        code: roleCode,
        name: `Chỉ cấu hình CSKH ${runSuffix}`,
        permissions: ['support-settings:manage'],
      },
    });
    expect(role.status(), await role.text()).toBe(201);
    const invitation = await adminPage.request.post('/api/admin/staff/invitations', {
      headers: SUPPORT_API_HEADERS,
      data: {
        email: managerEmail,
        name: 'Quản lý cấu hình CSKH E2E',
        roleCodes: [roleCode],
      },
    });
    expect(invitation.status(), await invitation.text()).toBeLessThan(300);
    await createCannedResponseByApi(adminPage, {
      shortcut: sharedShortcut,
      body: 'Mẫu dùng chung do admin tạo qua API.',
      shared: true,
    });

    managerContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    managerPage = await managerContext.newPage();
    managerConsoleIssues = collectConsoleIssues(managerPage);
    await stubWebFonts(managerPage);
    await activateStaffAccount(managerPage, managerEmail);
    await dismissConsent(managerPage);
    managerConsoleIssues.length = 0;
  });

  test.afterAll(async () => {
    try {
      if (adminPage !== undefined) await deleteCannedResponsesWithSuffix(adminPage, runSuffix);
    } finally {
      // Đóng cả hai context dù dọn dữ liệu lỗi, để trình duyệt không bị treo.
      await managerContext?.close();
      await adminContext?.close();
    }
  });

  test.afterEach(async () => {
    // Chờ request còn chạy dở xong, để lỗi muộn của trang vẫn nằm trong danh sách console trước khi kiểm.
    await managerPage.waitForLoadState('networkidle');
    expect(managerConsoleIssues, 'Console phải sạch').toEqual([]);
  });

  test('mở /admin/support/settings thấy danh sách mẫu dùng chung và nút Thêm mẫu, console sạch', async () => {
    await managerPage.goto('/admin/support/settings');
    await expect(
      managerPage.getByRole('heading', { level: 1, name: 'Cấu hình CSKH' }),
    ).toBeVisible();

    // Backend cho `GET` mẫu với quyền cấu hình, nên khối mẫu hiện danh sách thay cho câu thiếu quyền.
    const row = cannedRow(managerPage, sharedShortcut);
    await expect(row).toContainText('Mẫu dùng chung do admin tạo qua API.');
    await expect(row).not.toContainText('Chỉ xem');
    await expect(row.getByRole('button', { name: `Sửa mẫu /${sharedShortcut}` })).toBeVisible();
    await expect(managerPage.getByRole('button', { name: 'Thêm mẫu', exact: true })).toBeVisible();
    await expect(managerPage.getByText('conversations:reply')).toHaveCount(0);
  });
});
