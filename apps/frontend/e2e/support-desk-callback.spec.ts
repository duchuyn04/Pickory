import { expect, test, type Page, type Request } from '@playwright/test';
import { dismissConsent, loginAdmin } from './catalog-admin-flow';
import { activateStaffAccount, inviteStaff } from './staff-fixtures';
import { makeRunSuffix } from './support-desk-canned-api';
import {
  closeSessionsByMarkerBestEffort,
  createCallbackByApi,
  findQueueSession,
  listCallbacksByApi,
  makeUniquePhone,
  markCallbackByApi,
  openStaffSession,
  readAvailableStaffIds,
  readBox,
  readOwnUserId,
  readSupportSettingsByApi,
  readVietnamHolidayDates,
  resolveAllPendingCallbacks,
  saveSupportSettingsByApi,
  setPresenceByApi,
  waitUntilInsideWorkingHours,
  type QueueSessionRecord,
  type StaffSession,
  type SupportSettingsBody,
} from './support-desk-live-fixtures';

// Kiểm thử E2E cho luồng ngoài giờ, hai nhân viên cùng nhận một phiên và phân quyền sales.
// Mọi thao tác đi qua API thật và luồng sự kiện thật của backend.

const CLAIM_URL_PATTERN = /\/api\/admin\/support\/sessions\/[^/]+\/assignment$/;

function presenceSwitch(page: Page) {
  return page.getByRole('switch', { name: 'Sẵn sàng', exact: true });
}

function waitingTab(page: Page) {
  return page.getByRole('tab', { name: /^Đang chờ/ });
}

function customerComposer(page: Page) {
  return page.getByLabel('Nội dung tin nhắn', { exact: true });
}

function chatComposer(page: Page) {
  return page.getByRole('combobox', { name: 'Nội dung tin nhắn' });
}

function queueRow(page: Page, marker: string) {
  return page.getByRole('button', { name: marker });
}

// Kiểm tra phiên có nằm trong danh sách trả về của API hàng chờ hay không.
async function hasQueueSession(
  staffPage: Page,
  view: 'waiting' | 'mine',
  sessionId: string,
): Promise<boolean> {
  const response = await staffPage.request.get(`/api/admin/support/sessions?view=${view}`);
  expect(response.status(), await response.text()).toBe(200);
  const body: { data: QueueSessionRecord[] } = await response.json();
  return body.data.some((session) => session.id === sessionId);
}

test.describe.configure({ mode: 'serial' });

