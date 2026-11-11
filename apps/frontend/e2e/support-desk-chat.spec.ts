import { expect, test, type Locator, type Page, type Response } from '@playwright/test';
import { dismissConsent, loginAdmin } from './catalog-admin-flow';
import { activateStaffAccount, inviteStaff } from './staff-fixtures';
import {
  createCannedResponseByApi,
  deleteCannedResponsesWithSuffix,
  makeRunSuffix,
} from './support-desk-canned-api';
import {
  closeSessionsByMarkerBestEffort,
  countQueueSessions,
  findQueueSession,
  openStaffSession,
  readAvailableStaffIds,
  readOwnUserId,
  readSupportSettingsByApi,
  saveSupportSettingsByApi,
  setPresenceByApi,
  type StaffSession,
  type SupportSettingsBody,
} from './support-desk-live-fixtures';

// SD-E2E1: luồng CSKH trong giờ, từ tin đầu của khách tới phiên chờ mới sau khi kết thúc phiên.
// Spec chạy với backend thật. Spec không chặn và không giả route support-desk nào, nên mọi sự kiện đi qua SSE thật.

// Hạn của SSE phía nhân viên và phía khách theo tiêu chí nghiệm thu. Spec đo bằng timeout của `expect`, không chờ cố định.
const STAFF_SEES_NEW_SESSION_MS = 3000;
const CUSTOMER_SEES_STAFF_MESSAGE_MS = 2000;

// Hai phút cuối của ngày theo giờ Việt Nam. `HH:mm` lớn nhất là 23:59, nên phút 23:59 nằm ngoài khung giờ.
// Phút 23:58 cũng bị tránh, vì poll có thể đọc 23:58 còn request tới backend lúc 23:59.
const FIRST_RISKY_MINUTE_OF_DAY = 23 * 60 + 58;

test.describe.configure({ mode: 'serial' });

