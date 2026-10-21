import { expect, test, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';

const SEARCH_LABEL = 'Tên hoặc mã SKU';

// Trang không được rộng hơn khung nhìn. Bảng rộng phải cuộn trong khung riêng của nó.
async function expectNoHorizontalOverflow(page: Page) {
  const widths = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth,
  }));
  expect(widths.content).toBeLessThanOrEqual(widths.viewport);
}

async function loginAsAdmin(page: Page) {
  await page.goto('/admin/login');
  const consent = page.getByRole('button', { name: 'Chỉ cần thiết', exact: true });
  if (await consent.isVisible()) await consent.click();
  await page.getByLabel(/^Email/).fill(e2eEnvironment.adminEmail);
  await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.adminPassword);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin`);
}

// Dừng đồng hồ của trang. Lấy giờ từ chính trang vì đồng hồ giả đã chạy qua `runFor` nên lệch khỏi giờ máy.
async function pauseClock(page: Page) {
  const pageNow = await page.evaluate(() => Date.now());
  await page.clock.pauseAt(new Date(pageNow + 1000));
}

// Mỗi bước mở lại danh sách sạch, để từ khóa của bước trước không lọt sang bước sau.
async function openProductList(page: Page) {
  await page.goto('/admin/products');
  await expect(
    page.getByRole('heading', { level: 1, name: 'Sản phẩm', exact: true }),
  ).toBeVisible();
  return page.getByLabel(SEARCH_LABEL, { exact: true });
}

test('FLT-UI5: ô tìm kiếm có nút xóa, phím Enter và phím Esc', async ({ page }, testInfo) => {
  test.setTimeout(120000);
  // Ghi lỗi console và lỗi trang. Cả spec phải có 0 lỗi và 0 cảnh báo.
  const consoleIssues: string[] = [];
  page.on('console', (message) => {
    // Bỏ qua lỗi tải `favicon.ico`. Chromium có giao diện tự gọi tệp này, còn ứng dụng chưa có favicon.
    if (message.location().url.endsWith('/favicon.ico')) return;
    if (message.type() === 'error' || message.type() === 'warning') {
      consoleIssues.push(`[${message.type()}] ${message.text()}`);
    }
  });
  page.on('pageerror', (error) => consoleIssues.push(`[pageerror] ${error.message}`));

  // Đếm request danh sách sản phẩm theo từ khóa. Mỗi lần gõ phải tạo đúng một request cho từ khóa đó.
  const keywordRequests: string[] = [];
  page.on('request', (request) => {
    const url = new URL(request.url());
    if (request.method() === 'GET' && url.pathname === '/api/admin/products') {
      keywordRequests.push(url.searchParams.get('search') ?? '');
    }
  });

  await loginAsAdmin(page);
  await page.setViewportSize({ width: 1280, height: 800 });

  await test.step('Enter gửi từ khóa ngay, chỉ một request cho từ khóa đó', async () => {
    // Đồng hồ giả phải cài trước `goto`, để trang nạp với đồng hồ này. Các bước sau dùng lại đồng hồ đã cài.
    await page.clock.install();
    const searchBox = await openProductList(page);
    keywordRequests.length = 0;

    // Dừng đồng hồ trước khi gõ. Lần hẹn giờ 300 ms của ô không thể chạy khi đồng hồ dừng, dù máy nhanh hay chậm.
    await pauseClock(page);
    await searchBox.fill('dien');
    await searchBox.press('Enter');

    // Đồng hồ đang dừng mà URL đã có từ khóa, nghĩa là Enter đã gửi ngay chứ không chờ hẹn giờ.
    await expect(page).toHaveURL(/[?&]search=dien(&|$)/);
    // Chạy tiếp qua 300 ms của lần hẹn giờ cũ. Lần hẹn đó mà còn sống thì sẽ gọi `onSearch` thêm một lần ở đây.
    await page.clock.runFor(600);
    // Trả đồng hồ về chạy thật để TanStack Query nạp xong kết quả, rồi mới đếm request.
    await page.clock.resume();
    await expect.poll(() => keywordRequests.filter((search) => search === 'dien').length).toBe(1);
  });

  await test.step('Bấm nút Xóa từ khóa: ô rỗng, URL hết search, focus về ô', async () => {
    const searchBox = await openProductList(page);
    await searchBox.fill('dien');
    await expect(page).toHaveURL(/[?&]search=dien(&|$)/);

    await page.getByRole('button', { name: 'Xóa từ khóa', exact: true }).click();

    await expect(searchBox).toHaveValue('');
    await expect(page).not.toHaveURL(/[?&]search=/);
    await expect(searchBox).toBeFocused();
    // Ô rỗng thì nút xóa biến mất.
    await expect(page.getByRole('button', { name: 'Xóa từ khóa', exact: true })).toHaveCount(0);
  });

  await test.step('Nút Xóa từ khóa dùng được bằng bàn phím: Tab tới nút rồi bấm Enter', async () => {
    const searchBox = await openProductList(page);
    await searchBox.fill('dien');
    await expect(page).toHaveURL(/[?&]search=dien(&|$)/);

    // Tab đầu tiên từ ô tìm đi tới nút xóa, vì nút nằm ngay sau ô trong thứ tự focus.
    await searchBox.press('Tab');
    const clearButton = page.getByRole('button', { name: 'Xóa từ khóa', exact: true });
    await expect(clearButton).toBeFocused();
    await clearButton.press('Enter');

    await expect(searchBox).toHaveValue('');
    await expect(page).not.toHaveURL(/[?&]search=/);
    await expect(searchBox).toBeFocused();
  });

  await test.step('Gõ rồi bấm Esc: ô rỗng, URL không có search', async () => {
    const searchBox = await openProductList(page);
    keywordRequests.length = 0;

    // Dừng đồng hồ trước khi gõ, để lần hẹn giờ 300 ms của chữ vừa gõ chưa thể chạy trước khi Esc xóa ô.
    await pauseClock(page);
    await searchBox.fill('dien');
    await searchBox.press('Escape');

    await expect(searchBox).toHaveValue('');
    // Chạy tiếp qua 300 ms của lần hẹn giờ cũ. Lần hẹn đó mà còn sống thì sẽ đặt `search=dien` lên URL.
    await page.clock.runFor(600);
    await page.clock.resume();
    await expect(page).not.toHaveURL(/[?&]search=/);
    expect(keywordRequests.filter((search) => search === 'dien')).toHaveLength(0);
  });

  await test.step('Nút xóa riêng của trình duyệt bị ẩn', async () => {
    // `getComputedStyle(input, '::-webkit-search-cancel-button')` luôn trả `auto` trong Chromium, kể cả khi rule đã có.
    // Nên spec so ảnh chụp vùng bên phải ô: có chữ và có chữ nhưng không có nút gốc phải giống hệt nhau.
    const searchBox = await openProductList(page);
    await searchBox.fill('dien');
    const clearButton = page.getByRole('button', { name: 'Xóa từ khóa', exact: true });
    // Ẩn nút của ứng dụng, để vùng chụp chỉ còn lại nút gốc của trình duyệt nếu nó có hiện.
    await clearButton.evaluate((button) => (button.style.visibility = 'hidden'));
    const inputBox = await searchBox.boundingBox();
    expect(inputBox).not.toBeNull();
    // Nút gốc nằm trong vùng nội dung của ô, ngay bên trái phần `padding-right` 40px. Vùng chụp phủ cả hai chỗ.
    const rightEdge = {
      x: inputBox!.x + inputBox!.width - 72,
      y: inputBox!.y + 4,
      width: 66,
      height: inputBox!.height - 8,
    };

    // Nút gốc chỉ hiện khi ô có chữ và đang được trỏ hoặc có focus. Ô đang có focus sau khi gõ.
    await expect(searchBox).toBeFocused();
    const withText = await page.screenshot({ clip: rightEdge });

    // Cùng ô, cùng focus, nhưng rỗng. Lúc này nút gốc chắc chắn không hiện.
    await searchBox.fill('');
    await expect(searchBox).toBeFocused();
    const withoutText = await page.screenshot({ clip: rightEdge });

    expect(withText.equals(withoutText)).toBe(true);
  });

  for (const width of [1280, 375]) {
    await test.step(`Ảnh chụp ô có chữ ở ${width}px không tràn ngang`, async () => {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      const searchBox = await openProductList(page);
      await searchBox.fill('dien thoai');
      await expect(page).toHaveURL(/[?&]search=dien/);

      const clearButton = page.getByRole('button', { name: 'Xóa từ khóa', exact: true });
      await expect(clearButton).toBeVisible();
      await expectNoHorizontalOverflow(page);

      // Vùng chữ của ô kết thúc ở `padding-right` tính từ mép phải ô. Mép trái của nút phải nằm ở đó hoặc xa hơn, để nút không đè chữ.
      const [inputBox, buttonBox] = [
        await searchBox.boundingBox(),
        await clearButton.boundingBox(),
      ];
      expect(inputBox).not.toBeNull();
      expect(buttonBox).not.toBeNull();
      const paddingRight = await searchBox.evaluate((input) =>
        Number.parseFloat(getComputedStyle(input).paddingRight),
      );
      expect(inputBox!.x + inputBox!.width - paddingRight).toBeLessThanOrEqual(buttonBox!.x);
      expect(buttonBox!.x).toBeGreaterThanOrEqual(inputBox!.x);
      expect(buttonBox!.x + buttonBox!.width).toBeLessThanOrEqual(inputBox!.x + inputBox!.width);
      expect(buttonBox!.width).toBeGreaterThanOrEqual(24);
      expect(buttonBox!.height).toBeGreaterThanOrEqual(24);

      await page.screenshot({
        path: testInfo.outputPath(`products-search-input-${width}.png`),
        fullPage: true,
      });
    });
  }

  expect(consoleIssues).toEqual([]);
});
