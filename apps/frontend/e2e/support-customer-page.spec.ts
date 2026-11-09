import path from 'node:path';
import { expect, test, type Browser, type BrowserContext, type Page } from '@playwright/test';
import { expectNoHorizontalOverflow } from './admin-order-fixtures';
import { collectConsoleIssues, type AllowedLoadFailure } from './catalog-admin-flow';
import {
  dismissConsent,
  loginAdmin,
  seedCatalog,
  stubProductImages,
  type SeededCatalog,
} from './catalog-product-seed';
import { stubWebFonts } from './commerce-order-fixtures';
import { e2eEnvironment } from './environment';
import { createSupportDeskRouteMocks } from './support-desk-contract-routes';

// SD-UI7: trang /support của khách. Ba route hội thoại và tin đã có backend thật (SD-15, SD-16).
// Chỉ luồng sự kiện SSE được giả bằng helper `mockEventStream` của SD-C2, vì helper sự kiện không kiểm OpenAPI.

const STAFF_MESSAGE_BODY = 'Chào bạn, mình kiểm đơn PK123 ngay nhé.';
// Số lần Tab tối đa khi đi bàn phím từ đầu trang, để test không lặp vô hạn nếu lỗi focus.
const MAX_TAB_TRAVERSAL = 30;
// Chiều cao tối đa của ô soạn, khớp `max-height` trong `support-customer.css`.
const COMPOSER_MAX_HEIGHT_PX = 136;

interface GuestPage {
  context: BrowserContext;
  page: Page;
  consoleIssues: string[];
}

// Mỗi case một ngữ cảnh riêng. Khách vãng lai có cookie mới nên luôn nhận hội thoại mới với câu báo 90 ngày.
async function openGuestPage(
  browser: Browser,
  allowedFailures: AllowedLoadFailure[] = [],
): Promise<GuestPage> {
  const context = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
  const page = await context.newPage();
  const consoleIssues = collectConsoleIssues(page, allowedFailures);
  // Chặn phông chữ ngoài để lỗi mạng ngoài không làm console có lỗi oan.
  await stubWebFonts(page);
  await stubProductImages(page);
  return { context, page, consoleIssues };
}

// Ảnh chụp vào thư mục ngoài repo khi có biến môi trường. Không có biến thì Playwright giữ ảnh trong thư mục kết quả.
function screenshotPath(testInfo: { outputPath: (name: string) => string }, fileName: string) {
  const directory = process.env.SUPPORT_DESK_SHOTS_DIR;
  return directory ? path.join(directory, fileName) : testInfo.outputPath(fileName);
}

async function openSupportPage(page: Page) {
  // SD-UI8: apiRequest đã tự chờ request đầu tiên của tab để nhận cookie pk_anon.
  // Mở thẳng trang mà không cần nạp trước cookie.
  await page.goto('/support');
  await dismissConsent(page);
  await expect(page.getByRole('heading', { name: 'Liên hệ CSKH', exact: true })).toBeVisible();
}

function composer(page: Page) {
  return page.getByLabel('Nội dung tin nhắn', { exact: true });
}

// Tạo hội thoại trước bằng chính phiên của trang, để trang mở ra nhận cùng hội thoại đó.
async function createConversationId(page: Page): Promise<string> {
  // API chặn request thiếu Origin. `page.request` dùng chung cookie với trang nhưng không tự gửi Origin.
  const response = await page.request.post('/api/support/conversations', {
    headers: { Origin: e2eEnvironment.webOrigin },
  });
  const status = response.status();
  // Route trả 201 khi tạo mới, 200 khi trả hội thoại đang mở. Hai mã đều là thành công.
  expect(status === 200 || status === 201, await response.text()).toBe(true);
  const body = (await response.json()) as { data: { id: string } };
  return body.data.id;
}

function eventsPath(conversationId: string): string {
  return `/api/support/conversations/${conversationId}/events`;
}

// Đếm request của browser tới luồng sự kiện bằng cách quan sát, không chặn route `/api/support/`.
function countEventStreamRequests(page: Page, conversationId: string): { count: () => number } {
  let requests = 0;
  page.on('request', (request) => {
    if (request.url().includes(eventsPath(conversationId))) requests += 1;
  });
  return { count: () => requests };
}