test.describe('SD-E2E1: luồng CSKH trong giờ với backend thật', () => {
  const runSuffix = makeRunSuffix();
  // Email có hậu tố của lần chạy. Nếu `beforeAll` chạy lại sau một case đỏ, lời mời không đụng lỗi trùng email.
  const supportEmail = `e2e-sd-chat-${runSuffix}@pickory.local`;
  const cannedShortcut = `chaokhach-${runSuffix}`;
  const cannedBody = `Chào bạn, Pickory đã nhận được yêu cầu ${runSuffix}.`;
  // Cờ bật khi `beforeAll` chạy xong. `afterAll` chỉ dọn phần đã dựng thành công.
  let isSetupDone = false;
  let admin: StaffSession;
  let support: StaffSession;
  // Giờ làm việc ban đầu của DB. `afterAll` trả lại đúng giá trị này.
  let originalSettings: SupportSettingsBody | undefined;

  test.beforeAll(async ({ browser }) => {
    // Mời và kích hoạt nhân viên qua giao diện thật nên cần nhiều thời gian hơn mặc định.
    test.setTimeout(180000);
    admin = await openStaffSession(browser);
    support = await openStaffSession(browser);
    await loginAdmin(admin.page);
    await dismissConsent(admin.page);

    // Giờ mặc định của DB là 08:00 đến 21:00, nên lần chạy ngoài khung này sẽ không còn là "trong giờ".
    // Spec đặt khung phủ cả ngày và không có ngày nghỉ, rồi trả lại giá trị cũ ở `afterAll`.
    originalSettings = await readSupportSettingsByApi(admin.page);
    await saveSupportSettingsByApi(admin.page, {
      ...originalSettings,
      opensAt: '00:00',
      closesAt: '23:59',
      holidays: [],
    });

    await inviteStaff(admin.page, {
      email: supportEmail,
      fullName: 'Support chat E2E',
      roleCode: 'support',
    });
    await activateStaffAccount(support.page, supportEmail);
    await dismissConsent(support.page);
    // Mẫu dùng chung do admin tạo qua API thật. Nhân viên `support` chọn mẫu này bằng phím `/`.
    await createCannedResponseByApi(admin.page, {
      shortcut: cannedShortcut,
      body: cannedBody,
      shared: true,
    });
    // Console của các bước dựng dữ liệu không thuộc case nào.
    for (const session of [admin, support]) session.consoleIssues.length = 0;
    isSetupDone = true;
  });

  test.afterAll(async () => {
    try {
      if (isSetupDone) {
        // Tắt trạng thái sẵn sàng. Spec khác kiểm "không có nhân viên sẵn sàng" và sẽ đỏ nếu nhân viên này còn bật.
        await setPresenceByApi(support.page, 'away').catch(() => undefined);
        await deleteCannedResponsesWithSuffix(admin.page, runSuffix);
      }
    } finally {
      try {
        // Trả giờ làm việc cũ cả khi dọn mẫu lỗi, vì cấu hình dùng chung cho mọi spec sau.
        if (originalSettings) await saveSupportSettingsByApi(admin.page, originalSettings);
      } finally {
        // Đóng context cả khi dọn dữ liệu lỗi, để trình duyệt không bị treo.
        for (const session of [admin, support]) await session?.context.close();
      }
    }
  });

  test.beforeEach(() => {
    for (const session of [admin, support]) {
      session.consoleIssues.length = 0;
      session.allowedFailures.length = 0;
    }
  });

  function chatComposer(page: Page) {
    return page.getByRole('combobox', { name: 'Nội dung tin nhắn' });
  }

  function waitingTab(page: Page) {
    return page.getByRole('tab', { name: /^Đang chờ/ });
  }

  // Dòng phiên là một nút có tên truy cập chứa tin đầu của phiên.
  function queueRow(page: Page, marker: string) {
    return page.getByRole('button', { name: marker });
  }

  function customerComposer(page: Page) {
    return page.getByLabel('Nội dung tin nhắn', { exact: true });
  }

  // Phút trong ngày theo giờ Việt Nam. Backend tính giờ làm việc theo múi giờ này.
  function readVietnamMinuteOfDay(): number {
    const parts = new Intl.DateTimeFormat('en-GB', {
      timeZone: 'Asia/Ho_Chi_Minh',
      hourCycle: 'h23',
      hour: '2-digit',
      minute: '2-digit',
    }).formatToParts(new Date());
    const part = (type: string) => Number(parts.find((entry) => entry.type === type)?.value);
    return part('hour') * 60 + part('minute');
  }

  // Chờ qua hai phút cuối của ngày trước khi khách gửi tin. Request tới backend có thể rơi vào 23:59, là ngoài khung giờ.
  async function waitUntilInsideWorkingHours() {
    await expect
      .poll(readVietnamMinuteOfDay, {
        message: 'Hai phút cuối của ngày theo giờ Việt Nam có thể rơi ra ngoài khung giờ của spec',
        timeout: 90000,
      })
      .toBeLessThan(FIRST_RISKY_MINUTE_OF_DAY);
  }

  // Khách gửi tin bằng ô soạn của trang `/support` và phím Enter, như người dùng thật.
  // Hàm trả mốc thời gian ngay sau Enter để các bước sau đo hạn SSE.
  async function customerSends(page: Page, text: string): Promise<number> {
    await waitUntilInsideWorkingHours();
    await customerComposer(page).fill(text);
    await customerComposer(page).press('Enter');
    return Date.now();
  }

  // Hàm chờ phần tử hiện, rồi so thời gian từ mốc gửi với hạn. Timeout của `expect` chỉ chặn chờ vô hạn.
  async function measureVisible(
    annotationType: string,
    testInfo: { annotations: { type: string; description?: string }[] },
    locator: Locator,
    sentAt: number,
    limitMs: number,
  ) {
    await expect(locator).toBeVisible({ timeout: limitMs });
    const elapsedMs = Date.now() - sentAt;
    testInfo.annotations.push({
      type: annotationType,
      description: `${elapsedMs} ms (hạn ${limitMs} ms)`,
    });
    expect(elapsedMs, `${annotationType} phải nằm trong hạn ${limitMs} ms`).toBeLessThanOrEqual(
      limitMs,
    );
  }

  test('khách gửi tin đầu, nhân viên nhận, chèn mẫu bằng /, sửa và gửi, kết thúc phiên, khách chấm CSAT rồi gửi tin mới', async ({
    browser,
  }, testInfo) => {
    test.setTimeout(150000);
    const customer = await openStaffSession(browser);
    // Context trình duyệt này đóng vai khách vãng lai. Cookie mới làm backend mở hội thoại mới cho `/support`.
    const customerPage = customer.page;
    const staffPage = support.page;
    const firstMarker = `Cần hỗ trợ đơn hàng ${runSuffix}`;
    const followUpText = `Mình bổ sung mã đơn PK-${runSuffix}.`;
    const editedPhrase = `Mình kiểm tra trong ít phút ${runSuffix}.`;
    const staffMessage = `${cannedBody} ${editedPhrase}`;
    const secondMarker = `Mình còn câu hỏi khác ${runSuffix}`;
    // Mốc gửi của khách và của nhân viên. Mỗi bước đo độ trễ SSE tính từ mốc này.
    let firstSentAt = 0;
    let staffSentAt = 0;
    // Ghi mọi phản hồi 404 của trang nhân viên. Ghi chú Checkpoint SD2 nêu lỗi 404 của `canned-responses` khi route chưa có.
    const notFoundUrls: string[] = [];
    const recordNotFound = (response: Response) => {
      if (response.status() === 404) notFoundUrls.push(response.url());
    };
    staffPage.on('response', recordNotFound);

    try {
      await test.step('nhân viên support bật Sẵn sàng và mở hàng chờ', async () => {
        await staffPage.goto('/admin/conversations');
        await expect(
          staffPage.getByRole('heading', { level: 1, name: 'Chăm sóc khách hàng' }),
        ).toBeVisible();
        await expect(waitingTab(staffPage)).toBeVisible();
        await expect(staffPage.getByText('Đang nạp hàng chờ...')).toHaveCount(0);

        const presenceSwitch = staffPage.getByRole('switch', { name: 'Sẵn sàng', exact: true });
        // Bật bằng giao diện, rồi đối chiếu backend. Công tắc có thể đã bật nếu spec trước để lại.
        if ((await presenceSwitch.getAttribute('aria-checked')) !== 'true') {
          await presenceSwitch.click();
        }
        await expect(presenceSwitch).toHaveAttribute('aria-checked', 'true');
        const staffId = await readOwnUserId(staffPage);
        await expect.poll(() => readAvailableStaffIds(staffPage)).toContain(staffId);
      });

      await test.step('khách vãng lai mở /support và gửi tin đầu, trạng thái đổi sang Đang chờ nhân viên', async () => {
        await customerPage.goto('/support');
        await dismissConsent(customerPage);
        await expect(
          customerPage.getByRole('heading', { name: 'Liên hệ CSKH', exact: true }),
        ).toBeVisible();
        firstSentAt = await customerSends(customerPage, firstMarker);
        await expect(
          customerPage.locator('.support-message-bubble', { hasText: firstMarker }),
        ).toBeVisible();
        await expect(customerPage.locator('.support-chat-status')).toContainText(
          'Đang chờ nhân viên',
        );
      });

      await test.step('nhân viên thấy hội thoại mới trong 3 giây mà không tải lại trang', async () => {
        // Không gọi `reload` hay đọc API: dòng phiên chỉ tới được trang này qua SSE của nhân viên.
        await measureVisible(
          'đo: nhân viên thấy hội thoại mới',
          testInfo,
          queueRow(staffPage, firstMarker),
          firstSentAt,
          STAFF_SEES_NEW_SESSION_MS,
        );
        // Badge và số dòng của tab Đang chờ khớp số phiên chờ mà backend đếm. Đọc cả hai trong cùng vòng poll.
        await expect
          .poll(
            async () => {
              const apiCount = await countQueueSessions(staffPage, 'waiting');
              const badgeText = await waitingTab(staffPage)
                .locator('.tab-list-count')
                .textContent();
              const itemCount = await staffPage
                .getByRole('list', { name: 'Phiên đang chờ' })
                .getByRole('listitem')
                .count();
              return badgeText === String(apiCount) && itemCount === apiCount;
            },
            { message: 'Badge và số dòng của tab Đang chờ phải khớp số phiên chờ của backend' },
          )
          .toBe(true);
      });

      // Mã phiên đầu, để bước cuối chứng minh tin mới mở một phiên khác.
      const firstSession = await findQueueSession(staffPage, 'waiting', firstMarker);

      await test.step('nhân viên nhận phiên, tin bổ sung của khách làm lịch sử đọc lại', async () => {
        await queueRow(staffPage, firstMarker).click();
        await staffPage.getByRole('button', { name: 'Nhận', exact: true }).click();
        await expect(chatComposer(staffPage)).toBeEnabled();
        await expect(customerPage.locator('.support-chat-status')).toContainText(
          'Nhân viên đang hỗ trợ',
        );

        // Khung chat đang mở và không tải lại. Sự kiện `message.created` phải kéo tin bổ sung vào lịch sử.
        await customerSends(customerPage, followUpText);
        await expect(
          staffPage.locator('.support-chat-message').filter({ hasText: followUpText }),
        ).toBeVisible({ timeout: STAFF_SEES_NEW_SESSION_MS });
      });

      await test.step('nhân viên gõ /, chọn mẫu, sửa nội dung và bấm nút Gửi', async () => {
        await chatComposer(staffPage).fill('/');
        const listbox = staffPage.getByRole('listbox');
        await expect(listbox).toBeVisible();
        const options = listbox.getByRole('option');
        const sharedOption = options.filter({ hasText: `/${cannedShortcut}` });
        await expect(sharedOption).toHaveCount(1);

        // Danh sách còn có mẫu của spec khác, nên đếm số lần bấm mũi tên từ vị trí của mẫu này.
        const optionTexts = await options.allTextContents();
        const targetIndex = optionTexts.findIndex((text) => text.includes(`/${cannedShortcut}`));
        expect(targetIndex).toBeGreaterThanOrEqual(0);
        for (let step = 0; step < targetIndex; step++) await staffPage.keyboard.press('ArrowDown');
        await expect(sharedOption).toHaveAttribute('aria-selected', 'true');
        await staffPage.keyboard.press('Enter');
        await expect(chatComposer(staffPage)).toHaveValue(cannedBody);

        // Thêm một cụm riêng của lần chạy vào cuối mẫu. Khách phải thấy nội dung đã sửa.
        await chatComposer(staffPage).press('End');
        await chatComposer(staffPage).pressSequentially(` ${editedPhrase}`);
        await expect(chatComposer(staffPage)).toHaveValue(staffMessage);

        // Gửi bằng nút, không bằng Enter.
        await staffPage.getByRole('button', { name: 'Gửi', exact: true }).click();
        // Mốc đo của hạn 2 giây: ngay sau khi bấm nút Gửi.
        staffSentAt = Date.now();
        await expect(chatComposer(staffPage)).toHaveValue('');
        // Gõ `/` và chọn mẫu không được gây lỗi 404 nào, gồm lỗi từ route `canned-responses`.
        expect(notFoundUrls).toEqual([]);
      });

      await test.step('khách thấy đúng tin đã sửa của nhân viên trong 2 giây', async () => {
        // Khớp chính xác toàn bộ nội dung. Khớp một phần cho qua cả mẫu chưa sửa.
        const staffBubble = customerPage
          .locator('.support-message-bubble')
          .getByText(staffMessage, { exact: true });
        await measureVisible(
          'đo: khách thấy tin nhân viên',
          testInfo,
          staffBubble,
          staffSentAt,
          CUSTOMER_SEES_STAFF_MESSAGE_MS,
        );
        // Nhân viên gửi một lần, nên khách chỉ có một bong bóng với nội dung này.
        await expect(staffBubble).toHaveCount(1);
      });

      await test.step('nhân viên kết thúc phiên, khách thấy form CSAT và chấm điểm bằng giao diện', async () => {
        await staffPage.getByRole('button', { name: 'Kết thúc', exact: true }).click();
        const dialog = staffPage.getByRole('dialog', { name: 'Kết thúc hội thoại này?' });
        await expect(dialog).toBeVisible();
        await dialog.getByRole('button', { name: 'Kết thúc', exact: true }).click();
        await expect(dialog).toHaveCount(0);

        await expect(customerPage.locator('.support-chat-status')).toContainText(
          'Phiên hỗ trợ đã kết thúc',
        );
        const csatForm = customerPage.locator('[data-action="csat_form"]');
        await expect(csatForm).toBeVisible();
        await csatForm.getByRole('button', { name: '5 điểm - Rất hài lòng' }).click();
        await csatForm.locator('button[type="submit"]').click();
        const successAlert = csatForm.locator('.alert-ok');
        await expect(successAlert).toBeVisible();
        await expect(successAlert).toContainText('5/5');
      });

      await test.step('khách gửi tin tiếp theo, hàng chờ hiện một phiên chờ mới khác phiên đã đóng', async () => {
        // Nhân viên đang xem tab Của tôi sau khi nhận. Quay về tab Đang chờ bằng giao diện, không tải lại trang.
        await waitingTab(staffPage).click();
        const secondSentAt = await customerSends(customerPage, secondMarker);

        await measureVisible(
          'đo: nhân viên thấy phiên chờ mới',
          testInfo,
          queueRow(staffPage, secondMarker),
          secondSentAt,
          STAFF_SEES_NEW_SESSION_MS,
        );
        // So mã phiên bằng API đọc. Dòng mới trong hàng chờ chưa chứng minh backend mở phiên mới.
        const secondSession = await findQueueSession(staffPage, 'waiting', secondMarker);
        expect(secondSession.id).not.toBe(firstSession.id);
      });
    } finally {
      staffPage.off('response', recordNotFound);
      // Đóng mọi phiên CSKH của case, kể cả khi case đỏ. Hàng chờ của spec sau không đầy dần.
      await closeSessionsByMarkerBestEffort(staffPage, runSuffix);
      await customer.context.close();
    }
    // Kiểm sau `finally` để lỗi gốc của case không bị che. Các kiểm này chỉ có nghĩa khi mọi bước đã chạy.
    expect(customer.consoleIssues, 'Console của khách phải sạch').toEqual([]);
    // Bước kết thúc phiên và tab Đang chờ lần hai cũng không được gây 404 ở trang nhân viên.
    expect(notFoundUrls).toEqual([]);
  });

  test.afterEach(() => {
    // Mỗi case phải kết thúc với 0 lỗi và 0 cảnh báo trong console của hai trang nhân viên.
    expect(admin.consoleIssues, 'Console của admin phải sạch').toEqual([]);
    expect(support.consoleIssues, 'Console của support phải sạch').toEqual([]);
  });
});
