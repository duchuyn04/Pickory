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
  // Ba route của SD-UI3: bảng thông tin khách, chuyển phiên và kết thúc phiên.
  await routes.mockRoute('admin-customer-panel.json', 'khách đã đăng nhập');
  await routes.mockRoute('admin-customer-panel.json', 'khách vãng lai');
  await routes.mockRoute('admin-sessions.json', 'chuyển phiên cho đồng nghiệp');
  await routes.mockRoute('admin-sessions.json', 'kết thúc phiên');
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
  let colleague: StaffSession;
  const colleagueEmail = 'e2e-support-desk-colleague@pickory.local';

  test.beforeAll(async ({ browser }) => {
    // Mời hai nhân viên qua giao diện thật nên cần nhiều thời gian hơn mặc định.
    test.setTimeout(240000);
    admin = await openStaffSession(browser);
    support = await openStaffSession(browser);
    sales = await openStaffSession(browser);
    colleague = await openStaffSession(browser);
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
    await inviteStaff(admin.page, {
      email: colleagueEmail,
      fullName: 'Colleague CSKH E2E',
      roleCode: 'support',
    });
    await activateStaffAccount(colleague.page, colleagueEmail);
    await dismissConsent(colleague.page);
    await activateStaffAccount(support.page, e2eEnvironment.supportDeskSupportEmail);
    await activateStaffAccount(sales.page, e2eEnvironment.supportDeskSalesEmail);
    await dismissConsent(support.page);
    await dismissConsent(sales.page);
    await mockUnfinishedQueueRoutes(support.page);
    // Console của các bước dựng dữ liệu không thuộc case nào. Mỗi case bắt đầu với danh sách sạch.
    for (const session of [admin, support, sales, colleague]) session.consoleIssues.length = 0;
  });

  test.afterAll(async () => {
    for (const session of [admin, support, sales, colleague]) await session?.context.close();
  });

  test.beforeEach(() => {
    for (const session of [admin, support, sales, colleague]) session.consoleIssues.length = 0;
  });

  test.afterEach(() => {
    // Mỗi case phải kết thúc với 0 lỗi và 0 cảnh báo trong console của mọi phiên.
    expect(admin.consoleIssues, 'Console của admin phải sạch').toEqual([]);
    expect(support.consoleIssues, 'Console của support phải sạch').toEqual([]);
    expect(sales.consoleIssues, 'Console của sales phải sạch').toEqual([]);
    expect(colleague.consoleIssues, 'Console của colleague phải sạch').toEqual([]);
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
    const routes = createSupportDeskRouteMocks({ page, apiOrigin: e2eEnvironment.apiOrigin });
    await routes.mockRoute('admin-sessions.json', 'lịch sử tin của hội thoại cho nhân viên');
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
    // SD-UI3 đã nối nguồn dữ liệu khách: đơn đầu tiên là PK123 nên {ma_don} được thay bằng PK123.
    await expect(chatComposer(page)).toHaveValue('Mình kiểm đơn PK123 giúp bạn nhé.');
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

  test('SD-UI3: cột thông tin khách hiển thị dữ liệu khách đã đăng nhập và link đơn hàng', async () => {
    const { page } = support;
    await openWaitingSession(page);

    // Cột thông tin khách hiển thị hồ sơ khách hàng đã đăng nhập.
    const customerPanel = page.locator('.support-customer-panel');
    await expect(customerPanel.getByText('Nguyễn Minh Anh')).toBeVisible();
    await expect(customerPanel.getByText('minhanh@example.com')).toBeVisible();
    await expect(customerPanel.getByText('Khách vãng lai')).toHaveCount(0);

    // Đơn hàng PK123 có link tới trang quản trị đơn, trạng thái, tổng tiền và số sản phẩm.
    const orderLink = customerPanel.getByRole('link', { name: 'PK123' });
    await expect(orderLink).toBeVisible();
    await expect(orderLink).toHaveAttribute('href', '/admin/orders/PK123');
    await expect(customerPanel.getByText('Đang giao hàng')).toBeVisible();
    await expect(customerPanel.getByText('459.000 đ')).toBeVisible();
    await expect(customerPanel.getByText('2 sản phẩm')).toBeVisible();
  });

  test('SD-UI3: mở phiên từ tab Của tôi hiện thông tin khách vãng lai Chưa có thông tin', async () => {
    const { page } = support;
    await page.goto('/admin/conversations');
    await expect(
      page.getByRole('heading', { level: 1, name: 'Chăm sóc khách hàng' }),
    ).toBeVisible();

    // Mở tab Của tôi và chọn phiên khách vãng lai.
    await page.getByRole('tab', { name: 'Của tôi' }).click();
    await page.getByText('Mình muốn đổi size áo.').click();

    const customerPanel = page.locator('.support-customer-panel');
    // Hiển thị nhãn Khách vãng lai, Chưa có thông tin và thông báo không có đơn hàng.
    await expect(customerPanel.getByText('Khách vãng lai')).toBeVisible();
    await expect(customerPanel.getByText('Chưa có thông tin')).toHaveCount(2);
    await expect(customerPanel.getByText('Khách chưa có đơn hàng.')).toBeVisible();
  });

  test('SD-UI3: hộp thoại chuyển chỉ hiện đồng nghiệp sẵn sàng, gửi PUT với staffId và note', async () => {
    const { page } = support;
    const colleagueUserId = await readOwnUserId(colleague.page);
    const putRes = await colleague.page.request.put(PRESENCE_PATH, {
      headers: { Origin: e2eEnvironment.webOrigin },
      data: { status: 'available' },
    });
    expect(putRes.status()).toBe(200);

    await page.goto('/admin/conversations');
    await page.getByRole('tab', { name: 'Của tôi' }).click();
    await page.getByText('Mình muốn đổi size áo.').click();

    // Vùng thông báo role=status luôn hiện diện trong DOM với display: block ngay cả khi chưa có nội dung.
    const statusNotice = page.locator('.support-transfer-status');
    await expect(statusNotice).toHaveCSS('display', 'block');

    // Nút Chuyển và Kết thúc hiển thị trên thanh trên của khung chat.
    const transferButton = page.getByRole('button', { name: 'Chuyển', exact: true });
    await expect(transferButton).toBeVisible();
    await expect(page.getByRole('button', { name: 'Kết thúc', exact: true })).toBeVisible();

    await transferButton.click();
    const dialog = page.getByRole('dialog', { name: 'Chuyển hội thoại' });
    await expect(dialog).toBeVisible();

    // Backend thật chưa trả displayName nên nhãn hiển thị Nhân viên cộng 4 ký tự cuối của staffId.
    const staffLabel = `Nhân viên ${colleagueUserId.slice(-4)}`;
    const staffRadio = dialog.getByLabel(staffLabel);
    await expect(staffRadio).toBeVisible();

    // Chọn nhân viên và nhập ghi chú nội bộ.
    await staffRadio.click();
    const noteField = dialog.getByLabel('Ghi chú nội bộ');
    await noteField.fill('  Khách cần đổi size giày sang 42  ');

    // Bắt request PUT assignment khi bấm Chuyển.
    const putRequestPromise = page.waitForRequest(
      (req) => req.url().includes('/assignment') && req.method() === 'PUT',
    );

    await dialog.getByRole('button', { name: 'Chuyển', exact: true }).click();

    const putRequest = await putRequestPromise;
    const putBody = putRequest.postDataJSON() as { staffId: string; note: string | null };
    expect(putBody).toEqual({
      staffId: colleagueUserId,
      note: 'Khách cần đổi size giày sang 42',
    });
    // Hộp thoại đóng và dòng status thông báo xuất hiện ở đầu trang.
    await expect(dialog).toHaveCount(0);
    await expect(statusNotice).toBeVisible();
    await expect(statusNotice).toContainText(`Đã chuyển hội thoại cho ${staffLabel}.`);

    // Khung chat quay về trạng thái chưa chọn phiên.
    await expect(page.getByText('Tin nhắn của hội thoại đang mở sẽ hiện ở đây.')).toBeVisible();

    // Dọn dẹp trạng thái của colleague về away.
    await colleague.page.request.put(PRESENCE_PATH, {
      headers: { Origin: e2eEnvironment.webOrigin },
      data: { status: 'away' },
    });
  });

  test('SD-UI3: bấm Kết thúc hỏi xác nhận trước, gửi PATCH closed và ô soạn tắt', async () => {
    const { page } = support;
    const patchRequests: string[] = [];
    page.on('request', (req) => {
      if (req.url().includes('/api/admin/support/sessions/') && req.method() === 'PATCH') {
        patchRequests.push(req.url());
      }
    });

    await page.goto('/admin/conversations');
    await page.getByRole('tab', { name: 'Của tôi' }).click();
    await page.getByText('Mình muốn đổi size áo.').click();

    const endBtn = page.getByRole('button', { name: 'Kết thúc', exact: true });

    // Bấm nút Kết thúc ở thanh trên mở hộp thoại xác nhận chứ chưa gọi API.
    await endBtn.click();

    const dialog = page.getByRole('dialog', { name: 'Kết thúc hội thoại này?' });
    await expect(dialog).toBeVisible();
    await expect(
      dialog.getByText(
        'Khách hàng sẽ nhận được biểu mẫu chấm điểm dịch vụ sau khi kết thúc hội thoại.',
      ),
    ).toBeVisible();
    // Chưa có PATCH nào được gửi khi hộp thoại vừa mở.
    expect(patchRequests).toHaveLength(0);

    // Bấm nút Hủy trong hộp thoại: hộp thoại đóng lại và chưa có request PATCH nào được gửi.
    await dialog.getByRole('button', { name: 'Hủy', exact: true }).click();
    await expect(dialog).toHaveCount(0);
    expect(patchRequests).toHaveLength(0);
    await expect(endBtn).toBeVisible();

    // Bấm lại nút Kết thúc ở thanh trên khung chat.
    await endBtn.click();
    await expect(dialog).toBeVisible();

    // Bắt request PATCH khi bấm xác nhận trong hộp thoại.
    const patchRequestPromise = page.waitForRequest(
      (req) => req.url().includes('/api/admin/support/sessions/') && req.method() === 'PATCH',
    );

    await dialog.getByRole('button', { name: 'Kết thúc', exact: true }).click();

    const patchRequest = await patchRequestPromise;
    const patchBody = patchRequest.postDataJSON() as { status: string };
    expect(patchBody).toEqual({ status: 'closed' });
    expect(patchRequests).toHaveLength(1);

    // Hộp thoại đóng và tiêu điểm chuyển về tiêu đề khung chat.
    await expect(dialog).toHaveCount(0);
    await expect(page.locator('#support-chat-title')).toBeFocused();
    await expect(chatComposer(page)).toBeDisabled();
    await expect(page.getByText('Phiên đã kết thúc.')).toBeVisible();
  });

  test('SD-UI3: phím Esc đóng hộp thoại chuyển và kết thúc, trả focus về đúng nút mở', async () => {
    const { page } = support;
    await page.goto('/admin/conversations');
    await page.getByRole('tab', { name: 'Của tôi' }).click();
    await page.getByText('Mình muốn đổi size áo.').click();

    const transferBtn = page.getByRole('button', { name: 'Chuyển', exact: true });
    const endBtn = page.getByRole('button', { name: 'Kết thúc', exact: true });

    // Mở hộp thoại chuyển rồi bấm Esc để kiểm tra trả tiêu điểm.
    await transferBtn.click();
    const transferDialog = page.getByRole('dialog', { name: 'Chuyển hội thoại' });
    await expect(transferDialog).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(transferDialog).toHaveCount(0);
    await expect(transferBtn).toBeFocused();

    // Mở hộp thoại kết thúc rồi bấm Esc để kiểm tra trả tiêu điểm.
    await endBtn.click();
    const endDialog = page.getByRole('dialog', { name: 'Kết thúc hội thoại này?' });
    await expect(endDialog).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(endDialog).toHaveCount(0);
    await expect(endBtn).toBeFocused();
  });

  test('SD-UI3: ở 375px ngăn thông tin khách mở bằng nút, không tràn ngang và đo boundingBox', async () => {
    const testInfo = test.info();
    const { page } = support;
    await page.setViewportSize({ width: 375, height: 812 });

    await openWaitingSession(page);

    const toggleButton = page.getByRole('button', { name: 'Thông tin khách' });
    await expect(toggleButton).toBeVisible();
    await expect(toggleButton).toHaveAttribute('aria-expanded', 'false');

    const customerPanel = page.locator('.support-customer-panel');
    const customerName = customerPanel.getByText('Nguyễn Minh Anh');
    // Trước khi bấm mở ngăn, thông tin khách hàng phải ở trạng thái ẩn.
    await expect(customerName).toBeHidden();

    await expectNoHorizontalOverflow(page);
    await page.screenshot({
      path: screenshotPath(testInfo, 'customer-panel-closed-375.png'),
      fullPage: true,
    });

    const toggleBoxClosed = await readBox(toggleButton);
    // Chiều cao nút đóng phải nhỏ hơn 64px và nhãn chữ nằm gọn trên một dòng.
    expect(toggleBoxClosed.height).toBeLessThan(64);
    const labelBox = await readBox(toggleButton.locator('span'));
    expect(labelBox.height).toBeLessThan(28);

    // Mở ngăn thông tin khách ở 375px.
    await toggleButton.click();
    await expect(toggleButton).toHaveAttribute('aria-expanded', 'true');

    // Sau khi mở ngăn, thông tin khách hàng hiển thị rõ ràng.
    await expect(customerName).toBeVisible();

    await expectNoHorizontalOverflow(page);
    await page.screenshot({
      path: screenshotPath(testInfo, 'customer-panel-open-375.png'),
      fullPage: true,
    });

    const toggleBoxOpen = await readBox(toggleButton);
    const panelBoxOpen = await readBox(customerPanel);

    await testInfo.attach('measurements-customer-panel-375', {
      body: JSON.stringify(
        {
          viewport: { width: 375, height: 812 },
          toggleClosed: toggleBoxClosed,
          toggleOpen: toggleBoxOpen,
          panelOpen: panelBoxOpen,
        },
        null,
        2,
      ),
      contentType: 'application/json',
    });

    // Ở màn hình rộng 1280px: nút ngăn ẩn đi và nội dung luôn hiển thị.
    await page.setViewportSize({ width: 1280, height: 800 });
    await expect(toggleButton).toBeHidden();
    await expect(customerName).toBeVisible();
  });

  test('SD-UI3: ảnh chụp 1280px và 375px của cột khách và các hộp thoại không tràn ngang', async () => {
    const testInfo = test.info();
    const { page } = support;
    await colleague.page.request.put(PRESENCE_PATH, {
      headers: { Origin: e2eEnvironment.webOrigin },
      data: { status: 'available' },
    });

    // 1. Chụp cột thông tin khách ở 1280px.
    await page.setViewportSize({ width: 1280, height: 800 });
    await openWaitingSession(page);
    await expectNoHorizontalOverflow(page);

    const customerPanel1280 = page.locator('.support-customer-panel');
    const panelBox1280 = await readBox(customerPanel1280);
    // Đỉnh lưới của ba cột ở 1280px phải bắt đầu tại y=159 (lệch không quá 2px).
    expect(Math.abs(panelBox1280.y - 159)).toBeLessThanOrEqual(2);

    await page.screenshot({
      path: screenshotPath(testInfo, 'customer-panel-1280.png'),
      fullPage: true,
    });

    // 2. Chụp hộp thoại chuyển ở 1280px và 375px.
    await page.getByRole('tab', { name: 'Của tôi' }).click();
    await page.getByText('Mình muốn đổi size áo.').click();

    await page.getByRole('button', { name: 'Chuyển', exact: true }).click();
    const transferDialog = page.getByRole('dialog', { name: 'Chuyển hội thoại' });
    await expect(transferDialog).toBeVisible();

    // Chọn đồng nghiệp và nhập ghi chú nội bộ trước khi chụp ảnh theo yêu cầu review.
    const colleagueUserId = await readOwnUserId(colleague.page);
    const staffLabel = `Nhân viên ${colleagueUserId.slice(-4)}`;
    await transferDialog.getByLabel(staffLabel).click();
    await transferDialog.getByLabel('Ghi chú nội bộ').fill('Khách cần đổi size giày sang 42');

    await expectNoHorizontalOverflow(page);

    const transferDialogBox1280 = await readBox(transferDialog);
    await page.screenshot({
      path: screenshotPath(testInfo, 'transfer-dialog-1280.png'),
      fullPage: true,
    });

    // Hộp thoại chuyển ở 375px.
    await page.setViewportSize({ width: 375, height: 812 });
    await expectNoHorizontalOverflow(page);
    const transferDialogBox375 = await readBox(transferDialog);
    await page.screenshot({
      path: screenshotPath(testInfo, 'transfer-dialog-375.png'),
      fullPage: true,
    });

    // Đóng hộp thoại chuyển.
    await page.keyboard.press('Escape');
    await expect(transferDialog).toHaveCount(0);

    // 3. Chụp hộp thoại kết thúc ở 375px và 1280px.
    await page.getByRole('button', { name: 'Kết thúc', exact: true }).click();
    const endDialog = page.getByRole('dialog', { name: 'Kết thúc hội thoại này?' });
    await expect(endDialog).toBeVisible();
    await expectNoHorizontalOverflow(page);

    const endDialogBox375 = await readBox(endDialog);
    await page.screenshot({
      path: screenshotPath(testInfo, 'end-dialog-375.png'),
      fullPage: true,
    });

    await page.setViewportSize({ width: 1280, height: 800 });
    await expectNoHorizontalOverflow(page);
    const endDialogBox1280 = await readBox(endDialog);
    await page.screenshot({
      path: screenshotPath(testInfo, 'end-dialog-1280.png'),
      fullPage: true,
    });

    // Đóng hộp thoại kết thúc.
    await page.keyboard.press('Escape');
    await expect(endDialog).toHaveCount(0);

    const measurements = {
      panel1280: panelBox1280,
      transferDialog1280: transferDialogBox1280,
      transferDialog375: transferDialogBox375,
      endDialog1280: endDialogBox1280,
      endDialog375: endDialogBox375,
    };

    await testInfo.attach('measurements-dialogs-and-panels', {
      body: JSON.stringify(measurements, null, 2),
      contentType: 'application/json',
    });

    await colleague.page.request.put(PRESENCE_PATH, {
      headers: { Origin: e2eEnvironment.webOrigin },
      data: { status: 'away' },
    });
  });
});
