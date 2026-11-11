import { expect, test, type Page, type Route } from '@playwright/test';
import { expectNoHorizontalOverflow, prepareShipping, seedProduct } from './admin-order-fixtures';
import {
  collectConsoleIssues,
  dismissConsent,
  loginAdmin,
  type AllowedLoadFailure,
} from './catalog-admin-flow';
import {
  confirmAndShipOrderByApi,
  loginSeededCustomer,
  openIsolatedSession,
  placeCodOrderByApi,
  type IsolatedSession,
} from './commerce-order-fixtures';
import { e2eEnvironment } from './environment';
import { activateStaffAccount, inviteStaff } from './staff-fixtures';
import {
  createCannedResponseByApi,
  deleteCannedResponseByApi,
  deleteCannedResponsesWithSuffix,
  makeRunSuffix,
  SUPPORT_API_HEADERS,
} from './support-desk-canned-api';
import {
  claimSessionByApi,
  closeSessionBestEffort,
  countQueueSessions,
  createCallbackByApi,
  findQueueSession,
  listCallbacksByApi,
  makeUniquePhone,
  markCallbackByApi,
  openAnonymousCustomerChat,
  openLoggedInCustomerChat,
  openStaffSession,
  PRESENCE_PATH,
  readAvailableStaffIds,
  readBox,
  readOwnUserId,
  resolveAllPendingCallbacks,
  screenshotPath,
  sendStaffMessageByApi,
  setPresenceByApi,
  type CustomerChat,
  type StaffSession,
} from './support-desk-live-fixtures';

// SD-UI1 đến SD-UI4: màn chăm sóc khách hàng chạy với backend thật cho mọi route của `support-desk`.
// Dữ liệu dựng bằng API thật: khách gửi tin, nhân viên nhận, yêu cầu gọi lại và đơn hàng.

const EVENTS_PATH_PATTERN = /\/api\/admin\/support\/events/;
const CALLBACKS_URL_PATTERN = /\/api\/admin\/support\/callback-requests\//;
const PRESENCE_URL_PATTERN = /\/api\/admin\/support\/presence/;
// Danh sách "Đã xử lý" của tab Gọi lại chỉ hiện 20 dòng đầu tiên.
const MAX_HANDLED_ROWS = 20;

// Hậu tố riêng của lần chạy. Mọi tin nhắn và số điện thoại spec tạo đều đi cùng hậu tố hoặc số riêng, nên dòng của case tìm được chính xác.
const runSuffix = makeRunSuffix();

function presenceSwitch(page: Page) {
  return page.getByRole('switch', { name: 'Sẵn sàng', exact: true });
}

function soundSwitch(page: Page) {
  return page.getByRole('switch', { name: 'Âm báo', exact: true });
}

function waitingTab(page: Page) {
  return page.getByRole('tab', { name: /^Đang chờ/ });
}

function chatComposer(page: Page) {
  return page.getByRole('combobox', { name: 'Nội dung tin nhắn' });
}

// Dòng phiên là một nút với tên truy cập gồm tin đầu và thời gian chờ. Tin đầu chứa dấu riêng của case.
function queueRow(page: Page, marker: string) {
  return page.getByRole('button', { name: marker });
}

// Mở màn chăm sóc khách hàng và chờ cột hàng chờ nạp xong, để các khẳng định sau không gặp trạng thái đang nạp.
async function openDeskPage(page: Page) {
  await page.goto('/admin/conversations');
  await expect(page.getByRole('heading', { level: 1, name: 'Chăm sóc khách hàng' })).toBeVisible();
  await expect(waitingTab(page)).toBeVisible();
  await expect(page.getByText('Đang nạp hàng chờ...')).toHaveCount(0);
}

// Chuẩn bị chuỗi hiển thị theo cùng quy tắc của màn: `formatVnd` dùng `Intl.NumberFormat('vi-VN')`.
function formatVnd(amount: number): string {
  return `${new Intl.NumberFormat('vi-VN').format(Math.round(amount))} đ`;
}