// Gắn luồng sự kiện giả với tin nhân viên có tên, tin hệ thống và sự kiện chờ cho ảnh bố cục.
async function mockChatEvents(page: Page, conversationId: string) {
  const routes = createSupportDeskRouteMocks({ page, apiOrigin: e2eEnvironment.apiOrigin });
  await routes.mockEventStream({
    path: eventsPath(conversationId),
    fixtureFile: 'customer-events.json',
    eventNames: ['message.created', 'session.waiting'],
  });
}

test.describe('SD-UI7: trang /support của khách', () => {
  test('khách vãng lai thấy câu báo 90 ngày, gửi tin bằng Enter và tin hiện trong khung chat', async ({
    browser,
  }, testInfo) => {
    const { context, page, consoleIssues } = await openGuestPage(browser);

    await openSupportPage(page);
    await expect(page.getByText('Hội thoại này được lưu 90 ngày.')).toBeVisible();
    await expect(page.getByText('Bạn cần hỗ trợ gì?')).toBeVisible();

    await composer(page).fill('Đơn PK123 của mình giao tới đâu rồi?');
    await composer(page).press('Enter');
    await expect(page.getByText('Đơn PK123 của mình giao tới đâu rồi?')).toBeVisible();
    // Tin khách hiện kèm nhãn người gửi để trình đọc màn hình đọc được vai trò.
    await expect(page.locator('.support-message-sender', { hasText: 'Bạn' })).toBeVisible();

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.screenshot({
      path: screenshotPath(testInfo, 'support-customer-1280.png'),
      fullPage: true,
    });
    await expectNoHorizontalOverflow(page);

    await page.setViewportSize({ width: 375, height: 800 });
    await page.screenshot({
      path: screenshotPath(testInfo, 'support-customer-375.png'),
      fullPage: true,
    });
    await expectNoHorizontalOverflow(page);

    expect(consoleIssues).toEqual([]);
    await context.close();
  });

  test('khách mở thẳng /support lần đầu vẫn thấy khung chat và gửi được tin', async ({
    browser,
  }) => {
    // Lần mở thẳng có thể nhận một 404 tải lịch sử do cookie đầu trang chưa ổn định. Trang tự đọc lại
    // và hiện hội thoại, nên chỉ lỗi tải đó được phép trong console. Mọi lỗi khác vẫn tính.
    const allowedFailures: AllowedLoadFailure[] = [
      { urlPattern: /\/api\/support\/conversations\/[^/]+\/messages/, status: 404 },
    ];
    const { context, page, consoleIssues } = await openGuestPage(browser, allowedFailures);

    // Không gọi `/api/me` trước. Đây là đường khách thật đi từ `form_url` của chatbot tới trang.
    await page.goto('/support');
    await dismissConsent(page);
    await expect(page.getByRole('heading', { name: 'Liên hệ CSKH', exact: true })).toBeVisible();

    await composer(page).fill('Mình vào từ link ngoài giờ hỗ trợ.');
    await composer(page).press('Enter');
    await expect(page.getByText('Mình vào từ link ngoài giờ hỗ trợ.')).toBeVisible();

    expect(consoleIssues).toEqual([]);
    await context.close();
  });

  test('tin nhiều dòng giữ xuống dòng và ô soạn tự cao tới giới hạn', async ({ browser }) => {
    const { context, page, consoleIssues } = await openGuestPage(browser);

    await openSupportPage(page);
    await composer(page).fill('Dòng một của tin.');
    await composer(page).press('Shift+Enter');
    await composer(page).pressSequentially('Dòng hai của tin.');
    // Hai dòng phải nằm gọn trong ô, không sinh thanh cuộn trong ô soạn.
    const twoLineBox = await composer(page).evaluate((element) => ({
      scrollHeight: element.scrollHeight,
      clientHeight: element.clientHeight,
    }));
    expect(twoLineBox.scrollHeight).toBeLessThanOrEqual(twoLineBox.clientHeight);

    await composer(page).press('Enter');
    const bubble = page.locator('.support-message-bubble', { hasText: 'Dòng một của tin.' });
    await expect(bubble).toBeVisible();
    // `pre-wrap` giữ dấu xuống dòng của tin. Thiếu nó thì `innerText` chỉ còn một dòng.
    expect((await bubble.innerText()).includes('\n')).toBe(true);

    // Tám dòng vượt giới hạn năm dòng, nên ô dừng ở `max-height` và cuộn bên trong.
    await composer(page).fill('a\nb\nc\nd\ne\nf\ng\nh');
    const tallBox = await composer(page).evaluate((element) => ({
      height: element.getBoundingClientRect().height,
      maxHeight: Number.parseFloat(getComputedStyle(element).maxHeight),
    }));
    expect(Math.round(tallBox.height)).toBe(COMPOSER_MAX_HEIGHT_PX);
    expect(Math.round(tallBox.maxHeight)).toBe(COMPOSER_MAX_HEIGHT_PX);

    expect(consoleIssues).toEqual([]);
    await context.close();
  });

  test('khách đã đăng nhập mở được /support và gửi tin', async ({ browser }) => {
    const { context, page, consoleIssues } = await openGuestPage(browser);
    // Email riêng của spec để không đụng tài khoản `customerEmail` mà spec auth đăng ký.
    const supportCustomerEmail = 'e2e-support-customer@pickory.local';

    // DB E2E dựng lại mỗi lần chạy và không sẵn tài khoản khách. Đăng ký qua giao diện rồi dùng luôn phiên đó.
    await page.goto('/register');
    await dismissConsent(page);
    await page.getByLabel(/^Email/).fill(supportCustomerEmail);
    await page.getByLabel(/^Mật khẩu/).fill(e2eEnvironment.accountPassword);
    await page.getByLabel(/^Họ và tên/).fill('Khách hỗ trợ E2E');
    await page.getByRole('button', { name: 'Tạo tài khoản', exact: true }).click();
    await expect(page.getByRole('link', { name: 'Tài khoản', exact: true })).toBeVisible();

    await openSupportPage(page);
    await composer(page).fill('Xin chào, mình cần hỗ trợ.');
    await page.getByRole('button', { name: 'Gửi', exact: true }).click();
    await expect(page.getByText('Xin chào, mình cần hỗ trợ.')).toBeVisible();

    expect(consoleIssues).toEqual([]);
    await context.close();
  });

  test('link "Liên hệ CSKH" ở chân trang trang chủ dẫn tới /support', async ({ browser }) => {
    const { context, page, consoleIssues } = await openGuestPage(browser);

    await page.goto('/');
    await dismissConsent(page);
    await page.getByRole('contentinfo').getByRole('link', { name: 'Liên hệ CSKH' }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/support`);
    await expect(page.getByRole('heading', { name: 'Liên hệ CSKH', exact: true })).toBeVisible();

    expect(consoleIssues).toEqual([]);
    await context.close();
  });

  test('ô soạn và nút gửi dùng được chỉ bằng bàn phím, trả focus sau khi gửi', async ({
    browser,
  }) => {
    const { context, page, consoleIssues } = await openGuestPage(browser);

    await openSupportPage(page);
    // Đi bằng Tab thật từ đầu trang tới ô soạn. Giới hạn số lần Tab để test không lặp vô hạn.
    let composerFocused = false;
    for (let step = 0; step < MAX_TAB_TRAVERSAL; step++) {
      await page.keyboard.press('Tab');
      if (await composer(page).evaluate((element) => element === document.activeElement)) {
        composerFocused = true;
        break;
      }
    }
    expect(composerFocused).toBe(true);

    await page.keyboard.type('Mình cần đổi địa chỉ giao hàng.');
    await composer(page).press('Tab');
    await expect(page.getByRole('button', { name: 'Gửi', exact: true })).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(page.getByText('Mình cần đổi địa chỉ giao hàng.')).toBeVisible();
    // Nút "Gửi" bị tắt sau khi gửi nên focus phải quay về ô soạn để khách gõ tiếp.
    await expect(composer(page)).toBeFocused();

    expect(consoleIssues).toEqual([]);
    await context.close();
  });

  test('sự kiện giả: tin nhân viên hiện tên, session.waiting hiện trạng thái chờ', async ({
    browser,
  }) => {
    const { context, page, consoleIssues } = await openGuestPage(browser);
    const conversationId = await createConversationId(page);

    await mockChatEvents(page, conversationId);

    await openSupportPage(page);
    await expect(page.getByText(STAFF_MESSAGE_BODY)).toBeVisible();
    await expect(page.getByText('Lan - CSKH', { exact: true })).toBeVisible();
    await expect(page.getByText('Đang chờ nhân viên', { exact: true })).toBeVisible();

    expect(consoleIssues).toEqual([]);
    await context.close();
  });

  test('mất kết nối: luồng tự nối lại và không hiện tin trùng', async ({ browser }) => {
    const { context, page, consoleIssues } = await openGuestPage(browser);
    const conversationId = await createConversationId(page);

    await mockChatEvents(page, conversationId);
    const streamRequests = countEventStreamRequests(page, conversationId);

    await openSupportPage(page);
    const staffMessages = page.locator('.support-message', { hasText: STAFF_MESSAGE_BODY });
    await expect(staffMessages).toHaveCount(1);

    // Luồng giả đóng sau khi gửi sự kiện. EventSource tự nối lại và nhận lại cùng sự kiện.
    // Gộp theo `id` giữ số tin cùng nội dung bằng 1 dù luồng mở lại nhiều lần.
    // Chờ tới request thứ ba: lúc đó luồng thứ hai chắc chắn đã đóng và trang đã nhận hết sự kiện của nó.
    await expect.poll(() => streamRequests.count(), { timeout: 15_000 }).toBeGreaterThanOrEqual(3);
    await expect(staffMessages).toHaveCount(1);

    expect(consoleIssues).toEqual([]);
    await context.close();
  });

  test('giỏ hàng trống rộng hết khung trang ở 1280 px', async ({ browser }) => {
    const { context, page, consoleIssues } = await openGuestPage(browser);

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto('/cart');
    await dismissConsent(page);
    await expect(page.getByRole('heading', { level: 1, name: 'Giỏ hàng' })).toBeVisible();

    // Khung `main` phải giãn hết khung trang. Cột flex bọc ngoài từng làm nó co theo nội dung.
    const mainBox = await page.locator('main').boundingBox();
    expect(mainBox).not.toBeNull();
    expect(mainBox!.width).toBeGreaterThan(1000);

    expect(consoleIssues).toEqual([]);
    await context.close();
  });
});

// Ảnh bố cục cho review. Dựng sản phẩm một lần để ảnh giỏ hàng, thanh toán, danh mục và sản phẩm có dữ liệu.
test.describe('SD-UI7: ảnh bố cục', () => {
  let catalog: SeededCatalog;

  test.beforeAll(async ({ browser }) => {
    test.setTimeout(180000);
    const context = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    const page = await context.newPage();
    await loginAdmin(page);
    catalog = await seedCatalog(page);
    await context.close();
  });

  test('khung chat trống ở 1280 px và 375 px', async ({ browser }, testInfo) => {
    const { context, page, consoleIssues } = await openGuestPage(browser);

    await openSupportPage(page);
    await expect(page.getByText('Bạn cần hỗ trợ gì?')).toBeVisible();

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.screenshot({
      path: screenshotPath(testInfo, 'support-r3-empty-1280.png'),
      fullPage: true,
    });
    await page.setViewportSize({ width: 375, height: 800 });
    await page.screenshot({
      path: screenshotPath(testInfo, 'support-r3-empty-375.png'),
      fullPage: true,
    });
    await expectNoHorizontalOverflow(page);

    expect(consoleIssues).toEqual([]);
    await context.close();
  });

  test('khung chat có tin khách liên tiếp, tin nhân viên và badge chờ', async ({
    browser,
  }, testInfo) => {
    const { context, page, consoleIssues } = await openGuestPage(browser);
    const conversationId = await createConversationId(page);
    await mockChatEvents(page, conversationId);

    await openSupportPage(page);
    // Bốn tin khách liên tiếp trong cùng một phút để ảnh cho thấy cách gom nhóm và dòng phụ.
    for (const text of [
      'Đơn PK123 của mình giao tới đâu rồi?',
      'Mình đặt hôm qua, chọn giao nhanh.',
      'Cần giao trước 18 giờ hôm nay.',
      'Nếu chưa kịp thì báo mình nhé.',
    ]) {
      await composer(page).fill(text);
      await composer(page).press('Enter');
      await expect(page.getByText(text)).toBeVisible();
    }

    await expect(page.getByText(STAFF_MESSAGE_BODY)).toBeVisible();
    await expect(page.getByText('Đang chờ nhân viên', { exact: true })).toBeVisible();

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.screenshot({
      path: screenshotPath(testInfo, 'support-r3-chat-1280.png'),
      fullPage: true,
    });
    await page.setViewportSize({ width: 375, height: 800 });
    await page.screenshot({
      path: screenshotPath(testInfo, 'support-r3-chat-375.png'),
      fullPage: true,
    });
    await expectNoHorizontalOverflow(page);

    expect(consoleIssues).toEqual([]);
    await context.close();
  });

  test('lỗi 429 hiện trên thanh soạn khi gửi quá nhanh', async ({ browser }, testInfo) => {
    // Case chủ động gửi quá giới hạn, nên chỉ lỗi tải 429 của route gửi tin được phép trong console.
    const allowedFailures: AllowedLoadFailure[] = [
      { urlPattern: /\/api\/support\/conversations\/[^/]+\/messages/, status: 429 },
    ];
    const { context, page, consoleIssues } = await openGuestPage(browser, allowedFailures);

    await openSupportPage(page);
    // Giới hạn là 10 tin mỗi phút. Tin thứ 11 nhận 429 và ErrorAlert hiện trên thanh soạn.
    for (let index = 1; index <= 11; index++) {
      await composer(page).fill(`Tin số ${index}`);
      await page.getByRole('button', { name: 'Gửi', exact: true }).click();
    }
    // Câu của API thật là "Bạn gửi tin quá nhanh. Vui lòng thử lại sau ít phút.".
    // Fixture hợp đồng ghi câu khác, nên test chỉ kiểm phần đầu câu ổn định.
    await expect(page.getByText('Bạn gửi tin quá nhanh.')).toBeVisible();

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.screenshot({
      path: screenshotPath(testInfo, 'support-r3-error-1280.png'),
      fullPage: true,
    });

    expect(consoleIssues).toEqual([]);
    await context.close();
  });

  test('giỏ hàng trống ở 1280 px', async ({ browser }, testInfo) => {
    const { context, page, consoleIssues } = await openGuestPage(browser);

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto('/cart');
    await dismissConsent(page);
    await expect(page.getByRole('heading', { level: 1, name: 'Giỏ hàng' })).toBeVisible();
    await page.screenshot({
      path: screenshotPath(testInfo, 'support-r3-cart-empty-1280.png'),
      fullPage: true,
    });

    expect(consoleIssues).toEqual([]);
    await context.close();
  });

  test('giỏ hàng có hàng và trang thanh toán ở 1280 px', async ({ browser }, testInfo) => {
    const { context, page, consoleIssues } = await openGuestPage(browser);

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto(`/p/${catalog.activeSlug}`);
    await dismissConsent(page);
    await page.getByRole('button', { name: 'Thêm vào giỏ' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Đã thêm vào giỏ' })).toBeVisible();

    await page.goto('/cart');
    await expect(page.getByRole('heading', { level: 1, name: 'Giỏ hàng' })).toBeVisible();
    await expect(page.locator('.cart-line').first()).toBeVisible();
    await page.screenshot({
      path: screenshotPath(testInfo, 'support-r3-cart-filled-1280.png'),
      fullPage: true,
    });

    await page.getByRole('link', { name: 'Tiến hành đặt hàng' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Đặt hàng' })).toBeVisible();
    await page.screenshot({
      path: screenshotPath(testInfo, 'support-r3-checkout-1280.png'),
      fullPage: true,
    });

    expect(consoleIssues).toEqual([]);
    await context.close();
  });

  test('danh mục và trang sản phẩm ở 1280 px', async ({ browser }, testInfo) => {
    const { context, page, consoleIssues } = await openGuestPage(browser);

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto(`/c/${catalog.categorySlug}`);
    await dismissConsent(page);
    await expect(page.locator('.product-card').first()).toBeVisible();
    await page.screenshot({
      path: screenshotPath(testInfo, 'support-r3-category-1280.png'),
      fullPage: true,
    });

    await page.goto(`/p/${catalog.activeSlug}`);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(catalog.activeName);
    await page.screenshot({
      path: screenshotPath(testInfo, 'support-r3-product-1280.png'),
      fullPage: true,
    });

    expect(consoleIssues).toEqual([]);
    await context.close();
  });

  test('trang chủ ở 1280 px sau khi tải xong sản phẩm', async ({ browser }, testInfo) => {
    const { context, page, consoleIssues } = await openGuestPage(browser);

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto('/');
    await dismissConsent(page);
    await expect(page.locator('.product-card').first()).toBeVisible();
    // Cuộn qua các khối để IntersectionObserver gắn lớp hiển thị, không dùng sleep cố định.
    for (const section of await page.locator('.reveal').all()) {
      await section.scrollIntoViewIfNeeded();
    }
    await expect(page.locator('.reveal:not(.is-revealed)')).toHaveCount(0);
    await page.evaluate(() => window.scrollTo(0, 0));
    await expect(page.getByRole('contentinfo')).toBeVisible();
    await page.screenshot({
      path: screenshotPath(testInfo, 'support-r3-home-1280.png'),
      fullPage: true,
    });

    expect(consoleIssues).toEqual([]);
    await context.close();
  });
});

test.describe('SD-UI8: một mã khách vãng lai cho mọi request đầu trang', () => {
  test('mở thẳng /support bằng context mới nhận đúng 1 cookie pk_anon', async ({ browser }) => {
    const { context, page, consoleIssues } = await openGuestPage(browser);
    let anonSetCookieCount = 0;
    const responsePromises: Promise<void>[] = [];

    // Đếm số lần server phản hồi header Set-Cookie đặt pk_anon.
    page.on('response', (response) => {
      responsePromises.push(
        (async () => {
          const headers = await response.headersArray();
          for (const header of headers) {
            if (header.name.toLowerCase() === 'set-cookie' && header.value.includes('pk_anon=')) {
              anonSetCookieCount += 1;
            }
          }
        })(),
      );
    });

    await page.goto('/support');
    await dismissConsent(page);
    await expect(page.getByRole('heading', { name: 'Liên hệ CSKH', exact: true })).toBeVisible();
    await expect(composer(page)).toBeVisible();

    // Chờ các request đầu trang và luồng sự kiện hoàn tất trước khi chốt số cookie.
    await page.waitForLoadState('networkidle');
    await Promise.all(responsePromises);

    // Mọi response của lần tải đầu chỉ được đặt đúng một cookie pk_anon.
    expect(anonSetCookieCount).toBe(1);

    const cookies = await context.cookies();
    const anonCookies = cookies.filter((candidate) => candidate.name === 'pk_anon');
    expect(anonCookies).toHaveLength(1);
    expect(consoleIssues).toEqual([]);

    await context.close();
  });

  test('mở thẳng trang chủ bằng context mới nhận đúng 1 cookie pk_anon', async ({ browser }) => {
    const { context, page, consoleIssues } = await openGuestPage(browser);
    let anonSetCookieCount = 0;
    const responsePromises: Promise<void>[] = [];

    // Đếm số lần server phản hồi header Set-Cookie đặt pk_anon.
    page.on('response', (response) => {
      responsePromises.push(
        (async () => {
          const headers = await response.headersArray();
          for (const header of headers) {
            if (header.name.toLowerCase() === 'set-cookie' && header.value.includes('pk_anon=')) {
              anonSetCookieCount += 1;
            }
          }
        })(),
      );
    });

    await page.goto('/');
    await dismissConsent(page);

    // Chờ danh sách sản phẩm hiển thị để các request đầu trang chạy đủ.
    await expect(page.locator('.product-card').first()).toBeVisible();
    await page.waitForLoadState('networkidle');
    await Promise.all(responsePromises);

    // Mọi response của lần tải đầu chỉ được đặt đúng một cookie pk_anon.
    expect(anonSetCookieCount).toBe(1);

    const cookies = await context.cookies();
    const anonCookies = cookies.filter((candidate) => candidate.name === 'pk_anon');
    expect(anonCookies).toHaveLength(1);
    expect(consoleIssues).toEqual([]);

    await context.close();
  });
});

test.describe('SD-UI6: form số điện thoại và form CSAT phía khách', () => {
  test('ảnh bố cục hai form trong khung chat ở 1280 px và 375 px không tràn ngang', async ({
    browser,
  }, testInfo) => {
    const { context, page, consoleIssues } = await openGuestPage(browser);
    const conversationId = await createConversationId(page);
    await mockChatEvents(page, conversationId);

    await openSupportPage(page);

    // Chờ hai form hiển thị từ luồng tin nhắn hệ thống.
    await expect(page.locator('[data-action="callback_form"]')).toBeVisible();
    await expect(page.locator('[data-action="csat_form"]')).toBeVisible();

    await page.setViewportSize({ width: 1280, height: 900 });

    const callbackCard = page.locator('[data-action="callback_form"]');
    const csatCard = page.locator('[data-action="csat_form"]');
    const callbackBox = await callbackCard.boundingBox();
    const csatBox = await csatCard.boundingBox();

    // Đo bề rộng hai thẻ tại 1280 px bảo đảm kích thước bằng nhau.
    expect(callbackBox?.width).toBe(420);
    expect(csatBox?.width).toBe(420);

    await page.screenshot({
      path: screenshotPath(testInfo, 'support-sdui6-chat-forms-1280.png'),
      fullPage: true,
    });

    await page.setViewportSize({ width: 375, height: 800 });
    await page.screenshot({
      path: screenshotPath(testInfo, 'support-sdui6-chat-forms-375.png'),
      fullPage: true,
    });
    // Kiểm tra không tràn ngang ở chiều rộng di động 375 px.
    await expectNoHorizontalOverflow(page);

    expect(consoleIssues).toEqual([]);
    await context.close();
  });

  test('form số điện thoại kiểm bằng Zod, hiện lỗi bằng FieldError và đổi thành lời xác nhận khi gửi thành công', async ({
    browser,
  }, testInfo) => {
    const { context, page, consoleIssues } = await openGuestPage(browser);
    const conversationId = await createConversationId(page);

    const routes = createSupportDeskRouteMocks({ page, apiOrigin: e2eEnvironment.apiOrigin });
    // Dùng route giả từ ví dụ hợp đồng theo quy định SD-C2.
    await routes.mockRoute('customer-api.json', 'khách để lại số điện thoại');
    await routes.mockEventStream({
      path: eventsPath(conversationId),
      fixtureFile: 'customer-events.json',
      eventNames: ['message.created'],
    });

    await openSupportPage(page);

    const callbackForm = page.locator('[data-action="callback_form"]');
    await expect(callbackForm).toBeVisible();

    const phoneField = callbackForm.locator('input[name="phone"]');
    const submitBtn = callbackForm.locator('button[type="submit"]');

    // Bấm gửi khi số trống để kích hoạt validation Zod.
    await submitBtn.click();

    // Lỗi bắt buộc phải hiển thị dưới ô bằng FieldError.
    const errorId = await phoneField.getAttribute('aria-describedby');
    expect(errorId).not.toBeNull();
    const phoneError = callbackForm.locator(`[id="${errorId}"]`);
    await expect(phoneError).toHaveText('Số điện thoại không được để trống.');
    await expect(phoneField).toHaveAttribute('aria-invalid', 'true');
    // Nhập đầu số sai định dạng của di động Việt Nam.
    await phoneField.fill('0123456789');
    await submitBtn.click();
    await expect(phoneError).toHaveText('Số điện thoại không đúng định dạng.');

    // Nhập số điện thoại hợp lệ và ghi chú theo ví dụ mẫu.
    await phoneField.fill('0901234567');
    await callbackForm.locator('textarea[name="note"]').fill('Gọi sau 18 giờ giúp mình.');
    await submitBtn.click();

    // Gửi thành công thì form đổi thành thông báo xác nhận.
    const successAlert = callbackForm.locator('.alert-ok');
    await expect(successAlert).toBeVisible();
    await expect(successAlert).toContainText('Yêu cầu gọi lại đã được ghi nhận');

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.screenshot({
      path: screenshotPath(testInfo, 'support-sdui6-callback-success-1280.png'),
      fullPage: true,
    });

    await page.setViewportSize({ width: 375, height: 800 });
    await page.screenshot({
      path: screenshotPath(testInfo, 'support-sdui6-callback-success-375.png'),
      fullPage: true,
    });
    await expectNoHorizontalOverflow(page);

    expect(consoleIssues).toEqual([]);
    await context.close();
  });

  test('lỗi 429 CALLBACK_LIMIT_REACHED hiện thông báo tiếng Việt và form không gửi lại', async ({
    browser,
  }, testInfo) => {
    // Cho phép mã lỗi 429 có chủ đích từ API route giả.
    const allowedFailures: AllowedLoadFailure[] = [
      { urlPattern: /\/api\/support\/callback-requests/, status: 429 },
    ];
    const { context, page, consoleIssues } = await openGuestPage(browser, allowedFailures);
    const conversationId = await createConversationId(page);

    const routes = createSupportDeskRouteMocks({ page, apiOrigin: e2eEnvironment.apiOrigin });
    await routes.mockRoute('customer-api.json', 'yêu cầu gọi lại thứ 4 trong ngày');
    await routes.mockEventStream({
      path: eventsPath(conversationId),
      fixtureFile: 'customer-events.json',
      eventNames: ['message.created'],
    });

    await openSupportPage(page);

    const callbackForm = page.locator('[data-action="callback_form"]');
    await expect(callbackForm).toBeVisible();

    await callbackForm.locator('input[name="phone"]').fill('0901234567');
    const submitBtn = callbackForm.locator('button[type="submit"]');
    await submitBtn.click();

    // Thông báo lỗi tiếng Việt hiển thị từ response của API.
    const alertBody = callbackForm.locator('.alert-list');
    await expect(alertBody).toContainText(
      'Bạn đã gửi 3 yêu cầu gọi lại hôm nay. CSKH sẽ liên hệ sớm.',
    );

    // Nút gửi bị khóa sau khi nhận lỗi 429.
    await expect(submitBtn).toBeDisabled();

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.screenshot({
      path: screenshotPath(testInfo, 'support-sdui6-callback-429-1280.png'),
      fullPage: true,
    });

    await page.setViewportSize({ width: 375, height: 800 });
    await page.screenshot({
      path: screenshotPath(testInfo, 'support-sdui6-callback-429-375.png'),
      fullPage: true,
    });
    await expectNoHorizontalOverflow(page);

    expect(consoleIssues).toEqual([]);
    await context.close();
  });

  test('form CSAT có 5 nút chọn điểm dùng được bằng bàn phím, mỗi nút có tên truy cập', async ({
    browser,
  }, testInfo) => {
    const { context, page, consoleIssues } = await openGuestPage(browser);
    const conversationId = await createConversationId(page);

    const routes = createSupportDeskRouteMocks({ page, apiOrigin: e2eEnvironment.apiOrigin });
    await routes.mockRoute('customer-api.json', 'khách chấm điểm phiên vừa kết thúc');
    await routes.mockEventStream({
      path: eventsPath(conversationId),
      fixtureFile: 'customer-events.json',
      eventNames: ['message.created'],
    });

    await openSupportPage(page);

    const csatForm = page.locator('[data-action="csat_form"]');
    await expect(csatForm).toBeVisible();

    const scoreButtons = csatForm.locator('.csat-score-btn');
    await expect(scoreButtons).toHaveCount(5);

    // Kiểm tra tên truy cập đầy đủ của từng nút điểm.
    const expectedLabels = [
      '1 điểm - Rất không hài lòng',
      '2 điểm - Không hài lòng',
      '3 điểm - Bình thường',
      '4 điểm - Hài lòng',
      '5 điểm - Rất hài lòng',
    ];
    for (let i = 0; i < expectedLabels.length; i++) {
      const label = expectedLabels[i]!;
      await expect(scoreButtons.nth(i)).toHaveAttribute('aria-label', label);
    }

    // Dùng bàn phím để chọn điểm 5 rất hài lòng.
    await scoreButtons.nth(4).focus();
    await page.keyboard.press('Enter');
    await expect(scoreButtons.nth(4)).toHaveAttribute('aria-pressed', 'true');

    // Nhấn gửi đánh giá sau khi đã chọn điểm.
    const submitBtn = csatForm.locator('button[type="submit"]');
    await submitBtn.click();

    // Form đổi thành thông báo xác nhận có chứa điểm đã chọn.
    const successAlert = csatForm.locator('.alert-ok');
    await expect(successAlert).toBeVisible();
    await expect(successAlert).toContainText('5/5');
    await expect(successAlert).toContainText('Rất hài lòng');

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.screenshot({
      path: screenshotPath(testInfo, 'support-sdui6-csat-success-1280.png'),
      fullPage: true,
    });

    await page.setViewportSize({ width: 375, height: 800 });
    await page.screenshot({
      path: screenshotPath(testInfo, 'support-sdui6-csat-success-375.png'),
      fullPage: true,
    });
    await expectNoHorizontalOverflow(page);

    expect(consoleIssues).toEqual([]);
    await context.close();
  });
});
