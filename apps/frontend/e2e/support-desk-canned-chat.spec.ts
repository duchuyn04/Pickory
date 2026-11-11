import { expect, test, type Page, type Request, type Response } from '@playwright/test';
import { expectNoHorizontalOverflow } from './admin-order-fixtures';
import { dismissConsent, loginAdmin } from './catalog-admin-flow';
import { activateStaffAccount, inviteStaff } from './staff-fixtures';
import {
  createCannedResponseByApi,
  deleteCannedResponsesWithSuffix,
  makeRunSuffix,
} from './support-desk-canned-api';
import {
  closeSessionBestEffort,
  findQueueSession,
  openAnonymousCustomerChat,
  openStaffSession,
  readBox,
  screenshotPath,
  type StaffSession,
} from './support-desk-live-fixtures';

// SD-UI2 và SD-UI5: mẫu trả lời nhanh trong khung chat, chạy với route `canned-responses`, `sessions` và `messages` thật.
// Dữ liệu dựng bằng API của backend, và spec không chặn route API nào bằng `page.route`.

test.describe.configure({ mode: 'serial' });

test.describe('SD-UI2 và SD-UI5: mẫu trả lời nhanh với backend thật', () => {
  const runSuffix = makeRunSuffix();
  // Email có hậu tố của lần chạy. Nếu `beforeAll` chạy lại sau một case đỏ, lời mời không đụng lỗi trùng email.
  const supportEmail = `e2e-sd-canned-${runSuffix}@pickory.local`;
  // Mẫu dùng chung đứng sau mẫu cá nhân theo thứ tự ký tự, nên chọn nó cần ít nhất một lần bấm mũi tên xuống.
  const sharedShortcut = `xacnhan-${runSuffix}`;
  const sharedBody = 'Cảm ơn bạn đã liên hệ Pickory.';
  const personalShortcut = `kiemdon-${runSuffix}`;
  const personalBody = 'Mình kiểm đơn giúp bạn nhé.';
  let sessionCounter = 0;
  // Phiên do case tạo. `afterAll` đóng các phiên này để hàng chờ không đầy dần.
  const createdSessionIds: string[] = [];
  // Cờ bật khi `beforeAll` chạy xong. `afterAll` chỉ dọn khi cờ này bật, vì trước đó các phiên chưa đăng nhập.
  let isSetupDone = false;
  let admin: StaffSession;
  let support: StaffSession;

  test.beforeAll(async ({ browser }) => {
    // Mời và kích hoạt nhân viên qua giao diện thật nên cần nhiều thời gian hơn mặc định.
    test.setTimeout(180000);
    admin = await openStaffSession(browser);
    support = await openStaffSession(browser);
    await loginAdmin(admin.page);
    await dismissConsent(admin.page);
    await inviteStaff(admin.page, {
      email: supportEmail,
      fullName: 'Support mẫu E2E',
      roleCode: 'support',
    });
    await activateStaffAccount(support.page, supportEmail);
    await dismissConsent(support.page);
    // Mẫu dùng chung do admin tạo, mẫu cá nhân do chính nhân viên `support` tạo. Cả hai đi qua API thật.
    await createCannedResponseByApi(admin.page, {
      shortcut: sharedShortcut,
      body: sharedBody,
      shared: true,
    });
    await createCannedResponseByApi(support.page, {
      shortcut: personalShortcut,
      body: personalBody,
      shared: false,
    });
    // Console của các bước dựng dữ liệu không thuộc case nào.
    for (const session of [admin, support]) session.consoleIssues.length = 0;
    isSetupDone = true;
  });

  test.afterAll(async () => {
    try {
      if (isSetupDone) {
        // Dọn mẫu do spec tạo. Mỗi bên chỉ xóa được mẫu mà tài khoản của mình sửa được.
        await deleteCannedResponsesWithSuffix(admin.page, runSuffix);
        await deleteCannedResponsesWithSuffix(support.page, runSuffix);
        for (const sessionId of createdSessionIds) {
          await closeSessionBestEffort(support.page, sessionId);
        }
      }
    } finally {
      // Đóng context cả khi dọn dữ liệu lỗi, để trình duyệt không bị treo.
      for (const session of [admin, support]) await session?.context.close();
    }
  });

  test.beforeEach(() => {
    for (const session of [admin, support]) {
      session.consoleIssues.length = 0;
      session.allowedFailures.length = 0;
    }
  });

  test.afterEach(() => {
    // Mỗi case phải kết thúc với 0 lỗi và 0 cảnh báo trong console của cả hai phiên.
    expect(admin.consoleIssues, 'Console của admin phải sạch').toEqual([]);
    expect(support.consoleIssues, 'Console của support phải sạch').toEqual([]);
  });

  function chatComposer(page: Page) {
    return page.getByRole('combobox', { name: 'Nội dung tin nhắn' });
  }

  // Khách ẩn danh gửi một tin. Backend mở phiên chờ cho tin đầu tiên, nên phiên nằm trong hàng chờ thật.
  async function createWaitingSession(): Promise<string> {
    sessionCounter += 1;
    const marker = `Cần mẫu trả lời ${runSuffix} số ${sessionCounter}`;
    const customer = await openAnonymousCustomerChat();
    try {
      await customer.send(marker);
    } finally {
      await customer.dispose();
    }
    const queued = await findQueueSession(support.page, 'waiting', marker);
    createdSessionIds.push(queued.id);
    return marker;
  }

  // Nhân viên mở phiên chờ vừa tạo trong màn chăm sóc khách hàng và bấm Nhận, để ô soạn tin bật.
  async function openClaimedSession(page: Page) {
    const marker = await createWaitingSession();
    await page.goto('/admin/conversations');
    await expect(
      page.getByRole('heading', { level: 1, name: 'Chăm sóc khách hàng' }),
    ).toBeVisible();
    await page.getByRole('button', { name: marker }).click();
    await page.getByRole('button', { name: 'Nhận', exact: true }).click();
    await expect(chatComposer(page)).toBeEnabled();
  }

  test('SD-UI2: gõ / liệt kê mẫu dùng chung tạo qua API, mũi tên chọn mục, Enter chèn mẫu, không có lỗi 404', async () => {
    const { page } = support;
    // Ghi mọi phản hồi 404 của lượt chạy. Ghi chú Checkpoint SD2 nêu lỗi 404 của `canned-responses` khi route chưa có.
    const notFoundUrls: string[] = [];
    const recordNotFound = (response: Response) => {
      if (response.status() === 404) notFoundUrls.push(response.url());
    };
    page.on('response', recordNotFound);
    try {
      await openClaimedSession(page);

      await chatComposer(page).fill('/');
      const listbox = page.getByRole('listbox');
      await expect(listbox).toBeVisible();
      const options = listbox.getByRole('option');
      await expect(options.filter({ hasText: `/${sharedShortcut}` })).toHaveCount(1);
      await expect(options.filter({ hasText: `/${personalShortcut}` })).toHaveCount(1);

      // Danh sách còn có mẫu của spec khác, nên đếm số lần bấm mũi tên từ vị trí của mẫu dùng chung.
      const optionTexts = await options.allTextContents();
      const sharedIndex = optionTexts.findIndex((text) => text.includes(`/${sharedShortcut}`));
      // Mẫu dùng chung đứng sau mẫu cá nhân, nên phải cần ít nhất một lần bấm mũi tên xuống.
      expect(sharedIndex).toBeGreaterThan(0);
      for (let step = 0; step < sharedIndex; step++) await page.keyboard.press('ArrowDown');
      await expect(options.filter({ hasText: `/${sharedShortcut}` })).toHaveAttribute(
        'aria-selected',
        'true',
      );
      await page.keyboard.press('Enter');

      await expect(chatComposer(page)).toHaveValue(sharedBody);
      await expect(listbox).toHaveCount(0);
      expect(notFoundUrls).toEqual([]);
    } finally {
      page.off('response', recordNotFound);
    }
  });

  for (const viewport of [
    { name: '1280', width: 1280, height: 800 },
    { name: '375', width: 375, height: 812 },
  ]) {
    test(`SD-UI5: hộp thoại Mẫu của tôi ${viewport.name}px: trùng phím tắt, tạo mẫu cá nhân, khung soạn tin thấy mẫu mới, không tràn ngang`, async () => {
      const testInfo = test.info();
      const { page } = support;
      const createdShortcut = `baohanh-${runSuffix}-${viewport.name}`;
      const postBodies: unknown[] = [];
      const recordPost = (request: Request) => {
        if (
          request.method() === 'POST' &&
          request.url().endsWith('/api/admin/support/canned-responses')
        ) {
          postBodies.push(request.postDataJSON());
        }
      };
      page.on('request', recordPost);
      try {
        await page.setViewportSize({ width: viewport.width, height: viewport.height });
        await openClaimedSession(page);

        const manageButton = page.getByRole('button', { name: 'Quản lý mẫu', exact: true });
        await manageButton.scrollIntoViewIfNeeded();
        const manageButtonBox = await readBox(manageButton);
        const composerBox = await readBox(page.locator('.support-chat-composer'));
        expect(manageButtonBox.x).toBeGreaterThanOrEqual(0);
        expect(manageButtonBox.x + manageButtonBox.width).toBeLessThanOrEqual(viewport.width);
        // Nút nằm trong khung soạn tin, không làm khung rộng hơn khung nhìn.
        expect(manageButtonBox.x + manageButtonBox.width).toBeLessThanOrEqual(
          composerBox.x + composerBox.width + 0.5,
        );
        expect(composerBox.x + composerBox.width).toBeLessThanOrEqual(viewport.width);
        await expectNoHorizontalOverflow(page);

        await manageButton.click();
        const dialog = page.getByRole('dialog', { name: 'Mẫu của tôi' });
        await expect(dialog).toBeVisible();
        // Hộp thoại chỉ hiện mẫu cá nhân. Mẫu dùng chung do admin tạo không có trong hộp thoại.
        await expect(
          dialog.locator('.canned-row').filter({ hasText: `/${personalShortcut}` }),
        ).toBeVisible();
        await expect(
          dialog.locator('.canned-row').filter({ hasText: `/${sharedShortcut}` }),
        ).toHaveCount(0);
        await expectNoHorizontalOverflow(page);
        await page.screenshot({ path: screenshotPath(testInfo, `my-canned-${viewport.name}.png`) });

        // Thêm mẫu trùng phím tắt cá nhân: backend trả 409 với câu riêng của mẫu cá nhân.
        support.allowedFailures.push({
          urlPattern: /\/api\/admin\/support\/canned-responses$/,
          status: 409,
        });
        await dialog.getByRole('button', { name: 'Thêm mẫu', exact: true }).click();
        const formDialog = page.getByRole('dialog', { name: 'Thêm mẫu' });
        await formDialog.getByLabel('Phím tắt').fill(personalShortcut);
        await formDialog.getByLabel('Nội dung').fill('Nội dung giữ lại.');
        await formDialog.getByRole('button', { name: 'Lưu mẫu' }).click();
        await expect(formDialog.locator('#canned-response-shortcut-error')).toHaveText(
          'Bạn đã có mẫu dùng phím tắt này.',
        );
        await expect(formDialog.getByLabel('Nội dung')).toHaveValue('Nội dung giữ lại.');

        // Đổi sang phím tắt chưa có rồi lưu thành công.
        await formDialog.getByLabel('Phím tắt').fill(createdShortcut);
        await formDialog.getByLabel('Nội dung').fill('Sản phẩm được bảo hành 12 tháng.');
        await page.screenshot({
          path: screenshotPath(testInfo, `my-canned-form-${viewport.name}.png`),
        });
        await formDialog.getByRole('button', { name: 'Lưu mẫu' }).click();

        await expect(formDialog).toHaveCount(0);
        await expect(
          dialog.locator('.canned-row').filter({ hasText: `/${createdShortcut}` }),
        ).toBeVisible();
        // Mẫu cá nhân gửi `shared: false`. Lần gửi đầu bị 409, lần sau thành công.
        expect(postBodies).toEqual([
          { shortcut: personalShortcut, body: 'Nội dung giữ lại.', shared: false },
          { shortcut: createdShortcut, body: 'Sản phẩm được bảo hành 12 tháng.', shared: false },
        ]);

        const dialogBox = await readBox(dialog);
        expect(dialogBox.x).toBeGreaterThanOrEqual(0);
        expect(dialogBox.x + dialogBox.width).toBeLessThanOrEqual(viewport.width);
        expect(dialogBox.y).toBeGreaterThanOrEqual(0);
        expect(dialogBox.y + dialogBox.height).toBeLessThanOrEqual(viewport.height);
        // Mở hộp thoại thì trang nền không cuộn được.
        const bodyOverflow = await page.evaluate(() => document.body.style.overflow);
        expect(bodyOverflow).toBe('hidden');
        await expectNoHorizontalOverflow(page);
        await testInfo.attach(`measurements-my-canned-${viewport.name}`, {
          body: JSON.stringify(
            { viewport, manageButtonBox, composerBox, dialogBox, bodyOverflow },
            null,
            2,
          ),
          contentType: 'application/json',
        });
        await page.screenshot({
          path: screenshotPath(testInfo, `my-canned-created-${viewport.name}.png`),
        });

        // Esc đóng hộp thoại và trả focus về nút Quản lý mẫu.
        await page.keyboard.press('Escape');
        await expect(dialog).toHaveCount(0);
        await expect(manageButton).toBeFocused();

        // Khung soạn tin dùng ngay danh sách mới: gõ phím tắt vừa tạo thì thấy mẫu đó.
        await chatComposer(page).fill(`/${createdShortcut}`);
        await expect(page.getByRole('option', { name: `/${createdShortcut}` })).toBeVisible();
      } finally {
        page.off('request', recordPost);
        await page.setViewportSize({ width: 1280, height: 800 });
      }
    });
  }
});