test.describe('SD-E2E2: luồng ngoài giờ và hai người nhận với backend thật', () => {
  const runSuffix = makeRunSuffix();
  // Email gắn hậu tố riêng của lần chạy để lời mời không trùng khi chạy lại.
  const supportOneEmail = `e2e-sd-cb-s1-${runSuffix}@pickory.local`;
  const supportTwoEmail = `e2e-sd-cb-s2-${runSuffix}@pickory.local`;
  const salesEmail = `e2e-sd-cb-sales-${runSuffix}@pickory.local`;
  // Cờ đánh dấu bước dựng dữ liệu hoàn tất để `afterAll` chỉ dọn tài nguyên đã tạo.
  let isSetupDone = false;
  let admin: StaffSession;
  let supportOne: StaffSession;
  let supportTwo: StaffSession;
  let sales: StaffSession;
  // Cấu hình gốc của DB để `afterAll` khôi phục cho các file kiểm thử chạy sau.
  let originalSettings: SupportSettingsBody | undefined;

  test.beforeAll(async ({ browser }) => {
    // Quá trình tạo ba tài khoản nhân viên qua UI thật cần hạn chờ 240 giây.
    test.setTimeout(240000);
    // Mở bốn context trình duyệt tách biệt cho quản trị viên và ba nhân viên.
    admin = await openStaffSession(browser);
    supportOne = await openStaffSession(browser);
    supportTwo = await openStaffSession(browser);
    sales = await openStaffSession(browser);
    await loginAdmin(admin.page);
    await dismissConsent(admin.page);

    // Đọc cấu hình hiện tại trước khi các nhóm test ghi đè giờ làm việc và ngày nghỉ.
    originalSettings = await readSupportSettingsByApi(admin.page);

    // Ba tài khoản nhân viên dùng chứng thực thật của hệ thống để kiểm tra phân quyền.
    await inviteStaff(admin.page, {
      email: supportOneEmail,
      fullName: 'Support một E2E',
      roleCode: 'support',
    });
    await inviteStaff(admin.page, {
      email: supportTwoEmail,
      fullName: 'Support hai E2E',
      roleCode: 'support',
    });
    await inviteStaff(admin.page, {
      email: salesEmail,
      fullName: 'Sales E2E',
      roleCode: 'sales',
    });

    await activateStaffAccount(supportOne.page, supportOneEmail);
    await dismissConsent(supportOne.page);
    await activateStaffAccount(supportTwo.page, supportTwoEmail);
    await dismissConsent(supportTwo.page);
    await activateStaffAccount(sales.page, salesEmail);
    await dismissConsent(sales.page);

    // Xóa thông báo console của bước dựng dữ liệu để mỗi test bắt đầu với danh sách trống.
    for (const staffSession of [admin, supportOne, supportTwo, sales]) {
      staffSession.consoleIssues.length = 0;
    }
    isSetupDone = true;
  });

  test.afterAll(async () => {
    try {
      if (isSetupDone) {
        // Đưa hai nhân viên về trạng thái tạm vắng để không ảnh hưởng tới file kiểm thử khác.
        await setPresenceByApi(supportOne.page, 'away').catch(() => undefined);
        await setPresenceByApi(supportTwo.page, 'away').catch(() => undefined);
        // Phiên và yêu cầu gọi lại còn mở phải kết thúc để hàng chờ sạch cho test sau.
        await closeSessionsByMarkerBestEffort(admin.page, runSuffix);
        await resolveAllPendingCallbacks(admin.page).catch(() => undefined);
      }
    } finally {
      try {
        // Khôi phục cấu hình giờ làm việc ban đầu cả khi bước dọn phiên gặp lỗi.
        if (originalSettings) await saveSupportSettingsByApi(admin.page, originalSettings);
      } finally {
        // Đóng mọi context trình duyệt để tiến trình kiểm thử không bị treo.
        for (const staffSession of [admin, supportOne, supportTwo, sales]) {
          await staffSession?.context.close();
        }
      }
    }
  });

  test.beforeEach(() => {
    // Làm trống danh sách lỗi console và lỗi tải cho phép trước mỗi test.
    for (const staffSession of [admin, supportOne, supportTwo, sales]) {
      staffSession.consoleIssues.length = 0;
      staffSession.allowedFailures.length = 0;
    }
  });

  test.afterEach(() => {
    // Console của mọi trang nhân viên phải có 0 lỗi và 0 cảnh báo sau mỗi test.
    expect(admin.consoleIssues, 'Console của admin phải sạch').toEqual([]);
    expect(supportOne.consoleIssues, 'Console của support một phải sạch').toEqual([]);
    expect(supportTwo.consoleIssues, 'Console của support hai phải sạch').toEqual([]);
    expect(sales.consoleIssues, 'Console của sales phải sạch').toEqual([]);
  });

  test.describe('nhóm cấu hình ngoài giờ', () => {
    test.beforeAll(async () => {
      // Lịch nghỉ gồm hôm nay và ngày mai theo giờ Việt Nam giữ ca chạy luôn ngoài giờ.
      await saveSupportSettingsByApi(admin.page, {
        ...originalSettings!,
        opensAt: '08:00',
        closesAt: '21:00',
        holidays: readVietnamHolidayDates(),
      });
    });

    test.afterAll(async () => {
      // Trả lại lịch gốc ngay khi rời nhóm ngoài giờ để nhóm kế tiếp không dính ngày nghỉ.
      if (originalSettings) await saveSupportSettingsByApi(admin.page, originalSettings);
    });

    test('khách gửi tin ngoài giờ thấy thông báo và form số điện thoại, yêu cầu mới hiện ngay ở tab Gọi lại và nhân viên đánh dấu Đã gọi', async ({
      browser,
    }) => {
      test.setTimeout(120000);
      const customer = await openStaffSession(browser);
      // Context trình duyệt mới đóng vai khách vãng lai mở trang hỗ trợ.
      const customerPage = customer.page;
      const staffPage = supportOne.page;
      const outsideMarker = `Hỏi đơn ngoài giờ ${runSuffix}`;
      const callbackPhone = makeUniquePhone();
      const callbackNote = `Gọi lại buổi sáng ${runSuffix}`;

      try {
        await test.step('nhân viên support bật Sẵn sàng và mở sẵn tab Gọi lại', async () => {
          // Đánh dấu hoàn tất các yêu cầu gọi lại cũ để nhóm chờ bắt đầu từ trạng thái trống.
          await resolveAllPendingCallbacks(staffPage);
          const existingCallbacks = await listCallbacksByApi(staffPage);
          if (!existingCallbacks.some((record) => record.status !== 'pending')) {
            // Tạo sẵn một dòng đã gọi để kiểm thứ tự hiển thị giữa hai nhóm trên tab Gọi lại.
            const seededPhone = makeUniquePhone();
            await createCallbackByApi(seededPhone, null);
            const seededRecord = (await listCallbacksByApi(staffPage)).find(
              (record) => record.phone === seededPhone,
            );
            expect(seededRecord).toBeDefined();
            await markCallbackByApi(staffPage, seededRecord!.id, 'done');
          }

          // Cột hàng chờ phải nạp xong dữ liệu trước khi nhân viên thao tác trên trang.
          await staffPage.goto('/admin/conversations');
          await expect(
            staffPage.getByRole('heading', { level: 1, name: 'Chăm sóc khách hàng' }),
          ).toBeVisible();
          await expect(waitingTab(staffPage)).toBeVisible();
          await expect(staffPage.getByText('Đang nạp hàng chờ...')).toHaveCount(0);

          // Bật trạng thái sẵn sàng để chứng minh kết quả ngoài giờ đến từ lịch nghỉ.
          if ((await presenceSwitch(staffPage).getAttribute('aria-checked')) !== 'true') {
            await presenceSwitch(staffPage).click();
          }
          await expect(presenceSwitch(staffPage)).toHaveAttribute('aria-checked', 'true');
          const staffId = await readOwnUserId(staffPage);
          await expect.poll(() => readAvailableStaffIds(staffPage)).toContain(staffId);

          // Giữ tab Gọi lại mở sẵn để kiểm chứng yêu cầu gọi lại mới tự hiện qua luồng sự kiện.
          await staffPage.getByRole('tab', { name: 'Gọi lại', exact: true }).click();
          await expect(staffPage.getByText('Không có yêu cầu nào cần gọi lại.')).toBeVisible();
          await expect(staffPage.getByRole('list', { name: 'Đã xử lý' })).toBeVisible();
        });

        await test.step('khách gửi tin ở /support ngoài giờ và thấy thông báo kèm form số điện thoại', async () => {
          // Tin đầu tiên của khách vãng lai kích hoạt bước kiểm tra lịch làm việc ở backend.
          await customerPage.goto('/support');
          await dismissConsent(customerPage);
          await expect(
            customerPage.getByRole('heading', { name: 'Liên hệ CSKH', exact: true }),
          ).toBeVisible();

          await customerComposer(customerPage).fill(outsideMarker);
          await customerComposer(customerPage).press('Enter');
          await expect(
            customerPage.locator('.support-message-bubble', { hasText: outsideMarker }),
          ).toBeVisible();
          // Backend trả tin hệ thống báo ngoài giờ hỗ trợ kèm biểu mẫu để lại số điện thoại.
          await expect(
            customerPage.getByText(
              'Hiện ngoài giờ hỗ trợ (8:00 đến 21:00). Bạn để lại SĐT, CSKH sẽ gọi lại.',
              { exact: true },
            ),
          ).toBeVisible({ timeout: 5000 });
          await expect(customerPage.locator('[data-action="callback_form"]')).toBeVisible();
        });

        await test.step('khách gửi số điện thoại, tab Gọi lại của nhân viên hiện yêu cầu mới mà không tải lại trang', async () => {
          // Biểu mẫu ngoài giờ gửi số điện thoại kèm ghi chú lên API tạo yêu cầu gọi lại.
          const callbackForm = customerPage.locator('[data-action="callback_form"]');
          await callbackForm.locator('input[name="phone"]').fill(callbackPhone);
          await callbackForm.locator('textarea[name="note"]').fill(callbackNote);
          await callbackForm.locator('button[type="submit"]').click();

          const successAlert = callbackForm.locator('.alert-ok');
          await expect(successAlert).toBeVisible();
          await expect(successAlert).toContainText('Yêu cầu gọi lại đã được ghi nhận');
          // Không gọi `reload`: sự kiện `callback.created` làm tab Gọi lại đang mở tự nạp yêu cầu mới.
          const callbackRegion = staffPage.getByRole('group', { name: 'Yêu cầu gọi lại' });
          const pendingGroup = callbackRegion.getByRole('list', { name: /^Cần gọi lại/ });
          const handledGroup = callbackRegion.getByRole('list', { name: 'Đã xử lý' });
          await expect(pendingGroup).toBeVisible();
          const pendingRow = pendingGroup.getByRole('listitem').filter({ hasText: callbackPhone });
          await expect(pendingRow).toBeVisible();
          await expect(pendingRow).toContainText(callbackNote);

          // Số điện thoại hiện dạng liên kết `tel:`. Nhóm chờ luôn đứng phía trên nhóm hoàn tất.
          await expect(pendingGroup.getByRole('link', { name: callbackPhone })).toHaveAttribute(
            'href',
            `tel:${callbackPhone}`,
          );
          const pendingBox = await readBox(pendingGroup);
          const handledBox = await readBox(handledGroup);
          expect(pendingBox.y).toBeLessThan(handledBox.y);
        });

        await test.step('nhân viên đánh dấu Đã gọi và yêu cầu rời nhóm pending', async () => {
          const callbackRegion = staffPage.getByRole('group', { name: 'Yêu cầu gọi lại' });
          const handledGroup = callbackRegion.getByRole('list', { name: 'Đã xử lý' });

          // Nhân viên bấm nút đánh dấu đã gọi để chuyển yêu cầu gọi lại sang nhóm hoàn tất.
          await staffPage
            .getByRole('button', { name: `Đánh dấu đã gọi: ${callbackPhone}` })
            .click();

          await expect(
            staffPage.getByRole('heading', { level: 3, name: /^Cần gọi lại/ }),
          ).toHaveCount(0);
          await expect(staffPage.getByText('Không có yêu cầu nào cần gọi lại.')).toBeVisible();
          await expect(
            handledGroup
              .getByRole('listitem')
              .filter({ hasText: callbackPhone })
              .getByText('Đã gọi', { exact: true }),
          ).toBeVisible();

          // Đối chiếu qua API đọc để xác nhận trạng thái trong DB đã chuyển sang `done`.
          const storedCallbacks = await listCallbacksByApi(staffPage);
          const updatedRecord = storedCallbacks.find((record) => record.phone === callbackPhone);
          expect(updatedRecord?.status).toBe('done');
        });
      } finally {
        // Khối `finally` giải phóng dữ liệu và context trình duyệt của khách cả khi test lỗi.
        await resolveAllPendingCallbacks(staffPage).catch(() => undefined);
        await closeSessionsByMarkerBestEffort(staffPage, runSuffix);
        await customer.context.close();
      }
      // Kiểm tra console của khách sau khối dọn dẹp để không bỏ sót lỗi phát sinh cuối luồng.
      expect(customer.consoleIssues, 'Console của khách phải sạch').toEqual([]);
    });
  });

  test.describe('nhóm cấu hình trong giờ và phân quyền', () => {
    test.beforeAll(async () => {
      // Mở rộng khung giờ cả ngày trên lịch hiện tại để khách gửi tin mở phiên chờ trong giờ.
      const currentSettings = await readSupportSettingsByApi(admin.page);
      await saveSupportSettingsByApi(admin.page, {
        ...currentSettings,
        opensAt: '00:00',
        closesAt: '23:59',
      });
    });

    test('hai nhân viên bấm nhận cùng một phiên: một người nhận được và một người thấy Đã có người nhận', async ({
      browser,
    }) => {
      test.setTimeout(120000);
      const customer = await openStaffSession(browser);
      const customerPage = customer.page;
      const claimMarker = `Tranh chấp nhận phiên ${runSuffix}`;
      // Lỗi 409 khi hai nhân viên cùng bấm Nhận là kết quả mong đợi của người thua.
      supportOne.allowedFailures.push({ urlPattern: CLAIM_URL_PATTERN, status: 409 });
      supportTwo.allowedFailures.push({ urlPattern: CLAIM_URL_PATTERN, status: 409 });

      try {
        await test.step('hai nhân viên support bật Sẵn sàng và mở cùng một phiên chờ', async () => {
          // Hai nhân viên cần ở trạng thái sẵn sàng để cùng nhận sự kiện phiên chờ mới.
          for (const staffSession of [supportOne, supportTwo]) {
            await staffSession.page.goto('/admin/conversations');
            await expect(
              staffSession.page.getByRole('heading', { level: 1, name: 'Chăm sóc khách hàng' }),
            ).toBeVisible();
            await expect(waitingTab(staffSession.page)).toBeVisible();
            await expect(staffSession.page.getByText('Đang nạp hàng chờ...')).toHaveCount(0);
            if ((await presenceSwitch(staffSession.page).getAttribute('aria-checked')) !== 'true') {
              await presenceSwitch(staffSession.page).click();
            }
            await expect(presenceSwitch(staffSession.page)).toHaveAttribute('aria-checked', 'true');
          }
          // Chờ backend ghi nhận cả hai nhân viên sẵn sàng trước khi khách gửi tin.
          const staffOneId = await readOwnUserId(supportOne.page);
          const staffTwoId = await readOwnUserId(supportTwo.page);
          await expect.poll(() => readAvailableStaffIds(supportOne.page)).toContain(staffOneId);
          await expect.poll(() => readAvailableStaffIds(supportTwo.page)).toContain(staffTwoId);

          // Khách vãng lai gửi tin đầu tiên trong giờ để backend mở phiên chờ mới.
          await customerPage.goto('/support');
          await dismissConsent(customerPage);
          await expect(
            customerPage.getByRole('heading', { name: 'Liên hệ CSKH', exact: true }),
          ).toBeVisible();

          await waitUntilInsideWorkingHours();
          await customerComposer(customerPage).fill(claimMarker);
          await customerComposer(customerPage).press('Enter');
          await expect(
            customerPage.locator('.support-message-bubble', { hasText: claimMarker }),
          ).toBeVisible();
          // Tin gửi trong giờ nhận thông báo chờ nhân viên. Biểu mẫu ngoài giờ không xuất hiện.
          await expect(
            customerPage.getByText(/^Đã chuyển cho nhân viên CSKH\. Thời gian chờ dự kiến khoảng /),
          ).toBeVisible({ timeout: 4000 });
          await expect(customerPage.locator('[data-action="callback_form"]')).toHaveCount(0);

          // Hai nhân viên cùng mở khung chat của phiên chờ mới để chuẩn bị bấm nút Nhận.
          await expect(queueRow(supportOne.page, claimMarker)).toBeVisible();
          await expect(queueRow(supportTwo.page, claimMarker)).toBeVisible();
          await queueRow(supportOne.page, claimMarker).click();
          await queueRow(supportTwo.page, claimMarker).click();
          await expect(
            supportOne.page.getByRole('button', { name: 'Nhận', exact: true }),
          ).toBeEnabled();
          await expect(
            supportTwo.page.getByRole('button', { name: 'Nhận', exact: true }),
          ).toBeEnabled();
        });

        // Lấy mã phiên đang chờ từ API để theo dõi request nhận phiên của hai nhân viên.
        const waitingSession = await findQueueSession(supportOne.page, 'waiting', claimMarker);

        await test.step('hai nhân viên bấm Nhận cùng lúc, một người giữ phiên và người còn lại thấy Đã có người nhận', async () => {
          const claimUrlSuffix = `/api/admin/support/sessions/${waitingSession.id}/assignment`;
          const responseOnePromise = supportOne.page.waitForResponse(
            (response) =>
              response.url().endsWith(claimUrlSuffix) && response.request().method() === 'POST',
          );
          const responseTwoPromise = supportTwo.page.waitForResponse(
            (response) =>
              response.url().endsWith(claimUrlSuffix) && response.request().method() === 'POST',
          );

          // Khởi phát hai lượt bấm đồng thời để kiểm tra khóa nguyên tử ở backend.
          await Promise.all([
            supportOne.page.getByRole('button', { name: 'Nhận', exact: true }).click(),
            supportTwo.page.getByRole('button', { name: 'Nhận', exact: true }).click(),
          ]);
          const [responseOne, responseTwo] = await Promise.all([
            responseOnePromise,
            responseTwoPromise,
          ]);
          expect([responseOne.status(), responseTwo.status()].sort()).toEqual([201, 409]);

          // Xác định người thắng và người thua sau khi nhận phản hồi để test không phụ thuộc thứ tự mạng.
          const [winner, loser] =
            responseOne.status() === 201 ? [supportOne, supportTwo] : [supportTwo, supportOne];

          // Người thắng giữ được phiên nên ô soạn tin chuyển sang trạng thái cho phép nhập.
          await expect(chatComposer(winner.page)).toBeEnabled();
          await expect(winner.page.getByRole('button', { name: 'Nhận', exact: true })).toHaveCount(
            0,
          );
          await expect(
            winner.page.getByRole('alert').filter({ hasText: 'Đã có người nhận' }),
          ).toHaveCount(0);

          // Người thua nhận mã 409 nên giao diện hiện cảnh báo đã có người nhận.
          await expect(
            loser.page.getByRole('alert').filter({ hasText: 'Đã có người nhận' }),
          ).toBeVisible();
          await expect(chatComposer(loser.page)).toBeDisabled();

          // Dữ liệu API xác nhận phiên đã rời hàng chờ để thuộc về đúng người thắng.
          expect(await hasQueueSession(winner.page, 'mine', waitingSession.id)).toBe(true);
          expect(await hasQueueSession(loser.page, 'mine', waitingSession.id)).toBe(false);
          expect(await hasQueueSession(winner.page, 'waiting', waitingSession.id)).toBe(false);
        });
      } finally {
        // Đóng phiên tranh chấp và context trình duyệt của khách để không rò rỉ dữ liệu sang test sau.
        await closeSessionsByMarkerBestEffort(admin.page, runSuffix);
        await customer.context.close();
      }
      // Kiểm tra console của khách sau khối dọn dẹp để không bỏ sót lỗi phát sinh cuối luồng.
      expect(customer.consoleIssues, 'Console của khách phải sạch').toEqual([]);
    });

    test('sales không thấy menu CSKH, mở thẳng URL thấy trang không đủ quyền và không gọi API support desk', async () => {
      const { page } = sales;
      const supportRequests: string[] = [];
      // Theo dõi mọi request mạng để chứng minh trang thiếu quyền không gọi API chăm sóc khách hàng.
      const recordSupportRequest = (request: Request) => {
        if (request.url().includes('/api/admin/support')) supportRequests.push(request.url());
      };
      page.on('request', recordSupportRequest);

      try {
        // Tài khoản `sales` thiếu quyền `conversations:read` nên thanh điều hướng ẩn mục chăm sóc khách hàng.
        await page.goto('/admin');
        await expect(page.getByRole('link', { name: 'Tổng quan', exact: true })).toBeVisible();
        await expect(page.getByRole('link', { name: 'CSKH', exact: true })).toHaveCount(0);
        await expect(presenceSwitch(page)).toHaveCount(0);

        // Truy cập trực tiếp đường dẫn bị chặn phải hiện cảnh báo 403 nêu rõ quyền còn thiếu.
        await page.goto('/admin/conversations');
        const notice = page.getByRole('alert');
        await expect(notice).toContainText('403 - Không có quyền truy cập');
        await expect(notice).toContainText('conversations:read');
        await expect(page.getByRole('tablist')).toHaveCount(0);
        // Trang chặn quyền không vẽ cột nào nên không phát sinh request tới API chăm sóc khách hàng.
        expect(supportRequests).toEqual([]);
      } finally {
        page.off('request', recordSupportRequest);
      }
    });
  });
});
