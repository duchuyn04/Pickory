import { expect, test, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';
import {
  collectConsoleIssues,
  dismissConsent,
  loginAdmin,
  seedCatalog,
  stubProductImages,
  type SeededCatalog,
} from './catalog-product-seed';

// Thư mục lưu ảnh chụp màn hình khi reviewer cần xem. Không đặt biến này thì spec không ghi ảnh.
const screenshotDirectory = process.env.UI_C2_SHOTS_DIR;

async function saveScreenshot(page: Page, fileName: string, fullPage = true) {
  if (!screenshotDirectory) return;
  await page.screenshot({ path: `${screenshotDirectory}/${fileName}`, fullPage });
}

// Đo tràn ngang của cả trang. Rộng hơn viewport nghĩa là người dùng phải cuộn ngang.
async function expectNoHorizontalPageOverflow(page: Page) {
  const widths = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(widths.scrollWidth).toBeLessThanOrEqual(widths.clientWidth);
}

test.describe('UI-C2: trang sản phẩm của khách', () => {
  let catalog: SeededCatalog;

  test.beforeAll(async ({ browser }) => {
    // Tạo dữ liệu một lần cho cả nhóm test, vì mỗi sản phẩm cần nhiều lệnh gọi API.
    test.setTimeout(180000);
    const context = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
    const page = await context.newPage();
    await loginAdmin(page);
    catalog = await seedCatalog(page);
    await context.close();
  });

  test('chọn biến thể khác thì giá, giá niêm yết, tồn kho và ảnh đổi đúng (tiêu chí 1)', async ({
    page,
    browser,
  }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    // API không đảm bảo thứ tự SKU tạo cùng lúc. Vì vậy test chọn SKU bằng ?sku= thay vì dựa vào SKU mặc định.
    await page.goto(`/p/${catalog.activeSlug}?sku=${catalog.skuIds.black256}`);
    await dismissConsent(page);

    const price = page.locator('.product-price strong');
    const listPrice = page.locator('.product-price-list');
    const stock = page.locator('.product-buy .badge');
    const mainImage = page.locator('.gallery-main img');

    // SKU đang mở: Đen 256GB, còn hàng, không có giá niêm yết, ảnh riêng của màu đen.
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(catalog.activeName);
    await expect(price).toHaveText('20.000.000 đ');
    await expect(listPrice).toHaveCount(0);
    await expect(stock).toHaveText('Còn hàng');
    await expect(mainImage).toHaveAttribute('src', new RegExp(`e2e-c2-${catalog.suffix}-den`));
    await saveScreenshot(page, 'product-1280-default.png');

    // Đổi sang màu Bạc: giá, giá niêm yết, phần trăm giảm và ảnh riêng của màu bạc.
    await page.getByRole('group', { name: 'Màu' }).getByRole('radio', { name: 'Bạc' }).click();
    await expect(price).toHaveText('21.000.000 đ');
    await expect(listPrice).toHaveText('25.000.000 đ');
    await expect(page.locator('.product-discount')).toHaveText('-16%');
    await expect(mainImage).toHaveAttribute('src', new RegExp(`e2e-c2-${catalog.suffix}-bac`));
    await expect(page).toHaveURL(new RegExp(`\\?sku=${catalog.skuIds.silver256}$`));
    await saveScreenshot(page, 'product-1280-silver.png');

    // Sang bản Đen 512GB: hết hàng, giá khác, ảnh đen trở lại.
    await page.getByRole('group', { name: 'Màu' }).getByRole('radio', { name: 'Đen' }).click();
    await page.getByRole('group', { name: 'Bộ nhớ' }).getByRole('radio', { name: '512GB' }).click();
    await expect(price).toHaveText('24.000.000 đ');
    await expect(stock).toHaveText('Hết hàng');
    await expect(listPrice).toHaveCount(0);
    await expect(mainImage).toHaveAttribute('src', new RegExp(`e2e-c2-${catalog.suffix}-den`));
    await saveScreenshot(page, 'product-1280-out-of-stock.png');

    // Ảnh nhỏ: ảnh của màu đang chọn đứng trước ảnh chung, ảnh riêng của màu bạc bị ẩn.
    const thumbs = page.locator('.gallery-thumbs button');
    await expect(thumbs).toHaveCount(2);
    await expect(thumbs.nth(0)).toHaveAttribute('aria-label', /Ảnh màu đen/);
    await expect(thumbs.nth(1)).toHaveAttribute('aria-label', /Ảnh chung/);
    await thumbs.nth(1).click();
    await expect(mainImage).toHaveAttribute('src', new RegExp(`e2e-c2-${catalog.suffix}-chung`));
    await expect(thumbs.nth(1)).toHaveAttribute('aria-pressed', 'true');

    // Mở lại URL có ?sku= trong trang mới: phải thấy đúng biến thể đã chia sẻ.
    const sharedUrl = `/p/${catalog.activeSlug}?sku=${catalog.skuIds.silver512}`;
    const sharedContext = await browser.newContext({ viewport: { width: 1280, height: 800 } });
    const sharedPage = await sharedContext.newPage();
    const sharedIssues = collectConsoleIssues(sharedPage);
    await stubProductImages(sharedPage);
    await sharedPage.goto(`${e2eEnvironment.webOrigin}${sharedUrl}`);
    await expect(sharedPage.locator('.product-price strong')).toHaveText('26.000.000 đ');
    await expect(
      sharedPage.getByRole('group', { name: 'Màu' }).getByRole('radio', { name: 'Bạc' }),
    ).toBeChecked();
    await expect(
      sharedPage.getByRole('group', { name: 'Bộ nhớ' }).getByRole('radio', { name: '512GB' }),
    ).toBeChecked();
    expect(sharedIssues).toEqual([]);
    await sharedContext.close();

    expect(issues).toEqual([]);
  });

  test('tiêu đề mô tả thành h2, h3, h4 và chữ đậm, nghiêng hiện đúng (tiêu chí 2 và 3)', async ({
    page,
  }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto(`/p/${catalog.activeSlug}`);
    await dismissConsent(page);

    // Cả trang chỉ có một h1.
    await expect(page.getByRole('heading', { level: 1 })).toHaveCount(1);

    const description = page.locator('.description-body');
    await expect(description.locator('h2')).toHaveText('Tiêu đề cấp một');
    await expect(description.locator('h3')).toHaveText('Tiêu đề cấp hai');
    await expect(description.locator('h4')).toHaveText('Tiêu đề cấp ba');
    await expect(description.locator('h1')).toHaveCount(0);
    await expect(description.locator('strong')).toHaveText('Chữ đậm');
    await expect(description.locator('em')).toHaveText('chữ nghiêng');

    // Bảng thông số chia hai nhóm, giữ thứ tự khai báo và hiện Có cho boolean.
    const specGroups = page.locator('.spec-group h3');
    await expect(specGroups).toHaveText(['Cấu hình', 'Hiển thị']);
    await expect(page.locator('.spec-table tr', { hasText: 'RAM' })).toContainText('16 GB');
    await expect(page.locator('.spec-table tr', { hasText: 'Màn hình' })).toContainText('14 inch');
    await expect(page.locator('.spec-table tr', { hasText: 'Cảm ứng' })).toContainText('Có');

    expect(issues).toEqual([]);
  });

  test('tài liệu chứa nút lạ không hiện nội dung của nút và không chạy script (tiêu chí 4)', async ({
    page,
  }) => {
    const issues = collectConsoleIssues(page);
    const dialogs: string[] = [];
    // Nếu script hay onerror chạy được `alert`, hộp thoại này bắt được và test thất bại.
    page.on('dialog', async (dialog) => {
      dialogs.push(dialog.message());
      await dialog.dismiss();
    });
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });

    const textPayload = '<img src=x onerror=alert(1)>';
    await page.route(`**/api/products/${catalog.activeSlug}`, async (route) => {
      const response = await route.fetch();
      const body = await response.json();
      // Thay mô tả bằng tài liệu độc: nút `script`, nút `image` có `onerror`, mark lạ và text giống thẻ HTML.
      body.data.description = {
        type: 'doc',
        content: [
          { type: 'script', content: [{ type: 'text', text: 'window.__xss = "script"' }] },
          { type: 'image', attrs: { src: 'x', onerror: 'window.__xss = "onerror"' } },
          {
            type: 'paragraph',
            content: [
              { type: 'text', text: textPayload },
              {
                type: 'text',
                text: ' liên kết lạ',
                marks: [{ type: 'link', attrs: { href: 'javascript:window.__xss = "link"' } }],
              },
            ],
          },
        ],
      };
      await route.fulfill({ response, json: body });
    });

    await page.goto(`/p/${catalog.activeSlug}`);
    await dismissConsent(page);

    const description = page.locator('.description-body');
    await expect(description).toBeVisible();
    await expect(description.locator('script')).toHaveCount(0);
    await expect(description.locator('img')).toHaveCount(0);
    await expect(description.locator('a')).toHaveCount(0);
    await expect(description.locator('[onerror]')).toHaveCount(0);
    // Nội dung con của nút `script` không hiện. Chuỗi giống HTML hiện nguyên văn dạng chữ.
    await expect(description).not.toContainText('window.__xss');
    await expect(description).toContainText(textPayload);
    await expect(description).toContainText('liên kết lạ');

    // Chờ thêm một nhịp để `onerror` kịp chạy nếu có thẻ ảnh bị tạo ra.
    await page.waitForTimeout(300);
    expect(await page.evaluate(() => (window as unknown as { __xss?: string }).__xss)).toBe(
      undefined,
    );
    expect(dialogs).toEqual([]);
    expect(issues).toEqual([]);
  });

  test('sản phẩm ngừng kinh doanh hiện nhãn, tắt nút mua và không đổi được biến thể (tiêu chí 6)', async ({
    page,
  }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto(`/p/${catalog.discontinuedSlug}`);
    await dismissConsent(page);

    await expect(page.locator('.product-title .badge')).toHaveText('Ngừng kinh doanh');
    await expect(page.getByText('Sản phẩm đã ngừng kinh doanh.')).toBeVisible();
    // Nhãn tồn kho mâu thuẫn với dòng ngừng kinh doanh, nên trang không hiện.
    await expect(page.getByText('Còn hàng')).toHaveCount(0);
    await expect(page.getByText('Hết hàng')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Thêm vào giỏ' })).toBeDisabled();

    // Mọi lựa chọn đều tắt, nên khách chỉ xem được biến thể mặc định.
    const radios = page.getByRole('radio');
    await expect(radios).toHaveCount(2);
    for (const radio of await radios.all()) await expect(radio).toBeDisabled();
    await expect(page).not.toHaveURL(/sku=/);
    await saveScreenshot(page, 'product-1280-discontinued.png');

    expect(issues).toEqual([]);
  });

  test('không cuộn ngang ở viewport 375 px (tiêu chí 7)', async ({ page }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto(`/p/${catalog.activeSlug}?sku=${catalog.skuIds.black256}`);
    await dismissConsent(page);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(catalog.activeName);

    await expectNoHorizontalPageOverflow(page);
    // Ảnh chỉ trong viewport để mắt thấy đúng thứ khách thấy, vì header dính có thể đè lên ảnh toàn trang.
    await saveScreenshot(page, 'product-375-default-viewport.png', false);
    await saveScreenshot(page, 'product-375-default-full.png');

    await page.getByRole('group', { name: 'Màu' }).getByRole('radio', { name: 'Bạc' }).click();
    await expect(page.locator('.product-price strong')).toHaveText('21.000.000 đ');
    await expectNoHorizontalPageOverflow(page);
    await saveScreenshot(page, 'product-375-silver-viewport.png', false);

    await page.goto(`/p/${catalog.discontinuedSlug}`);
    await expect(page.locator('.product-title .badge')).toHaveText('Ngừng kinh doanh');
    await expectNoHorizontalPageOverflow(page);
    await saveScreenshot(page, 'product-375-discontinued-viewport.png', false);

    await page.goto('/p/san-pham-khong-ton-tai');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Không tìm thấy sản phẩm');
    await expectNoHorizontalPageOverflow(page);
    await saveScreenshot(page, 'product-375-not-found-viewport.png', false);

    expect(issues).toEqual([]);
  });

  test('tới được SKU không chung giá trị nào với SKU đang chọn (tiêu chí 1)', async ({ page }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto(`/p/${catalog.sparseSlug}`);
    await dismissConsent(page);

    const colorGroup = page.getByRole('group', { name: 'Màu' });
    const memoryGroup = page.getByRole('group', { name: 'Bộ nhớ' });
    // Chọn Đen 256GB trước, vì thứ tự SKU mặc định của API không cố định.
    await colorGroup.getByRole('radio', { name: 'Đen' }).click();
    await expect(memoryGroup.getByRole('radio', { name: '256GB' })).toBeChecked();
    await expect(page.locator('.product-price strong')).toHaveText('18.000.000 đ');

    // Bạc chỉ có bản 512GB. Bấm Bạc phải đổi cả bộ nhớ sang 512GB.
    const silver = colorGroup.getByRole('radio', { name: /^Bạc/ });
    await expect(silver).toBeEnabled();
    await silver.click();
    await expect(colorGroup.getByRole('radio', { name: /^Bạc/ })).toBeChecked();
    await expect(memoryGroup.getByRole('radio', { name: /^512GB/ })).toBeChecked();
    await expect(page.locator('.product-price strong')).toHaveText('29.000.000 đ');
    await saveScreenshot(page, 'product-1280-sparse-switch.png');
    expect(issues).toEqual([]);
  });

  test('slug cũ chuyển sang slug mới và giữ query string', async ({ page }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    expect(catalog.oldSlug).not.toBe(catalog.newSlug);
    await page.goto(`/p/${catalog.oldSlug}?ref=mail`);
    await dismissConsent(page);

    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/p/${catalog.newSlug}?ref=mail`);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(
      `Laptop Tên Mới C2 ${catalog.suffix}`,
    );
    // Sản phẩm không có tùy chọn: không có bộ chọn biến thể.
    await expect(page.getByRole('radio')).toHaveCount(0);
    expect(issues).toEqual([]);
  });

  test('slug lạ hiện Không tìm thấy sản phẩm kèm link về trang chủ', async ({ page }) => {
    const issues = collectConsoleIssues(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/p/san-pham-khong-ton-tai');
    await dismissConsent(page);

    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Không tìm thấy sản phẩm');
    await page.getByRole('link', { name: 'Về trang chủ' }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/`);
    await saveScreenshot(page, 'product-1280-not-found.png');
    expect(issues).toEqual([]);
  });

  test('breadcrumb link về danh mục và nút so sánh thêm, bỏ, giữ sau khi tải lại', async ({
    page,
  }) => {
    const issues = collectConsoleIssues(page);
    await stubProductImages(page);
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto(`/p/${catalog.activeSlug}`);
    await dismissConsent(page);

    const crumbs = page.getByRole('navigation', { name: 'Đường dẫn' });
    await expect(crumbs.getByRole('link', { name: catalog.categoryName })).toHaveAttribute(
      'href',
      `/c/${catalog.categorySlug}`,
    );

    const compareButton = page.getByRole('button', { name: /so sánh$/ });
    const compareCount = page.locator('.compare-count');
    await expect(compareButton).toHaveText('Thêm vào so sánh');
    await expect(compareCount).toHaveText('Danh sách so sánh: 0/4');
    await expect(compareButton).toHaveAttribute('aria-pressed', 'false');
    await compareButton.click();
    await expect(compareButton).toHaveText('Bỏ khỏi so sánh');
    await expect(compareCount).toHaveText('Danh sách so sánh: 1/4');
    await expect(compareButton).toHaveAttribute('aria-pressed', 'true');

    // Tải lại trang: danh sách đọc từ localStorage nên số lượng và trạng thái nút còn nguyên.
    await page.reload();
    await expect(compareButton).toHaveText('Bỏ khỏi so sánh');
    await expect(compareCount).toHaveText('Danh sách so sánh: 1/4');
    await expect(compareButton).toHaveAttribute('aria-pressed', 'true');

    await compareButton.click();
    await expect(compareButton).toHaveText('Thêm vào so sánh');
    await expect(compareCount).toHaveText('Danh sách so sánh: 0/4');
    await expect(compareButton).toHaveAttribute('aria-pressed', 'false');

    // COM-UI1 đã bật nút giỏ cho SKU còn hàng và bỏ chú thích "Sắp có".
    await expect(page.getByRole('button', { name: 'Thêm vào giỏ' })).toBeEnabled();
    await expect(page.getByText('Sắp có')).toHaveCount(0);
    expect(issues).toEqual([]);
  });
});