test.describe('SD-UI1: màn chăm sóc khách hàng', () => {
  let admin: StaffSession;
  let support: StaffSession;
  let sales: StaffSession;
  let colleague: StaffSession;
  let customer: IsolatedSession;
  // Email nhân viên có hậu tố của lần chạy. Nếu `beforeAll` chạy lại sau một case đỏ (Playwright khởi động worker mới), lời mời không đụng lỗi trùng email.
  const supportEmail = `e2e-sd-support-${runSuffix}@pickory.local`;
  const salesEmail = `e2e-sd-sales-${runSuffix}@pickory.local`;
  const colleagueEmail = `e2e-sd-colleague-${runSuffix}@pickory.local`;
  // Mã đơn `shipping` của khách đã đăng nhập, dựng một lần ở `beforeAll`.
  let customerOrderCode = '';
  // Cờ bật khi `beforeAll` chạy xong. `afterAll` chỉ dọn khi cờ này bật, vì trước đó các phiên chưa đăng nhập.
  let isSetupDone = false;
  // Phiên và khách do case đang chạy tạo. `afterEach` đóng phiên và hủy khách để hàng chờ không đầy dần.
  let createdSessionIds: string[] = [];
  let openCustomerChats: CustomerChat[] = [];
  // Mẫu do case tạo. `afterEach` xóa các mẫu này để danh sách mẫu của case sau không đổi.
  let createdCannedIds: string[] = [];
  // Tên đầy đủ của nhân viên `support` khi mời. Backend phải trả đúng tên này ở nhãn người gửi.
  const supportStaffFullName = 'Support CSKH E2E';
  let sessionCounter = 0;

  test.beforeAll(async ({ browser }) => {
    // Mời bốn nhân viên, dựng đơn hàng và vận đơn qua API thật nên cần nhiều thời gian hơn mặc định.
    test.setTimeout(300000);
    admin = await openStaffSession(browser);
    support = await openStaffSession(browser);
    sales = await openStaffSession(browser);
    colleague = await openStaffSession(browser);
    customer = await openIsolatedSession(browser);
    await loginAdmin(admin.page);
    await inviteStaff(admin.page, {
      email: supportEmail,
      fullName: supportStaffFullName,
      roleCode: 'support',
    });
    await inviteStaff(admin.page, {
      email: salesEmail,
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
    await activateStaffAccount(support.page, supportEmail);
    await activateStaffAccount(sales.page, salesEmail);
    await dismissConsent(support.page);
    await dismissConsent(sales.page);

    // Khách đã seed đặt một đơn COD, rồi admin xác nhận và tạo vận đơn. Đơn sang `shipping` như thật.
    await loginSeededCustomer(customer.page, e2eEnvironment.screensCustomerEmail);
    // Admin cấu hình kho gửi trước, nếu không backend từ chối đơn vì cửa hàng chưa sẵn sàng giao hàng.
    await prepareShipping(admin.page);
    const product = await seedProduct(admin.page);
    customerOrderCode = await placeCodOrderByApi(customer.page, product);
    await confirmAndShipOrderByApi(admin.page, customerOrderCode);

    // Console của các bước dựng dữ liệu không thuộc case nào. Mỗi case bắt đầu với danh sách sạch.
    for (const session of [admin, support, sales, colleague]) session.consoleIssues.length = 0;
    isSetupDone = true;
  });

  test.afterAll(async () => {
    try {
      if (isSetupDone) {
        // Lưới an toàn: xóa mọi mẫu có hậu tố của lần chạy, kể cả mẫu mà `afterEach` chưa kịp xóa.
        await deleteCannedResponsesWithSuffix(admin.page, runSuffix);
        await setPresenceByApi(support.page, 'away');
        await setPresenceByApi(colleague.page, 'away');
      }
    } finally {
      // Đóng context cả khi dọn dữ liệu lỗi, để trình duyệt không bị treo.
      for (const session of [admin, support, sales, colleague]) await session?.context.close();
      await customer?.context.close();
    }
  });

  test.beforeEach(async () => {
    // Case ảnh chụp đổi viewport sang 375px. Mỗi case bắt đầu từ 1280px để cột thông tin khách luôn hiện, không phụ thuộc case chạy trước.
    await support.page.setViewportSize({ width: 1280, height: 800 });
    createdSessionIds = [];
    openCustomerChats = [];
    createdCannedIds = [];
    for (const session of [admin, support, sales, colleague]) {
      session.consoleIssues.length = 0;
      session.allowedFailures.length = 0;
    }
  });

  test.afterEach(async () => {
    // Dọn phiên và khách của case trước khi kiểm console, để lỗi dọn dẹp không che lỗi của case.
    for (const sessionId of createdSessionIds) {
      await closeSessionBestEffort(support.page, sessionId);
      await closeSessionBestEffort(colleague.page, sessionId);
    }
    for (const chat of openCustomerChats) await chat.dispose();
    for (const cannedId of createdCannedIds) {
      // Một lần xóa hỏng không được chặn các lần xóa còn lại và các dòng kiểm console bên dưới.
      try {
        await deleteCannedResponseByApi(admin.page, cannedId);
      } catch {
        // Mẫu còn lại bị `afterAll` xóa theo hậu tố.
      }
    }
    // Mỗi case phải kết thúc với 0 lỗi và 0 cảnh báo trong console của mọi phiên.
    expect(admin.consoleIssues, 'Console của admin phải sạch').toEqual([]);
    expect(support.consoleIssues, 'Console của support phải sạch').toEqual([]);
    expect(sales.consoleIssues, 'Console của sales phải sạch').toEqual([]);
    expect(colleague.consoleIssues, 'Console của colleague phải sạch').toEqual([]);
  });

  interface WaitingSession {
    marker: string;
    sessionId: string;
    conversationId: string;
    customerChat: CustomerChat;
  }

  // Khách gửi tin đầu tiên, backend mở phiên chờ, rồi hàm chờ phiên xuất hiện trong hàng chờ thật của nhân viên.
  async function createWaitingSession(
    options: { loggedIn?: boolean; firstMessage?: (marker: string) => string } = {},
  ): Promise<WaitingSession> {
    sessionCounter += 1;
    const marker = `Hỏi đơn giao tới đâu ${runSuffix} số ${sessionCounter}`;
    const customerChat = options.loggedIn
      ? await openLoggedInCustomerChat(customer.page)
      : await openAnonymousCustomerChat();
    openCustomerChats.push(customerChat);
    await customerChat.send(options.firstMessage ? options.firstMessage(marker) : marker);
    const queued = await findQueueSession(support.page, 'waiting', marker);
    createdSessionIds.push(queued.id);
    return {
      marker,
      sessionId: queued.id,
      conversationId: queued.conversationId,
      customerChat,
    };
  }

  // Phiên do `support` giữ, tức nằm trong tab Của tôi.
  async function createHeldSession(): Promise<WaitingSession> {
    const session = await createWaitingSession();
    await claimSessionByApi(support.page, session.sessionId);
    return session;
  }

  // Mở phiên từ hàng chờ hoặc từ tab Của tôi. Tin đầu của khách hiện trong khung chat nghĩa là khung đã nạp xong.
  async function openSession(page: Page, session: WaitingSession, tab: 'waiting' | 'mine') {
    await openDeskPage(page);
    if (tab === 'mine') await page.getByRole('tab', { name: 'Của tôi', exact: true }).click();
    await queueRow(page, session.marker).click();
    await expect(
      page.locator('.support-chat-message').filter({ hasText: session.marker }),
    ).toBeVisible();
  }

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

  test('hàng chờ hiện dữ liệu backend, badge khớp số phiên, tab Gọi lại có hai nhóm với yêu cầu cần gọi đứng trước', async () => {
    const { page } = support;
    // Chỉ để lại một yêu cầu đang chờ và một yêu cầu đã xử lý. Nhờ vậy hai nhóm của tab Gọi lại đều có dòng.
    await resolveAllPendingCallbacks(page);
    const waiting = await createWaitingSession();
    const held = await createHeldSession();
    const handledPhone = makeUniquePhone();
    await createCallbackByApi(handledPhone, null);
    const handledRecord = (await listCallbacksByApi(page)).find(
      (record) => record.phone === handledPhone,
    );
    expect(handledRecord, 'Yêu cầu vừa tạo phải có trong danh sách của backend').toBeDefined();
    await markCallbackByApi(page, handledRecord!.id, 'done');
    const pendingPhone = makeUniquePhone();
    await createCallbackByApi(pendingPhone, 'Khách cần gọi lại sau 18h.');

    await openDeskPage(page);

    // Badge và số dòng của tab Đang chờ khớp số phiên mà backend trả cho view `waiting`.
    // Đọc API và giao diện trong cùng một vòng, vì phiên có thể đổi giữa hai lần đọc.
    await expect
      .poll(
        async () => {
          const apiCount = await countQueueSessions(page, 'waiting');
          const badgeText = await waitingTab(page).locator('.tab-list-count').textContent();
          const itemCount = await page
            .getByRole('list', { name: 'Phiên đang chờ' })
            .getByRole('listitem')
            .count();
          return badgeText === String(apiCount) && itemCount === apiCount;
        },
        { message: 'Badge và số dòng của tab Đang chờ phải khớp số phiên của backend' },
      )
      .toBe(true);
    await expect(queueRow(page, waiting.marker)).toBeVisible();

    await page.getByRole('tab', { name: 'Gọi lại', exact: true }).click();
    const callbackRegion = page.getByRole('group', { name: 'Yêu cầu gọi lại' });
    const pendingGroup = callbackRegion.getByRole('list', { name: /^Cần gọi lại/ });
    const handledGroup = callbackRegion.getByRole('list', { name: 'Đã xử lý' });
    // Một dòng cần gọi lại. Số dòng đã xử lý khớp danh sách của backend, tối đa 20 dòng.
    const handledCount = (await listCallbacksByApi(page)).filter(
      (record) => record.status !== 'pending',
    ).length;
    await expect(pendingGroup.getByRole('listitem')).toHaveCount(1);
    await expect(handledGroup.getByRole('listitem')).toHaveCount(
      Math.min(handledCount, MAX_HANDLED_ROWS),
    );
    // Số điện thoại là link `tel:`, và dòng cần gọi lại đứng trước nhóm đã xử lý.
    await expect(pendingGroup.getByRole('link', { name: pendingPhone })).toHaveAttribute(
      'href',
      `tel:${pendingPhone}`,
    );
    const pendingGroupBox = await readBox(pendingGroup);
    const handledGroupBox = await readBox(handledGroup);
    expect(pendingGroupBox.y).toBeLessThan(handledGroupBox.y);

    await page.getByRole('tab', { name: 'Của tôi', exact: true }).click();
    const mineList = page.getByRole('list', { name: 'Phiên của tôi' });
    await expect(mineList.getByRole('listitem')).toHaveCount(
      await countQueueSessions(page, 'mine'),
    );
    await expect(mineList.getByText(held.marker)).toBeVisible();
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
    await expect(waitingTab(page)).toBeVisible();
    await expect(soundSwitch(page)).toHaveAttribute('aria-checked', 'false');

    // Bật lại để các case sau bắt đầu từ mặc định.
    await soundSwitch(page).click();
    await expect(soundSwitch(page)).toHaveAttribute('aria-checked', 'true');
  });

  // Hủy request `events` bằng lỗi mạng, không dựng phản hồi nào. Backend không tự làm đứt luồng này.
  function abortEvents(route: Route) {
    return route.abort('failed');
  }

  test('mất kết nối: hiện chữ Mất kết nối khi luồng sự kiện lỗi, và mất chữ khi luồng nối lại được', async () => {
    test.setTimeout(120000);
    const testInfo = test.info();
    const { page } = support;
    // Request `events` bị hủy làm trình duyệt ghi lỗi mạng mỗi lần thử nối.
    // Case chỉ bỏ qua đúng lỗi này (URL của luồng, lỗi mạng), mọi lỗi khác vẫn làm case đỏ.
    support.allowedFailures.push({ urlPattern: EVENTS_PATH_PATTERN, status: 'network-failed' });
    try {
      await openDeskPage(page);
      const lostNotice = page.locator('header.admin-top').getByText('Mất kết nối', { exact: true });
      await expect(lostNotice).toBeHidden();

      // Luồng `events` chỉ đứt khi trình duyệt mất mạng, nên route hủy request để gây lỗi này.
      await page.route(EVENTS_PATH_PATTERN, abortEvents);
      // Tải lại để trang mở luồng mới. Luồng cũ đang mở nên route chặn chưa tác động tới nó.
      await page.reload();
      await expect(waitingTab(page)).toBeVisible();

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
      await page.unroute(EVENTS_PATH_PATTERN, abortEvents);
      await expect(lostNotice).toBeHidden({ timeout: 60000 });
    } finally {
      // Gỡ route cả khi case đỏ giữa chừng, để route hủy không sót sang case sau. Gỡ hai lần không gây lỗi.
      await page.unroute(EVENTS_PATH_PATTERN, abortEvents);
      support.allowedFailures.length = 0;
    }
  });

  // Đổi body của `PUT presence` thành trạng thái không hợp lệ rồi chuyển tới backend thật.
  async function sendInvalidPresence(route: Route) {
    if (route.request().method() !== 'PUT') {
      await route.fallback();
      return;
    }
    await route.continue({ postData: JSON.stringify({ status: 'invalid-status' }) });
  }

  test('PUT trạng thái lỗi: công tắc giữ trạng thái cũ, báo lỗi ngắn và thanh trên cùng không tràn ngang ở 375px', async () => {
    const testInfo = test.info();
    const { page } = support;
    // Backend trả 400 cho `PUT` bị sửa body. Case chỉ bỏ qua đúng lỗi này, mọi lỗi khác vẫn làm case đỏ.
    support.allowedFailures.push({ urlPattern: PRESENCE_URL_PATTERN, status: 400 });
    try {
      await page.setViewportSize({ width: 375, height: 812 });
      await openDeskPage(page);
      await expect(presenceSwitch(page)).toHaveAttribute('aria-checked', 'false');

      // Giao diện không gửi được trạng thái sai, nên route sửa body để backend trả 400.
      await page.route(PRESENCE_URL_PATTERN, sendInvalidPresence);
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
      // Backend từ chối lệnh, nên nhân viên vẫn Tạm vắng.
      const staffId = await readOwnUserId(page);
      expect(await readAvailableStaffIds(page)).not.toContain(staffId);
    } finally {
      // `unroute` so khớp theo tham chiếu hàm, nên phải truyền đúng hàm đã đăng ký.
      await page.unroute(PRESENCE_URL_PATTERN, sendInvalidPresence);
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
      await createWaitingSession();
      await resolveAllPendingCallbacks(page);
      const callbackPhone = makeUniquePhone();
      await createCallbackByApi(callbackPhone, null);
      await page.setViewportSize({ width: viewport.width, height: viewport.height });
      await openDeskPage(page);
      await expect
        .poll(
          async () => {
            const apiCount = await countQueueSessions(page, 'waiting');
            const badgeText = await waitingTab(page).locator('.tab-list-count').textContent();
            return badgeText === String(apiCount);
          },
          { message: 'Badge của tab Đang chờ phải khớp số phiên của backend' },
        )
        .toBe(true);
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
      await expect(page.getByText(callbackPhone)).toBeVisible();
      await expectNoHorizontalOverflow(page);
      await page.screenshot({
        path: screenshotPath(testInfo, `workspace-${viewport.name}-callbacks.png`),
        fullPage: true,
      });
    });
  }

  // SD-UI2: khung chat có phiên, tin và nhận phiên đều đi qua backend thật.
  // Case mẫu trả lời nhanh nằm ở file `support-desk-canned-chat.spec.ts`.
  test('SD-UI2: mở phiên từ hàng chờ hiện lịch sử tin, nút Nhận và ô soạn tắt kèm chú thích', async () => {
    const { page } = support;
    const session = await createWaitingSession();
    await openSession(page, session, 'waiting');

    // Nhãn người gửi theo vai trò. Phiên chờ chỉ có tin của khách, nên nhãn nhân viên được kiểm ở case gửi tin bên dưới.
    const chatRegion = page.locator('section', {
      has: page.getByRole('heading', { name: 'Khung chat' }),
    });
    await expect(chatRegion.getByText('Khách', { exact: true })).toBeVisible();
    // Nút Nhận hiện ở đầu khung chat. Ô soạn tắt với chú thích nêu rõ lý do.
    await expect(page.getByRole('button', { name: 'Nhận', exact: true })).toBeVisible();
    await expect(chatComposer(page)).toBeDisabled();
    await expect(page.getByText('Nhận hội thoại để trả lời.')).toBeVisible();
  });

  test('SD-UI2: bấm Nhận gọi POST assignment và ô soạn bật ngay', async () => {
    const { page } = support;
    const session = await createWaitingSession();
    await openSession(page, session, 'waiting');

    await page.getByRole('button', { name: 'Nhận', exact: true }).click();
    await expect(chatComposer(page)).toBeEnabled();
    // Nhận xong thì nút Nhận biến mất khỏi khung chat.
    await expect(page.getByRole('button', { name: 'Nhận', exact: true })).toHaveCount(0);
    // Backend đã ghi nhận: phiên nằm trong view `mine` của người nhận.
    await findQueueSession(page, 'mine', session.marker);
  });

  test('SD-UI2: Enter gửi tin qua POST messages và dọn ô soạn', async () => {
    const { page } = support;
    const session = await createWaitingSession();
    await openSession(page, session, 'waiting');
    await page.getByRole('button', { name: 'Nhận', exact: true }).click();
    await expect(chatComposer(page)).toBeEnabled();

    const staffText = `Chào bạn, mình kiểm đơn ngay nhé ${runSuffix}.`;
    await chatComposer(page).fill(staffText);
    await page.keyboard.press('Enter');
    // Gửi thành công thì ô sạch và tin hiện trong lịch sử với nhãn nhân viên mà backend trả.
    await expect(chatComposer(page)).toHaveValue('');
    const sentMessage = page.locator('.support-chat-message').filter({ hasText: staffText });
    await expect(sentMessage).toBeVisible();
    const history = await page.request.get(
      `/api/admin/support/conversations/${session.conversationId}/messages`,
    );
    expect(history.status(), await history.text()).toBe(200);
    const historyBody: {
      data: { items: { role: string; body: string; staffDisplayName: string | null }[] };
    } = await history.json();
    const stored = historyBody.data.items.find((item) => item.body === staffText);
    expect(stored?.role).toBe('staff');
    // Nhân viên được mời với `fullName` này, nên backend phải trả đúng tên đó.
    expect(stored?.staffDisplayName).toBe(supportStaffFullName);
    await expect(sentMessage.getByText(supportStaffFullName, { exact: true })).toBeVisible();
  });

  test('SD-UI2: gõ / chọn mẫu có {ma_don}, Enter chèn mẫu với mã đơn đầu tiên của khách', async () => {
    const { page } = support;
    // Mẫu dùng chung do admin tạo qua API, có biến `{ma_don}`. Phím tắt có hậu tố để không đụng mẫu khác.
    const shortcut = `kiemdon-${runSuffix}`;
    await createCannedResponseByApi(admin.page, {
      shortcut,
      body: 'Mình kiểm đơn {ma_don} giúp bạn nhé.',
      shared: true,
    });
    try {
      // Khách đã đăng nhập có đơn `shipping`, nên màn thay `{ma_don}` bằng mã đơn của khách. Nhân viên giữ phiên trước, rồi mở từ tab Của tôi.
      const session = await createWaitingSession({ loggedIn: true });
      await claimSessionByApi(page, session.sessionId);
      await openSession(page, session, 'mine');
      await expect(chatComposer(page)).toBeEnabled();
      // Chờ bảng thông tin khách nạp xong, vì biến lấy từ dữ liệu của bảng này.
      await expect(
        page.locator('.support-customer-panel').getByRole('link', { name: customerOrderCode }),
      ).toBeVisible();
      // Biến `{ma_don}` lấy đơn đầu tiên của danh sách, nên đơn đó phải là đơn dựng ở `beforeAll`.
      const panelOrders = await readCustomerPanelOrders(session.sessionId);
      expect(
        panelOrders[0]?.code,
        'Đơn đầu của bảng khách phải là đơn dựng ở beforeAll, vì biến lấy đơn đầu',
      ).toBe(customerOrderCode);

      await chatComposer(page).fill(`/${shortcut}`);
      await expect(page.getByRole('option', { name: `/${shortcut}` })).toBeVisible();
      await page.keyboard.press('Enter');

      await expect(chatComposer(page)).toHaveValue(
        `Mình kiểm đơn ${customerOrderCode} giúp bạn nhé.`,
      );
    } finally {
      await deleteCannedResponsesWithSuffix(admin.page, runSuffix);
    }
  });
  // Lịch sử dài 50 tin: 18 tin đầu xen kẽ khách và nhân viên (9 tin khách), các tin sau do nhân viên gửi.
  // Backend giới hạn khách 10 tin mỗi phút.
  const LONG_HISTORY_MESSAGE_COUNT = 50;
  const CUSTOMER_MESSAGE_COUNT = 9;

  function longHistoryText(index: number, marker: string): string {
    const base = `Tin nhắn thứ ${index} trong hội thoại dài để kiểm tra vùng cuộn của khung chat.`;
    return index === 1 ? `${base} ${marker}` : base;
  }

  for (const viewport of [
    { name: '1280', width: 1280, height: 800 },
    { name: '375', width: 375, height: 812 },
  ]) {
    test(`SD-UI2: ảnh chụp khung chat ${viewport.name}px không tràn ngang và cuộn được với 50 tin`, async () => {
      test.setTimeout(90000);
      const testInfo = test.info();
      const { page } = support;
      await page.setViewportSize({ width: viewport.width, height: viewport.height });

      // Dựng lịch sử 50 tin trong phiên thật. Tin 1 của khách mở phiên, rồi nhân viên nhận để gửi tin xen kẽ.
      sessionCounter += 1;
      const marker = `Lịch sử dài ${runSuffix} số ${sessionCounter}`;
      const customerChat = await openAnonymousCustomerChat();
      openCustomerChats.push(customerChat);
      await customerChat.send(longHistoryText(1, marker));
      const queued = await findQueueSession(page, 'waiting', marker);
      createdSessionIds.push(queued.id);
      await claimSessionByApi(page, queued.id);
      for (let index = 2; index <= LONG_HISTORY_MESSAGE_COUNT; index++) {
        const isCustomerTurn = index % 2 === 1 && (index + 1) / 2 <= CUSTOMER_MESSAGE_COUNT;
        if (isCustomerTurn) await customerChat.send(longHistoryText(index, marker));
        else await sendStaffMessageByApi(page, queued.id, longHistoryText(index, marker));
      }

      // Năm mẫu dùng chung làm danh sách mẫu có nhiều dòng, như khi nhân viên dùng thật.
      // Tạo trước khi mở trang, vì khung soạn tin đọc danh sách mẫu một lần khi trang nạp.
      for (let index = 1; index <= 5; index++) {
        const record = await createCannedResponseByApi(admin.page, {
          shortcut: `dai-${index}-${runSuffix}`,
          body: `Mẫu số ${index} cho danh sách dài.`,
          shared: true,
        });
        createdCannedIds.push(record.id);
      }

      await openDeskPage(page);
      await page.getByRole('tab', { name: 'Của tôi', exact: true }).click();
      await queueRow(page, marker).click();

      const lastMessageLocator = page
        .locator('.support-chat-message')
        .filter({ hasText: 'Tin nhắn thứ 50' });
      await expect(lastMessageLocator).toBeVisible();

      const scrollRegion = page.locator('.support-chat-scroll');
      // Đọc scrollTop bằng expect.poll để đảm bảo useEffect cuộn đáy đã chạy xong.
      await expect
        .poll(async () => scrollRegion.evaluate((el) => el.scrollTop), {
          message: 'Vùng tin phải cuộn xuống đáy khi có 50 tin',
        })
        .toBeGreaterThan(0);

      // Phiên đã do nhân viên giữ, nên ô soạn bật ngay.
      await expect(chatComposer(page)).toBeEnabled();

      // Mở danh sách mẫu: ô soạn cao thêm, kiểm tra tin 50 không bị cắt nhờ ResizeObserver.
      await chatComposer(page).fill('/');
      const cannedListbox = page.getByRole('listbox');
      await expect(cannedListbox).toBeVisible();
      // Danh sách phải có ít nhất 5 dòng, nếu không số đo bên dưới đo một danh sách rỗng.
      await expect
        .poll(() => cannedListbox.getByRole('option').count(), {
          message: 'Danh sách mẫu phải có ít nhất 5 dòng',
        })
        .toBeGreaterThanOrEqual(5);
      await expectNoHorizontalOverflow(page);

      // Ở 375px (xếp dọc): cuộn khung chat vào tầm nhìn trước khi đo.
      const chatCard = page.locator('.support-chat-open');
      await chatCard.scrollIntoViewIfNeeded();

      // Tin 50 phải nằm trọn trong vùng cuộn: cạnh dưới tin không vượt quá cạnh dưới vùng.
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
      // Kiểm tra ô soạn nằm trong khung nhìn theo chiều dọc sau scrollIntoViewIfNeeded.
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

  // SD-UI3: thông tin khách đọc từ `GET sessions/{id}/customer` thật. Khách đã đăng nhập có đơn `shipping` dựng ở `beforeAll`.
  async function readCustomerPanelOrders(sessionId: string) {
    const response = await support.page.request.get(
      `/api/admin/support/sessions/${sessionId}/customer`,
    );
    expect(response.status(), await response.text()).toBe(200);
    const body: {
      data: { orders: { code: string; total: number; itemCount: number }[] };
    } = await response.json();
    return body.data.orders;
  }

  async function readCustomerProfile() {
    const response = await customer.page.request.get('/api/me');
    expect(response.status(), await response.text()).toBe(200);
    const body: { data: { name: string; email: string } } = await response.json();
    return body.data;
  }

  test('SD-UI3: cột thông tin khách hiển thị dữ liệu khách đã đăng nhập và link đơn hàng', async () => {
    const { page } = support;
    const session = await createWaitingSession({ loggedIn: true });
    await openSession(page, session, 'waiting');

    // Cột thông tin khách hiển thị hồ sơ của khách đã đăng nhập.
    const profile = await readCustomerProfile();
    const customerPanel = page.locator('.support-customer-panel');
    await expect(customerPanel.getByText(profile.name, { exact: true })).toBeVisible();
    await expect(customerPanel.getByText(profile.email, { exact: true })).toBeVisible();
    await expect(customerPanel.getByText('Khách vãng lai')).toHaveCount(0);

    // Đơn hàng có link tới trang quản trị đơn, trạng thái, tổng tiền và số sản phẩm của backend.
    const orders = await readCustomerPanelOrders(session.sessionId);
    const order = orders.find((candidate) => candidate.code === customerOrderCode);
    expect(order, 'Bảng khách phải có đơn vừa đặt').toBeDefined();
    const orderLink = customerPanel.getByRole('link', { name: customerOrderCode });
    await expect(orderLink).toBeVisible();
    await expect(orderLink).toHaveAttribute('href', `/admin/orders/${customerOrderCode}`);
    // Khách có thể có nhiều đơn `shipping` do spec khác đặt, nên các giá trị được tìm trong dòng của đơn vừa đặt.
    const orderRow = customerPanel
      .locator('.support-customer-order-item')
      .filter({ hasText: customerOrderCode });
    await expect(orderRow.getByText('Đang giao hàng')).toBeVisible();
    await expect(orderRow.getByText(formatVnd(order!.total))).toBeVisible();
    await expect(orderRow.getByText(`${order!.itemCount} sản phẩm`)).toBeVisible();
  });

  test('SD-UI3: mở phiên từ tab Của tôi hiện thông tin khách vãng lai Chưa có thông tin', async () => {
    const { page } = support;
    const session = await createHeldSession();
    await openSession(page, session, 'mine');

    const customerPanel = page.locator('.support-customer-panel');
    // Hiển thị nhãn Khách vãng lai, Chưa có thông tin và thông báo không có đơn hàng.
    await expect(customerPanel.getByText('Khách vãng lai')).toBeVisible();
    await expect(customerPanel.getByText('Chưa có thông tin')).toHaveCount(2);
    await expect(customerPanel.getByText('Khách chưa có đơn hàng.')).toBeVisible();
  });

  // Nhãn đồng nghiệp trong hộp thoại chuyển, theo cùng quy tắc của màn: tên hiển thị, hoặc 4 ký tự cuối của mã.
  async function readColleagueLabel(colleagueUserId: string): Promise<string> {
    const response = await support.page.request.get(PRESENCE_PATH);
    expect(response.status(), await response.text()).toBe(200);
    const body: { data: { staffId: string; displayName: string | null }[] } = await response.json();
    const entry = body.data.find((candidate) => candidate.staffId === colleagueUserId);
    expect(entry, 'Đồng nghiệp phải đang sẵn sàng').toBeDefined();
    return entry!.displayName?.trim() || `Nhân viên ${colleagueUserId.slice(-4)}`;
  }

  test('SD-UI3: hộp thoại chuyển chỉ hiện đồng nghiệp sẵn sàng, gửi PUT với staffId và note', async () => {
    const { page } = support;
    const session = await createHeldSession();
    const colleagueUserId = await readOwnUserId(colleague.page);
    await setPresenceByApi(colleague.page, 'available');
    try {
      await openSession(page, session, 'mine');

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

      const staffLabel = await readColleagueLabel(colleagueUserId);
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
      const putBody: { staffId: string; note: string | null } = putRequest.postDataJSON();
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
      // Backend đã chuyển phiên: phiên nằm trong view `mine` của đồng nghiệp.
      await findQueueSession(colleague.page, 'mine', session.marker);
    } finally {
      // Dọn trạng thái của colleague về away.
      await setPresenceByApi(colleague.page, 'away');
    }
  });

  test('SD-UI3: bấm Kết thúc hỏi xác nhận trước, gửi PATCH closed và ô soạn tắt', async () => {
    const { page } = support;
    const session = await createHeldSession();
    const patchRequests: string[] = [];
    page.on('request', (req) => {
      if (req.url().includes('/api/admin/support/sessions/') && req.method() === 'PATCH') {
        patchRequests.push(req.url());
      }
    });

    await openSession(page, session, 'mine');

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
    const patchBody: { status: string } = patchRequest.postDataJSON();
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
    const session = await createHeldSession();
    await openSession(page, session, 'mine');

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

    const session = await createWaitingSession({ loggedIn: true });
    await openSession(page, session, 'waiting');

    const toggleButton = page.getByRole('button', { name: 'Thông tin khách' });
    await expect(toggleButton).toBeVisible();
    await expect(toggleButton).toHaveAttribute('aria-expanded', 'false');

    const profile = await readCustomerProfile();
    const customerPanel = page.locator('.support-customer-panel');
    const customerName = customerPanel.getByText(profile.name, { exact: true });
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
    await setPresenceByApi(colleague.page, 'available');
    try {
      // 1. Chụp cột thông tin khách ở 1280px.
      await page.setViewportSize({ width: 1280, height: 800 });
      const waiting = await createWaitingSession({ loggedIn: true });
      await openSession(page, waiting, 'waiting');
      await expectNoHorizontalOverflow(page);

      const customerPanel1280 = page.locator('.support-customer-panel');
      const panelBox1280 = await readBox(customerPanel1280);
      // Đỉnh lưới của ba cột ở 1280px phải bắt đầu tại y=159 (lệch không quá 2px).
      expect(Math.abs(panelBox1280.y - 159)).toBeLessThanOrEqual(2);

      await page.screenshot({
        path: screenshotPath(testInfo, 'customer-panel-1280.png'),
        fullPage: true,
      });

      // 2. Chụp hộp thoại chuyển ở 1280px và 375px, trên một phiên do nhân viên giữ.
      const held = await createHeldSession();
      await openSession(page, held, 'mine');

      await page.getByRole('button', { name: 'Chuyển', exact: true }).click();
      const transferDialog = page.getByRole('dialog', { name: 'Chuyển hội thoại' });
      await expect(transferDialog).toBeVisible();

      // Chọn đồng nghiệp và nhập ghi chú nội bộ trước khi chụp ảnh theo yêu cầu review.
      const colleagueUserId = await readOwnUserId(colleague.page);
      const staffLabel = await readColleagueLabel(colleagueUserId);
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
    } finally {
      await setPresenceByApi(colleague.page, 'away');
    }
  });

  // SD-UI4: tab Gọi lại đọc yêu cầu thật do khách ẩn danh để lại qua API.
  // Dòng "Đã xóa số" (E2E không chạy được bước xóa số sau 30 ngày), sự kiện `session.reminded` và `callback.created` chỉ có unit test.
  interface CallbackDesk {
    page: Page;
    consoleIssues: string[];
    // Body của mọi request PATCH mà giao diện đã gửi, theo bản gốc trước khi route sửa.
    patchBodies: unknown[];
    pendingPhone: string;
    pendingId: string;
  }

  async function openCallbackDesk(
    options: { patchFails?: boolean; width?: number; height?: number; longNote?: boolean } = {},
  ): Promise<CallbackDesk> {
    // Chỉ để lại yêu cầu của case ở nhóm Cần gọi lại, và một yêu cầu đã xử lý ở nhóm Đã xử lý.
    await resolveAllPendingCallbacks(support.page);
    const handledPhone = makeUniquePhone();
    await createCallbackByApi(handledPhone, null);
    const pendingPhone = makeUniquePhone();
    // Ghi chú một chuỗi dài không dấu cách, để đo việc ngắt dòng trong cột hẹp.
    await createCallbackByApi(
      pendingPhone,
      options.longNote ? 'GoiLaiSauMuoiTam'.repeat(12) : 'Khách cần gọi lại sau 18h.',
    );
    const records = await listCallbacksByApi(support.page);
    const handledRecord = records.find((record) => record.phone === handledPhone);
    expect(handledRecord, 'Yêu cầu đã xử lý vừa tạo phải có trong danh sách').toBeDefined();
    await markCallbackByApi(support.page, handledRecord!.id, 'done');
    const pendingRecord = records.find((record) => record.phone === pendingPhone);
    expect(pendingRecord, 'Yêu cầu đang chờ vừa tạo phải có trong danh sách').toBeDefined();
    const pendingId = pendingRecord!.id;

    const page = await support.context.newPage();
    // Case lỗi chủ động gây lỗi 400 cho PATCH. Chỉ đúng lỗi tải đó được bỏ qua, mọi dòng console khác vẫn tính.
    const allowedFailures: AllowedLoadFailure[] = options.patchFails
      ? [{ urlPattern: CALLBACKS_URL_PATTERN, status: 400 }]
      : [];
    const consoleIssues = collectConsoleIssues(page, allowedFailures);
    await page.setViewportSize({ width: options.width ?? 1280, height: options.height ?? 800 });
    const patchBodies: unknown[] = [];
    page.on('request', (request) => {
      if (request.method() === 'PATCH' && CALLBACKS_URL_PATTERN.test(request.url())) {
        patchBodies.push(request.postDataJSON());
      }
    });
    return { page, consoleIssues, patchBodies, pendingPhone, pendingId };
  }

  async function openCallbacksTab(page: Page) {
    await openDeskPage(page);
    await page.getByRole('tab', { name: 'Gọi lại', exact: true }).click();
    await expect(page.getByRole('heading', { level: 3, name: /^Cần gọi lại/ })).toBeVisible();
  }

  // Đổi body của `PATCH callback-requests` thành trạng thái không hợp lệ rồi chuyển tới backend thật. Giao diện chỉ gửi `done` hoặc `unreachable`, nên case phải sửa request để backend trả 400.
  async function sendInvalidCallbackStatus(route: Route) {
    if (route.request().method() !== 'PATCH') {
      await route.fallback();
      return;
    }
    await route.continue({ postData: JSON.stringify({ status: 'invalid-status' }) });
  }

  // Câu lỗi mà backend trả cho đúng body không hợp lệ, lấy bằng lời gọi API trực tiếp để case không viết cứng câu này.
  async function readBackendCallbackError(callbackId: string): Promise<string> {
    const response = await support.page.request.patch(
      `/api/admin/support/callback-requests/${callbackId}`,
      { headers: SUPPORT_API_HEADERS, data: { status: 'invalid-status' } },
    );
    expect(response.status(), await response.text()).toBe(400);
    const body: { error: { message: string; details?: { message: string }[] } } =
      await response.json();
    // Giao diện hiện câu của lỗi gắn với trường khi có, nếu không hiện câu chung của lỗi.
    return body.error.details?.[0]?.message ?? body.error.message;
  }

  test('SD-UI4: Đã gọi gửi PATCH body done, dòng sang nhóm Đã xử lý và tiêu điểm về tiêu đề nhóm', async () => {
    const desk = await openCallbackDesk();
    const { page, pendingPhone } = desk;
    try {
      await openCallbacksTab(page);
      const pendingGroup = page.getByRole('list', { name: /^Cần gọi lại/ });
      const handledGroup = page.getByRole('list', { name: 'Đã xử lý' });
      const handledBefore = (await listCallbacksByApi(support.page)).filter(
        (record) => record.status !== 'pending',
      ).length;
      await expect(pendingGroup.getByRole('listitem')).toHaveCount(1);
      await expect(handledGroup.getByRole('listitem')).toHaveCount(
        Math.min(handledBefore, MAX_HANDLED_ROWS),
      );
      // Số điện thoại là link `tel:`.
      await expect(pendingGroup.getByRole('link', { name: pendingPhone })).toHaveAttribute(
        'href',
        `tel:${pendingPhone}`,
      );
      // Dòng cần gọi lại đứng trước dòng đã xử lý trong cột.
      const pendingBox = await readBox(pendingGroup);
      const handledBox = await readBox(handledGroup);
      expect(pendingBox.y).toBeLessThan(handledBox.y);

      const patchRequest = page.waitForRequest(
        (request) =>
          request.method() === 'PATCH' &&
          request.url().includes('/api/admin/support/callback-requests/'),
      );
      await page.getByRole('button', { name: `Đánh dấu đã gọi: ${pendingPhone}` }).click();
      expect((await patchRequest).postDataJSON()).toEqual({ status: 'done' });

      // Nhóm Cần gọi lại biến mất, dòng vừa đánh dấu nằm ở nhóm Đã xử lý và có nhãn Đã gọi.
      await expect(page.getByRole('heading', { level: 3, name: /^Cần gọi lại/ })).toHaveCount(0);
      await expect(handledGroup.getByRole('listitem')).toHaveCount(
        Math.min(handledBefore + 1, MAX_HANDLED_ROWS),
      );
      await expect(
        handledGroup
          .getByRole('listitem')
          .filter({ hasText: pendingPhone })
          .getByText('Đã gọi', { exact: true }),
      ).toBeVisible();
      await expect(page.getByText('Không có yêu cầu nào cần gọi lại.')).toBeVisible();
      await expect(page.getByRole('status').filter({ hasText: 'Đã đánh dấu' })).toHaveText(
        `Đã đánh dấu yêu cầu ${pendingPhone} là Đã gọi.`,
      );
      await expect(page.getByRole('heading', { level: 3, name: 'Đã xử lý' })).toBeFocused();
      expect(desk.patchBodies).toEqual([{ status: 'done' }]);
      // Backend đã ghi trạng thái.
      const stored = (await listCallbacksByApi(support.page)).find(
        (record) => record.id === desk.pendingId,
      );
      expect(stored?.status).toBe('done');
      expect(desk.consoleIssues, 'Console của trang Gọi lại phải sạch').toEqual([]);
    } finally {
      await page.close();
    }
  });

  test('SD-UI4: Không liên lạc được gửi PATCH body unreachable và hiện nhãn tương ứng', async () => {
    const desk = await openCallbackDesk();
    const { page, pendingPhone } = desk;
    try {
      await openCallbacksTab(page);

      await page
        .getByRole('button', { name: `Đánh dấu không liên lạc được: ${pendingPhone}` })
        .click();

      const handledGroup = page.getByRole('list', { name: 'Đã xử lý' });
      await expect(
        handledGroup
          .getByRole('listitem')
          .filter({ hasText: pendingPhone })
          .getByText('Không liên lạc được', { exact: true }),
      ).toBeVisible();
      expect(desk.patchBodies).toEqual([{ status: 'unreachable' }]);
      const stored = (await listCallbacksByApi(support.page)).find(
        (record) => record.id === desk.pendingId,
      );
      expect(stored?.status).toBe('unreachable');
      expect(desk.consoleIssues, 'Console của trang Gọi lại phải sạch').toEqual([]);
    } finally {
      await page.close();
    }
  });

  test('SD-UI4: bàn phím tới link và hai nút theo thứ tự, Enter trên nút Đã gọi lưu và trả tiêu điểm về tiêu đề', async () => {
    const desk = await openCallbackDesk();
    const { page, pendingPhone } = desk;
    try {
      await openCallbacksTab(page);
      await page.getByRole('tab', { name: 'Gọi lại', exact: true }).focus();

      await page.keyboard.press('Tab');
      await expect(page.getByRole('link', { name: pendingPhone })).toBeFocused();
      await page.keyboard.press('Tab');
      await expect(
        page.getByRole('button', { name: `Đánh dấu đã gọi: ${pendingPhone}` }),
      ).toBeFocused();
      await page.keyboard.press('Tab');
      await expect(
        page.getByRole('button', { name: `Đánh dấu không liên lạc được: ${pendingPhone}` }),
      ).toBeFocused();
      await page.keyboard.press('Shift+Tab');
      await page.keyboard.press('Enter');

      await expect(page.getByRole('heading', { level: 3, name: 'Đã xử lý' })).toBeFocused();
      expect(desk.patchBodies).toEqual([{ status: 'done' }]);
      expect(desk.consoleIssues, 'Console của trang Gọi lại phải sạch').toEqual([]);
    } finally {
      await page.close();
    }
  });

  test('SD-UI4: PATCH lỗi hiện thông báo của API dưới dòng, dòng giữ ở nhóm Cần gọi lại và nút bật lại', async () => {
    const desk = await openCallbackDesk({ patchFails: true });
    const { page, pendingPhone } = desk;
    try {
      const backendMessage = await readBackendCallbackError(desk.pendingId);
      await openCallbacksTab(page);
      // Backend trả 400 cho `PATCH` bị sửa body. Nhánh lỗi 5xx do unit test `callback-list.test.tsx` kiểm ("lỗi hiện thông báo của API ngay dưới dòng...").
      await page.route(CALLBACKS_URL_PATTERN, sendInvalidCallbackStatus);

      await page.getByRole('button', { name: `Đánh dấu đã gọi: ${pendingPhone}` }).click();

      const pendingGroup = page.getByRole('list', { name: /^Cần gọi lại/ });
      await expect(pendingGroup.getByRole('alert')).toContainText(backendMessage);
      await expect(pendingGroup.getByRole('listitem')).toHaveCount(1);
      await expect(
        page.getByRole('button', { name: `Đánh dấu đã gọi: ${pendingPhone}` }),
      ).toBeEnabled();
      await expect(
        page.getByRole('button', { name: `Đánh dấu không liên lạc được: ${pendingPhone}` }),
      ).toBeEnabled();
      expect(desk.consoleIssues, 'Console của trang Gọi lại phải sạch').toEqual([]);
    } finally {
      await page.close();
    }
  });

  for (const viewport of [
    { name: '1280', width: 1280, height: 800 },
    { name: '375', width: 375, height: 812 },
  ]) {
    test(`SD-UI4: ảnh chụp tab Gọi lại ${viewport.name}px không tràn ngang, ghi chú dài ngắt dòng`, async () => {
      const testInfo = test.info();
      const desk = await openCallbackDesk({
        width: viewport.width,
        height: viewport.height,
        longNote: true,
      });
      const { page } = desk;
      try {
        await openCallbacksTab(page);
        await expectNoHorizontalOverflow(page);

        const queueBox = await readBox(page.getByRole('region', { name: 'Hàng chờ' }));
        const pendingRow = page
          .getByRole('list', { name: /^Cần gọi lại/ })
          .getByRole('listitem')
          .first();
        const rowBox = await readBox(pendingRow);
        const noteBox = await readBox(pendingRow.locator('.support-callback-note'));
        const phoneBox = await readBox(pendingRow.getByRole('link'));
        const doneButtonBox = await readBox(pendingRow.getByRole('button', { name: /đã gọi/ }));
        const unreachableButtonBox = await readBox(
          pendingRow.getByRole('button', { name: /không liên lạc được/ }),
        );
        const noteTextWidth = await pendingRow
          .locator('.support-callback-note')
          .evaluate((element) => ({
            scrollWidth: element.scrollWidth,
            clientWidth: element.clientWidth,
          }));
        const measurements = {
          viewport: { width: viewport.width, height: viewport.height },
          queue: queueBox,
          row: rowBox,
          note: noteBox,
          noteTextWidth,
          phone: phoneBox,
          doneButton: doneButtonBox,
          unreachableButton: unreachableButtonBox,
        };
        await testInfo.attach(`measurements-callbacks-${viewport.name}`, {
          body: JSON.stringify(measurements, null, 2),
          contentType: 'application/json',
        });
        const tolerance = 0.5;
        const queueRight = queueBox.x + queueBox.width;
        // Dòng, ghi chú và hai nút đều nằm trong cột hàng chờ, và cột nằm trong khung nhìn.
        expect(queueRight).toBeLessThanOrEqual(viewport.width + tolerance);
        for (const box of [rowBox, noteBox, phoneBox, doneButtonBox, unreachableButtonBox]) {
          expect(box.x).toBeGreaterThanOrEqual(queueBox.x - tolerance);
          expect(box.x + box.width).toBeLessThanOrEqual(queueRight + tolerance);
        }
        // Hộp của ghi chú luôn vừa cột, kể cả khi chữ tràn ra ngoài hộp. `scrollWidth` lớn hơn `clientWidth` nghĩa là chữ tràn.
        expect(noteTextWidth.scrollWidth).toBeLessThanOrEqual(noteTextWidth.clientWidth);
        await page.screenshot({
          path: screenshotPath(testInfo, `callbacks-${viewport.name}-groups.png`),
          fullPage: true,
        });
        expect(desk.consoleIssues, 'Console của trang Gọi lại phải sạch').toEqual([]);
      } finally {
        await page.close();
      }
    });

    test(`SD-UI4: ảnh chụp lỗi của dòng ${viewport.name}px không tràn ngang`, async () => {
      const testInfo = test.info();
      const desk = await openCallbackDesk({
        width: viewport.width,
        height: viewport.height,
        patchFails: true,
      });
      const { page, pendingPhone } = desk;
      try {
        const backendMessage = await readBackendCallbackError(desk.pendingId);
        await openCallbacksTab(page);
        // Backend trả 400 cho `PATCH` bị sửa body, vì giao diện chỉ gửi trạng thái hợp lệ.
        await page.route(CALLBACKS_URL_PATTERN, sendInvalidCallbackStatus);
        await page.getByRole('button', { name: `Đánh dấu đã gọi: ${pendingPhone}` }).click();
        const pendingRow = page
          .getByRole('list', { name: /^Cần gọi lại/ })
          .getByRole('listitem')
          .first();
        const alert = pendingRow.getByRole('alert');
        await expect(alert).toContainText(backendMessage);
        await expectNoHorizontalOverflow(page);

        const rowBox = await readBox(pendingRow);
        const alertBox = await readBox(alert);
        await testInfo.attach(`measurements-callback-error-${viewport.name}`, {
          body: JSON.stringify({ row: rowBox, alert: alertBox }, null, 2),
          contentType: 'application/json',
        });
        expect(alertBox.x + alertBox.width).toBeLessThanOrEqual(rowBox.x + rowBox.width + 0.5);
        expect(alertBox.y).toBeGreaterThanOrEqual(rowBox.y);
        await page.screenshot({
          path: screenshotPath(testInfo, `callbacks-${viewport.name}-error.png`),
          fullPage: true,
        });
        expect(desk.consoleIssues, 'Console của trang Gọi lại phải sạch').toEqual([]);
      } finally {
        await page.close();
      }
    });
  }
});
