import path from 'node:path';
import {
  expect,
  test,
  type Browser,
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
import { activateStaffAccount, inviteStaff } from './staff-fixtures';

// SD-UI1: màn CSKH, hai công tắc (sẵn sàng, âm báo) và trạng thái kết nối.
// Hai route `sessions` và `callback-requests` chưa có nên spec trả dữ liệu mẫu bằng `page.route`, còn `events` và `presence` dùng backend thật.

const PRESENCE_PATH = '/api/admin/support/presence';
const EVENTS_PATH_PATTERN = /\/api\/admin\/support\/events/;

const WAITING_SESSIONS = [
  {
    id: 'sd-ui1-session-1',
    conversationId: 'sd-ui1-conversation-1',
    preview: 'Tôi cần đổi địa chỉ giao hàng cho đơn đặt hôm qua, nhờ bạn hỗ trợ giúp mình.',
    waitingSeconds: 150,
    handoffAt: '2026-10-22T08:00:00Z',
  },
  {
    id: 'sd-ui1-session-2',
    conversationId: 'sd-ui1-conversation-2',
    // Chuỗi liền không có khoảng trắng để kiểm cột hàng chờ không bị đẩy tràn ngang ở 375px.
    preview: `https://pickory.example/orders/${'a'.repeat(110)}`,
    waitingSeconds: 45,
    handoffAt: '2026-10-22T08:02:00Z',
  },
];

const CALLBACK_REQUESTS = [
  { id: 'cb-1', phone: '0901000001', status: 'pending', createdAt: '2026-10-22T07:30:00Z' },
  { id: 'cb-2', phone: '0901000002', status: 'done', createdAt: '2026-10-22T07:40:00Z' },
  { id: 'cb-3', phone: '0901000003', status: 'pending', createdAt: '2026-10-22T07:50:00Z' },
];

interface StaffSession {
  context: BrowserContext;
  page: Page;
  consoleIssues: string[];
  // Lỗi tải mà một case chủ động gây ra. Case thêm vào rồi gỡ ra, nên mọi case khác vẫn đòi console sạch.
  allowedFailures: AllowedLoadFailure[];
}

async function openStaffSession(browser: Browser): Promise<StaffSession> {
  const context = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
  const page = await context.newPage();
  const allowedFailures: AllowedLoadFailure[] = [];
  const consoleIssues = collectConsoleIssues(page, allowedFailures);
  // Chặn phông chữ ngoài để lỗi mạng ngoài không làm console có lỗi oan.
  await stubWebFonts(page);
  return { context, page, consoleIssues, allowedFailures };
}

// Trả dữ liệu mẫu cho hai route backend chưa có. Request vẫn do trình duyệt gửi nên đường đi của client được kiểm thật.
async function mockUnfinishedQueueRoutes(page: Page) {
  await page.route(/\/api\/admin\/support\/sessions\?/, (route) => {
    const view = new URL(route.request().url()).searchParams.get('view');
    return route.fulfill({ json: { data: view === 'waiting' ? WAITING_SESSIONS : [] } });
  });
  await page.route('**/api/admin/support/callback-requests', (route) =>
    route.fulfill({ json: { data: CALLBACK_REQUESTS } }),
  );
}

async function readOwnUserId(page: Page): Promise<string> {
  const response = await page.request.get('/api/me');
  expect(response.status(), await response.text()).toBe(200);
  const body = (await response.json()) as { data: { userId: string } };
  return body.data.userId;
}

// Danh sách nhân viên đang sẵn sàng theo backend thật. Đây là bằng chứng trạng thái đã được ghi vào DB.
async function readAvailableStaffIds(page: Page): Promise<string[]> {
  const response = await page.request.get(PRESENCE_PATH);
  expect(response.status(), await response.text()).toBe(200);
  const body = (await response.json()) as { data: { staffId: string }[] };
  return body.data.map((entry) => entry.staffId);
}

// Ảnh chụp vào thư mục ngoài repo khi có biến môi trường. Không có biến thì Playwright giữ ảnh trong thư mục kết quả.
function screenshotPath(testInfo: { outputPath: (name: string) => string }, fileName: string) {
  const directory = process.env.SUPPORT_DESK_SHOTS_DIR;
  return directory ? path.join(directory, fileName) : testInfo.outputPath(fileName);
}

// Trả 503 cho luồng sự kiện. Hàm có tên riêng để `unroute` gỡ đúng route đã đăng ký.
function blockEvents(route: Route) {
  return route.fulfill({ status: 503, contentType: 'application/json', body: '{}' });
}

function isPresenceUrl(url: URL): boolean {
  return url.pathname === PRESENCE_PATH;
}

// Chỉ lệnh `PUT` bị trả 500. Lệnh đọc danh sách vẫn đi tới backend thật.
function failPresenceUpdate(route: Route) {
  if (route.request().method() !== 'PUT') return route.continue();
  return route.fulfill({
    status: 500,
    json: { error: { code: 'INTERNAL', message: 'Máy chủ gặp lỗi.' } },
  });
}

// Khung bao của phần tử. Phần tử không có khung thì case đỏ ngay, thay vì so với 0 cho qua.
async function readBox(locator: Locator) {
  const box = await locator.boundingBox();
  expect(box).not.toBeNull();
  return box!;
}

function presenceSwitch(page: Page) {
  return page.getByRole('switch', { name: 'Sẵn sàng', exact: true });
}

function soundSwitch(page: Page) {
  return page.getByRole('switch', { name: 'Âm báo', exact: true });
}

function waitingTab(page: Page) {
  return page.getByRole('tab', { name: /^Đang chờ/ });
}

async function openDeskPage(page: Page) {
  await page.goto('/admin/conversations');
  await expect(page.getByRole('heading', { level: 1, name: 'Chăm sóc khách hàng' })).toBeVisible();
  // Danh sách mẫu hiện ra nghĩa là cột hàng chờ đã nạp xong.
  await expect(page.getByText('Tôi cần đổi địa chỉ giao hàng')).toBeVisible();
}

test.describe.configure({ mode: 'serial' });

test.describe('SD-UI1: màn CSKH', () => {
  let admin: StaffSession;
  let support: StaffSession;
  let sales: StaffSession;

  test.beforeAll(async ({ browser }) => {
    // Mời hai nhân viên qua giao diện thật nên cần nhiều thời gian hơn mặc định.
    test.setTimeout(240000);
    admin = await openStaffSession(browser);
    support = await openStaffSession(browser);
    sales = await openStaffSession(browser);

    await loginAdmin(admin.page);
    await inviteStaff(admin.page, {
      email: e2eEnvironment.supportDeskSupportEmail,
      fullName: 'Support CSKH E2E',
      roleCode: 'support',
    });
    await inviteStaff(admin.page, {
      email: e2eEnvironment.supportDeskSalesEmail,
      fullName: 'Sales CSKH E2E',
      roleCode: 'sales',
    });
    await activateStaffAccount(support.page, e2eEnvironment.supportDeskSupportEmail);
    await activateStaffAccount(sales.page, e2eEnvironment.supportDeskSalesEmail);
    await dismissConsent(support.page);
    await dismissConsent(sales.page);
    await mockUnfinishedQueueRoutes(support.page);
    // Console của các bước dựng dữ liệu không thuộc case nào. Mỗi case bắt đầu với danh sách sạch.
    for (const session of [admin, support, sales]) session.consoleIssues.length = 0;
  });

  test.afterAll(async () => {
    for (const session of [admin, support, sales]) await session?.context.close();
  });

  test.beforeEach(() => {
    for (const session of [admin, support, sales]) session.consoleIssues.length = 0;
  });

  test.afterEach(() => {
    // Mỗi case phải kết thúc với 0 lỗi và 0 cảnh báo trong console của mọi phiên.
    expect(admin.consoleIssues, 'Console của admin phải sạch').toEqual([]);
    expect(support.consoleIssues, 'Console của support phải sạch').toEqual([]);
    expect(sales.consoleIssues, 'Console của sales phải sạch').toEqual([]);
  });

  test('support thấy menu CSKH, mở trang và bật Sẵn sàng thì backend ghi nhận', async () => {
    const { page } = support;
    await page.goto('/admin');
    const menuLink = page.getByRole('link', { name: 'CSKH', exact: true });
    await expect(menuLink).toBeVisible();

    await menuLink.click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/conversations`);
    await expect(
      page.getByRole('heading', { level: 1, name: 'Chăm sóc khách hàng' }),
    ).toBeVisible();
    // Ba cột: hàng chờ, khung chat và thông tin khách.
    await expect(page.getByRole('region', { name: 'Hàng chờ' })).toBeVisible();
    await expect(page.getByRole('heading', { level: 2, name: 'Khung chat' })).toBeVisible();
    await expect(page.getByRole('heading', { level: 2, name: 'Thông tin khách' })).toBeVisible();

    const staffId = await readOwnUserId(page);
    const toggle = presenceSwitch(page);
    // Công tắc nằm trên thanh trên cùng của khung quản trị.
    await expect(
      page.locator('header.admin-top').getByRole('switch', { name: 'Sẵn sàng' }),
    ).toBeVisible();
    await expect(toggle).toHaveAttribute('aria-checked', 'false');
    expect(await readAvailableStaffIds(page)).not.toContain(staffId);

    await toggle.click();
    await expect(toggle).toHaveAttribute('aria-checked', 'true');
    await expect.poll(() => readAvailableStaffIds(page)).toContain(staffId);

    // Tải lại trang: công tắc đọc trạng thái từ backend nên vẫn bật.
    await page.reload();
    await expect(toggle).toHaveAttribute('aria-checked', 'true');

    await toggle.click();
    await expect(toggle).toHaveAttribute('aria-checked', 'false');
    await expect.poll(() => readAvailableStaffIds(page)).not.toContain(staffId);
  });

  test('hàng chờ hiện dữ liệu mẫu, badge khớp số phiên, tab Gọi lại chỉ có yêu cầu pending', async () => {
    const { page } = support;
    await openDeskPage(page);

    // Hai phiên mẫu thì badge là 2.
    await expect(waitingTab(page).locator('.tab-list-count')).toHaveText('2');
    await expect(
      page.getByRole('list', { name: 'Phiên đang chờ' }).getByRole('listitem'),
    ).toHaveCount(2);

    await page.getByRole('tab', { name: 'Gọi lại', exact: true }).click();
    const callbackList = page.getByRole('list', { name: 'Yêu cầu gọi lại' });
    await expect(callbackList.getByRole('listitem')).toHaveCount(2);
    await expect(callbackList.getByText('0901000001')).toBeVisible();
    await expect(callbackList.getByText('0901000003')).toBeVisible();
    await expect(callbackList.getByText('0901000002')).toHaveCount(0);

    await page.getByRole('tab', { name: 'Của tôi', exact: true }).click();
    await expect(page.getByText('Bạn chưa nhận hội thoại nào.')).toBeVisible();
  });

  test('bàn phím: mũi tên đổi tab và phím Tab tới được công tắc Sẵn sàng', async () => {
    const { page } = support;
    await openDeskPage(page);
    await expect(page.getByRole('tablist', { name: 'Hàng chờ chăm sóc khách hàng' })).toBeVisible();

    const tabs = page.getByRole('tab');
    await waitingTab(page).focus();
    await page.keyboard.press('ArrowRight');
    await expect(page.getByRole('tab', { name: 'Của tôi', exact: true })).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('tab', { name: 'Của tôi', exact: true })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    await page.keyboard.press('ArrowLeft');
    await expect(waitingTab(page)).toBeFocused();
    await page.keyboard.press('End');
    await expect(page.getByRole('tab', { name: 'Gọi lại', exact: true })).toBeFocused();
    await expect(tabs).toHaveCount(3);

    // Đưa focus về đầu trang rồi bấm Tab cho tới khi tới công tắc. Menu bên trái có nhiều link nên cần nhiều lần bấm.
    await page.goto('/admin/conversations');
    await expect(
      page.getByRole('heading', { level: 1, name: 'Chăm sóc khách hàng' }),
    ).toBeVisible();
    const toggle = presenceSwitch(page);
    let reachedToggle = false;
    for (let pressCount = 0; pressCount < 60 && !reachedToggle; pressCount++) {
      await page.keyboard.press('Tab');
      reachedToggle = await toggle.evaluate((element) => element === document.activeElement);
    }
    expect(reachedToggle, 'Phím Tab phải tới được công tắc Sẵn sàng').toBe(true);

    // Công tắc đổi trạng thái bằng phím Space. Trả về trạng thái cũ để case sau không bị ảnh hưởng.
    await page.keyboard.press('Space');
    await expect(toggle).toHaveAttribute('aria-checked', 'true');
    await page.keyboard.press('Space');
    await expect(toggle).toHaveAttribute('aria-checked', 'false');
  });

  test('tắt âm báo rồi tải lại trang thì công tắc vẫn tắt', async () => {
    const { page } = support;
    await openDeskPage(page);
    const toggle = soundSwitch(page);
    await expect(toggle).toHaveAttribute('aria-checked', 'true');

    await toggle.click();
    await expect(toggle).toHaveAttribute('aria-checked', 'false');

    await page.reload();
    await expect(page.getByText('Tôi cần đổi địa chỉ giao hàng')).toBeVisible();
    await expect(soundSwitch(page)).toHaveAttribute('aria-checked', 'false');

    // Bật lại để các case sau bắt đầu từ mặc định.
    await soundSwitch(page).click();
    await expect(soundSwitch(page)).toHaveAttribute('aria-checked', 'true');
  });

  test('mất kết nối: hiện chữ Mất kết nối khi luồng sự kiện lỗi, và mất chữ khi luồng nối lại được', async () => {
    test.setTimeout(120000);
    const testInfo = test.info();
    const { page } = support;
    // Chặn route `events` làm trình duyệt ghi lỗi tải 503 mỗi lần thử nối.
    // Case chỉ bỏ qua đúng lỗi này (URL của luồng, mã 503), mọi lỗi khác vẫn làm case đỏ.
    support.allowedFailures.push({ urlPattern: EVENTS_PATH_PATTERN, status: 503 });
    try {
      await openDeskPage(page);
      const lostNotice = page.locator('header.admin-top').getByText('Mất kết nối', { exact: true });
      await expect(lostNotice).toBeHidden();

      await page.route(EVENTS_PATH_PATTERN, blockEvents);
      // Tải lại để trang mở luồng mới. Luồng cũ đang mở nên route chặn chưa tác động tới nó.
      await page.reload();
      await expect(page.getByText('Tôi cần đổi địa chỉ giao hàng')).toBeVisible();

      await expect(lostNotice).toBeVisible({ timeout: 20000 });
      await page.screenshot({ path: screenshotPath(testInfo, 'lost-connection-1280.png') });

      // Ở 375px logo, chữ báo mất kết nối và công tắc phải nằm trên một hàng của thanh, cao 56px, không tràn ngang.
      await page.setViewportSize({ width: 375, height: 812 });
      await expect(lostNotice).toBeVisible();
      await expect(presenceSwitch(page)).toBeVisible();
      await expectNoHorizontalOverflow(page);
      expect((await readBox(page.locator('header.admin-top'))).height).toBe(56);
      await page.screenshot({
        path: screenshotPath(testInfo, 'lost-connection-375.png'),
        fullPage: true,
      });
      await page.setViewportSize({ width: 1280, height: 800 });

      // Gỡ route chặn. Client tự nối lại sau tối đa 30 giây, rồi chữ báo mất kết nối biến mất.
      await page.unroute(EVENTS_PATH_PATTERN, blockEvents);
      await expect(lostNotice).toBeHidden({ timeout: 60000 });
    } finally {
      // Gỡ route cả khi case đỏ giữa chừng, để route 503 không sót sang case sau. Gỡ hai lần không gây lỗi.
      await page.unroute(EVENTS_PATH_PATTERN, blockEvents);
      support.allowedFailures.length = 0;
    }
  });

  test('PUT trạng thái lỗi: công tắc giữ trạng thái cũ, báo lỗi ngắn và thanh trên cùng không tràn ngang ở 375px', async () => {
    const testInfo = test.info();
    const { page } = support;
    // Route chặn làm trình duyệt ghi lỗi tải 500 của `PUT`. Case chỉ bỏ qua đúng lỗi này, mọi lỗi khác vẫn làm case đỏ.
    support.allowedFailures.push({ urlPattern: /\/api\/admin\/support\/presence/, status: 500 });
    try {
      await page.setViewportSize({ width: 375, height: 812 });
      await openDeskPage(page);
      await expect(presenceSwitch(page)).toHaveAttribute('aria-checked', 'false');

      await page.route(isPresenceUrl, failPresenceUpdate);
      await presenceSwitch(page).click();

      const topBar = page.locator('header.admin-top');
      const errorLine = topBar.getByRole('alert');
      await expect(errorLine).toContainText('Không đổi được trạng thái. Bấm công tắc để thử lại.');
      await expect(presenceSwitch(page)).toHaveAttribute('aria-checked', 'false');
      await expectNoHorizontalOverflow(page);
      // Ở 375px dòng lỗi là hàng thứ hai của thanh, nằm dưới hàng công tắc. Tiêu đề trang bị đẩy xuống, không bị che.
      const switchBox = await readBox(presenceSwitch(page));
      const errorBox = await readBox(errorLine);
      const headingBox = await readBox(
        page.getByRole('heading', { level: 1, name: 'Chăm sóc khách hàng' }),
      );
      expect(errorBox.y).toBeGreaterThanOrEqual(switchBox.y + switchBox.height);
      expect(errorBox.y + errorBox.height).toBeLessThanOrEqual(headingBox.y);
      await page.screenshot({
        path: screenshotPath(testInfo, 'presence-error-375.png'),
        fullPage: true,
      });

      // Ở 1280px dòng lỗi nằm cạnh công tắc, trọn trong thanh 48px.
      await page.setViewportSize({ width: 1280, height: 800 });
      await expect(errorLine).toBeVisible();
      await expectNoHorizontalOverflow(page);
      const desktopTopBarBox = await readBox(topBar);
      const desktopErrorBox = await readBox(errorLine);
      const desktopSwitchBox = await readBox(presenceSwitch(page));
      expect(desktopErrorBox.y).toBeGreaterThanOrEqual(desktopTopBarBox.y);
      expect(desktopErrorBox.y + desktopErrorBox.height).toBeLessThanOrEqual(
        desktopTopBarBox.y + desktopTopBarBox.height,
      );
      expect(desktopErrorBox.x).toBeGreaterThanOrEqual(desktopSwitchBox.x + desktopSwitchBox.width);
      await page.screenshot({ path: screenshotPath(testInfo, 'presence-error-1280.png') });
      // Backend không nhận lệnh nào, nên nhân viên vẫn Tạm vắng.
      const staffId = await readOwnUserId(page);
      expect(await readAvailableStaffIds(page)).not.toContain(staffId);
    } finally {
      // `unroute` so khớp theo tham chiếu hàm, nên phải truyền đúng hai hàm đã đăng ký.
      await page.unroute(isPresenceUrl, failPresenceUpdate);
      await page.setViewportSize({ width: 1280, height: 800 });
      support.allowedFailures.length = 0;
    }
  });

  test('sales không thấy menu CSKH, mở thẳng URL thì thấy trang không đủ quyền và không gọi API support desk', async () => {
    const { page } = sales;
    const supportRequests: string[] = [];
    page.on('request', (request) => {
      if (request.url().includes('/api/admin/support')) supportRequests.push(request.url());
    });

    await page.goto('/admin');
    await expect(page.getByRole('link', { name: 'Tổng quan', exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'CSKH', exact: true })).toHaveCount(0);
    await expect(presenceSwitch(page)).toHaveCount(0);

    await page.goto('/admin/conversations');
    const notice = page.getByRole('alert');
    await expect(notice).toContainText('403 - Không có quyền truy cập');
    await expect(notice).toContainText('conversations:read');
    await expect(page.getByRole('tablist')).toHaveCount(0);
    // Trang thiếu quyền không vẽ cột nào, nên không có query nào để gửi request tới API support desk.
    expect(supportRequests).toEqual([]);
  });

  for (const viewport of [
    { name: '1280', width: 1280, height: 800, topBarHeight: 48 },
    { name: '375', width: 375, height: 812, topBarHeight: 56 },
  ]) {
    test(`ảnh chụp ${viewport.name}px: không tràn ngang`, async () => {
      const testInfo = test.info();
      const { page } = support;
      await page.setViewportSize({ width: viewport.width, height: viewport.height });
      await openDeskPage(page);
      await expect(waitingTab(page).locator('.tab-list-count')).toHaveText('2');
      await expect(presenceSwitch(page)).toBeVisible();
      await expectNoHorizontalOverflow(page);
      // Khi không có lỗi, thanh trên cùng cao 48px ở màn rộng và 56px ở màn hẹp.
      expect((await readBox(page.locator('header.admin-top'))).height).toBe(viewport.topBarHeight);
      await page.screenshot({
        path: screenshotPath(testInfo, `workspace-${viewport.name}.png`),
        fullPage: true,
      });

      // Bật Sẵn sàng để ảnh chụp thể hiện công tắc ở trạng thái bật, rồi trả về trạng thái cũ.
      await presenceSwitch(page).click();
      await expect(presenceSwitch(page)).toHaveAttribute('aria-checked', 'true');
      await expectNoHorizontalOverflow(page);
      await page.screenshot({
        path: screenshotPath(testInfo, `workspace-${viewport.name}-available.png`),
        fullPage: true,
      });
      await presenceSwitch(page).click();
      await expect(presenceSwitch(page)).toHaveAttribute('aria-checked', 'false');

      await page.getByRole('tab', { name: 'Gọi lại', exact: true }).click();
      await expect(page.getByText('0901000001')).toBeVisible();
      await expectNoHorizontalOverflow(page);
      await page.screenshot({
        path: screenshotPath(testInfo, `workspace-${viewport.name}-callbacks.png`),
        fullPage: true,
      });
    });
  }
});
