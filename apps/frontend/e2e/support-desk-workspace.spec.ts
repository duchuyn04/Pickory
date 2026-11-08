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
import { createSupportDeskRouteMocks } from './support-desk-contract-routes';

// SD-UI1: màn CSKH, hai công tắc (sẵn sàng, âm báo) và trạng thái kết nối.
// Hai route `sessions` và `callback-requests` chưa có nên spec trả dữ liệu mẫu bằng helper SD-C2, còn `events` và `presence` dùng backend thật.

const PRESENCE_PATH = '/api/admin/support/presence';
const EVENTS_PATH_PATTERN = /\/api\/admin\/support\/events/;

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

// Trả dữ liệu mẫu cho các route backend chưa có. Helper đọc tài liệu OpenAPI trước khi gắn route.
async function mockUnfinishedQueueRoutes(page: Page) {
  const routes = createSupportDeskRouteMocks({ page, apiOrigin: e2eEnvironment.apiOrigin });
  await routes.mockRoute('admin-sessions.json', 'hàng chờ');
  await routes.mockRoute('admin-sessions.json', 'phiên của tôi sau khi đồng nghiệp chuyển');
  await routes.mockRoute('admin-callbacks.json', 'danh sách yêu cầu gọi lại');
  // Bốn route của khung chat SD-UI2: nhận phiên, gửi tin, lịch sử tin và danh sách mẫu.
  await routes.mockRoute('admin-sessions.json', 'nhận phiên');
  await routes.mockRoute('admin-sessions.json', 'nhân viên gửi tin');
  await routes.mockRoute('admin-sessions.json', 'lịch sử tin của hội thoại cho nhân viên');
  await routes.mockRoute('admin-canned-responses.json', 'danh sách mẫu');
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
  await expect(page.getByText('Đơn PK123 của mình giao tới đâu rồi?')).toBeVisible();
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

    // File mẫu có một phiên đang chờ thì badge là 1.
    await expect(waitingTab(page).locator('.tab-list-count')).toHaveText('1');
    await expect(
      page.getByRole('list', { name: 'Phiên đang chờ' }).getByRole('listitem'),
    ).toHaveCount(1);
    // Cột hàng chờ hiện nội dung của ví dụ "hàng chờ", không phải dữ liệu viết tay.
    await expect(page.getByText('Đơn PK123 của mình giao tới đâu rồi?')).toBeVisible();

    await page.getByRole('tab', { name: 'Gọi lại', exact: true }).click();
    const callbackList = page.getByRole('list', { name: 'Yêu cầu gọi lại' });
    // File mẫu có hai yêu cầu, một dòng đã gọi. Tab chỉ hiện dòng `pending`.
    await expect(callbackList.getByRole('listitem')).toHaveCount(1);
    await expect(callbackList.getByText('0901234567')).toBeVisible();

    await page.getByRole('tab', { name: 'Của tôi', exact: true }).click();
    const mineList = page.getByRole('list', { name: 'Phiên của tôi' });
    await expect(mineList.getByRole('listitem')).toHaveCount(1);
    await expect(mineList.getByText('Mình muốn đổi size áo.')).toBeVisible();
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
    await expect(page.getByText('Đơn PK123 của mình giao tới đâu rồi?')).toBeVisible();
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
      await expect(page.getByText('Đơn PK123 của mình giao tới đâu rồi?')).toBeVisible();

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
      await expect(waitingTab(page).locator('.tab-list-count')).toHaveText('1');
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
      await expect(page.getByText('0901234567')).toBeVisible();
      await expectNoHorizontalOverflow(page);
      await page.screenshot({
        path: screenshotPath(testInfo, `workspace-${viewport.name}-callbacks.png`),
        fullPage: true,
      });
    });
  }

  // SD-UI2: khung chat và mẫu trả lời nhanh. Bốn route của khung chat trả dữ liệu mẫu qua helper SD-C2.
  function queueRow(page: Page) {
    // Dòng phiên là một nút với tên truy cập gồm tin đầu và thời gian chờ.
    return page.getByRole('button', { name: /Đơn PK123/ });
  }

  function chatComposer(page: Page) {
    return page.getByRole('combobox', { name: 'Nội dung tin nhắn' });
  }

  async function openWaitingSession(page: Page) {
    await openDeskPage(page);
    await queueRow(page).click();
    // Lịch sử tin mẫu hiện ra nghĩa là khung chat đã nạp xong.
    // Dùng tin của nhân viên để tránh trùng chữ với tin đầu ở dòng hàng chờ (strict mode).
    await expect(page.getByText('Chào bạn, mình kiểm đơn PK123 ngay nhé.')).toBeVisible();
  }

  test('SD-UI2: mở phiên từ hàng chờ hiện lịch sử tin, nút Nhận và ô soạn tắt kèm chú thích', async () => {
    const { page } = support;
    await openWaitingSession(page);

    // Nhãn người gửi theo vai trò: khách và nhân viên.
    // Lịch sử mẫu không có tin hệ thống; nhãn hệ thống được unit test kiểm riêng.
    const chatRegion = page.locator('section', {
      has: page.getByRole('heading', { name: 'Khung chat' }),
    });
    await expect(chatRegion.getByText('Khách', { exact: true })).toBeVisible();
    await expect(chatRegion.getByText('Lan - CSKH', { exact: true })).toBeVisible();
    // Nút Nhận hiện ở đầu khung chat; ô soạn tắt với chú thích nêu rõ lý do.
    await expect(page.getByRole('button', { name: 'Nhận', exact: true })).toBeVisible();
    await expect(chatComposer(page)).toBeDisabled();
    await expect(page.getByText('Nhận hội thoại để trả lời.')).toBeVisible();
  });

  test('SD-UI2: bấm Nhận gọi POST assignment và ô soạn bật ngay', async () => {
    const { page } = support;
    await openWaitingSession(page);

    await page.getByRole('button', { name: 'Nhận', exact: true }).click();
    await expect(chatComposer(page)).toBeEnabled();
    // Nhận xong thì nút Nhận biến mất khỏi khung chat.
    await expect(page.getByRole('button', { name: 'Nhận', exact: true })).toHaveCount(0);
  });

  test('SD-UI2: gõ / mở danh sách mẫu, mũi tên chọn mục, Enter chèn mẫu đã thay biến', async () => {
    const { page } = support;
    await openWaitingSession(page);
    // Nhận phiên để ô soạn bật, rồi mở danh sách mẫu.
    await page.getByRole('button', { name: 'Nhận', exact: true }).click();
    await expect(chatComposer(page)).toBeEnabled();

    await chatComposer(page).fill('/');
    const listbox = page.getByRole('listbox');
    await expect(listbox).toBeVisible();
    // Dữ liệu mẫu có hai dòng: /camon dùng chung và /kiemdon cá nhân.
    await expect(page.getByRole('option', { name: /\/camon/ })).toHaveCount(1);
    await expect(page.getByRole('option', { name: /\/kiemdon/ })).toHaveCount(1);

    await page.keyboard.press('ArrowDown');
    await expect(page.getByRole('option', { name: /\/kiemdon/ })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    await page.keyboard.press('Enter');
    // Biến mẫu chưa có giá trị thật (SD-UI3 nối sau), nên {ten_khach} giữ nguyên theo luật.
    await expect(chatComposer(page)).toHaveValue('Mình kiểm đơn {ma_don} giúp bạn nhé.');
    await expect(listbox).toHaveCount(0);
  });

  test('SD-UI2: Enter gửi tin qua POST messages và dọn ô soạn', async () => {
    const { page } = support;
    await openWaitingSession(page);
    await page.getByRole('button', { name: 'Nhận', exact: true }).click();
    await expect(chatComposer(page)).toBeEnabled();

    await chatComposer(page).fill('Chào bạn, mình kiểm đơn PK123 ngay nhé.');
    await page.keyboard.press('Enter');
    // Gửi thành công thì ô sạch. Lịch sử mẫu cố định nên tin gửi không hiện thêm ở E2E.
    await expect(chatComposer(page)).toHaveValue('');
  });

  // Sinh 50 tin nhắn để kiểm tra vùng tin cuộn và ô soạn bám đáy khi lịch sử dài (R1-02).
  function createFiftyMessages() {
    const items = [];
    for (let index = 1; index <= 50; index++) {
      const isStaff = index % 2 === 0;
      items.push({
        id: `m-scroll-${index.toString().padStart(3, '0')}`,
        sessionId: '5e550000-0000-4000-8000-000000000001',
        role: isStaff ? 'staff' : 'customer',
        body: `Tin nhắn thứ ${index} trong hội thoại dài để kiểm tra vùng cuộn của khung chat.`,
        action: null,
        staffDisplayName: isStaff ? 'Lan - CSKH' : null,
        createdAt: new Date(
          Date.UTC(2026, 10, 3, 2, Math.floor(index / 2), (index % 2) * 30),
        ).toISOString(),
      });
    }
    return { data: { items, nextCursor: null } };
  }

  for (const viewport of [
    { name: '1280', width: 1280, height: 800 },
    { name: '375', width: 375, height: 812 },
  ]) {
    test(`SD-UI2: ảnh chụp khung chat ${viewport.name}px không tràn ngang và cuộn được với 50 tin`, async () => {
      const testInfo = test.info();
      const { page } = support;
      await page.setViewportSize({ width: viewport.width, height: viewport.height });

      // Ghi đè lịch sử với 50 tin nhắn để kiểm tra vùng cuộn thật và chụp ảnh có thanh cuộn (R1-02).
      const routes = createSupportDeskRouteMocks({ page, apiOrigin: e2eEnvironment.apiOrigin });
      await routes.mockRoute('admin-sessions.json', 'lịch sử tin của hội thoại cho nhân viên', {
        responseOverride: createFiftyMessages(),
      });

      await openDeskPage(page);
      await queueRow(page).click();

      const lastMessageLocator = page
        .locator('.support-chat-message')
        .filter({ hasText: 'Tin nhắn thứ 50' });
      await expect(lastMessageLocator).toBeVisible();

      const scrollRegion = page.locator('.support-chat-scroll');
      // Đọc scrollTop bằng expect.poll để đảm bảo useEffect cuộn đáy đã chạy xong (R2-02).
      await expect
        .poll(async () => scrollRegion.evaluate((el) => el.scrollTop), {
          message: 'Vùng tin phải cuộn xuống đáy khi có 50 tin',
        })
        .toBeGreaterThan(0);

      await page.getByRole('button', { name: 'Nhận', exact: true }).click();
      await expect(chatComposer(page)).toBeEnabled();

      // Mở danh sách mẫu: ô soạn cao thêm, kiểm tra tin 50 không bị cắt nhờ ResizeObserver (R2-02).
      await chatComposer(page).fill('/');
      await expect(page.getByRole('listbox')).toBeVisible();
      await expectNoHorizontalOverflow(page);

      // Ở 375px (xếp dọc): cuộn khung chat vào tầm nhìn trước khi đo.
      const chatCard = page.locator('.support-chat-open');
      await chatCard.scrollIntoViewIfNeeded();

      // Tin 50 phải nằm trọn trong vùng cuộn: cạnh dưới tin không vượt quá cạnh dưới vùng (R2-02).
      await expect
        .poll(
          async () => {
            const messageBox = await readBox(lastMessageLocator);
            const scrollBox = await readBox(scrollRegion);
            return messageBox.y + messageBox.height <= scrollBox.y + scrollBox.height + 2;
          },
          {
            message:
              'Cạnh dưới tin 50 không được vượt quá cạnh dưới vùng cuộn khi mở danh sách mẫu',
          },
        )
        .toBe(true);

      const composerBox = await readBox(chatComposer(page));
      expect(composerBox.x).toBeGreaterThanOrEqual(0);
      expect(composerBox.x + composerBox.width).toBeLessThanOrEqual(viewport.width);
      // Kiểm tra ô soạn nằm trong khung nhìn theo chiều dọc sau scrollIntoViewIfNeeded (R2-02 mục 2).
      expect(composerBox.y).toBeGreaterThanOrEqual(0);
      expect(composerBox.y + composerBox.height).toBeLessThanOrEqual(viewport.height);

      const cannedListBox = await readBox(page.getByRole('listbox'));
      expect(cannedListBox.x).toBeGreaterThanOrEqual(0);
      expect(cannedListBox.x + cannedListBox.width).toBeLessThanOrEqual(viewport.width);

      // Đính kèm số đo thật bằng testInfo.attach.
      const scrollMetrics = await scrollRegion.evaluate((el) => ({
        scrollTop: el.scrollTop,
        scrollHeight: el.scrollHeight,
        clientHeight: el.clientHeight,
      }));
      const chatCardBox = await readBox(chatCard);
      const message50Box = await readBox(lastMessageLocator);
      const scrollRegionBox = await readBox(scrollRegion);

      const measurements = {
        viewport,
        chatCard: chatCardBox,
        scrollRegion: scrollRegionBox,
        scrollMetrics,
        message50: message50Box,
        composer: composerBox,
        cannedList: cannedListBox,
      };

      await testInfo.attach(`measurements-${viewport.name}`, {
        body: JSON.stringify(measurements, null, 2),
        contentType: 'application/json',
      });

      await page.screenshot({
        path: screenshotPath(testInfo, `chat-${viewport.name}.png`),
        fullPage: true,
      });
      await page.setViewportSize({ width: 1280, height: 800 });
    });
  }
});
