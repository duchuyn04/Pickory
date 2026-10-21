import { expect, test, type Page } from '@playwright/test';
import { trackEventRequests } from './activity-tracking-fixtures';
import { e2eEnvironment } from './environment';
import {
  collectConsoleIssues,
  dismissConsent,
  loginAdmin,
  seedCatalog,
  stubProductImages,
  type SeededCatalog,
} from './catalog-product-seed';

// Chờ thêm 1 giây sau khi trang đã hiện. Lần chạy thứ hai của StrictMode hoặc request thừa sẽ lộ ra trong khoảng này.
async function waitForLateRequests(page: Page) {
  await page.waitForTimeout(1000);
}

test.describe('ACT-UI1: trang sản phẩm gửi lượt xem', () => {
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

  test('mở sản phẩm gửi đúng 1 request, nhận 204 và console sạch (tiêu chí 1, 2, 7)', async ({
    page,
  }) => {
    const issues = collectConsoleIssues(page);
    const events = trackEventRequests(page);
    await stubProductImages(page);
    await page.goto(`/p/${catalog.activeSlug}`);
    await dismissConsent(page);

    await expect(page.getByRole('heading', { level: 1 })).toHaveText(catalog.activeName);
    await expect.poll(() => events.filter((event) => event.status !== null).length).toBe(1);
    await waitForLateRequests(page);

    // Dev server chạy `StrictMode`, nên effect chạy hai lần. Cờ trong hook giữ số request ở mức 1.
    expect(events).toHaveLength(1);
    expect(JSON.parse(events[0]?.body ?? 'null')).toEqual({ type: 'product_view' });
    // Response 204 chứng minh API nhận header `Origin` của trình duyệt thật.
    expect(events[0]?.status).toBe(204);
    expect(issues).toEqual([]);
  });

  test('đổi biến thể trên cùng trang không gửi thêm request (tiêu chí 3)', async ({ page }) => {
    const events = trackEventRequests(page);
    await stubProductImages(page);
    await page.goto(`/p/${catalog.activeSlug}?sku=${catalog.skuIds.black256}`);
    await dismissConsent(page);
    await expect(page.locator('.product-price strong')).toHaveText('20.000.000 đ');
    await expect.poll(() => events.length).toBe(1);

    await page.getByRole('group', { name: 'Màu' }).getByRole('radio', { name: 'Bạc' }).click();
    await expect(page.locator('.product-price strong')).toHaveText('21.000.000 đ');
    await expect(page).toHaveURL(new RegExp(`\\?sku=${catalog.skuIds.silver256}$`));
    await waitForLateRequests(page);

    expect(events).toHaveLength(1);
  });

  test('sang sản phẩm khác qua link trong trang gửi thêm đúng 1 request (tiêu chí 4)', async ({
    page,
  }) => {
    const events = trackEventRequests(page);
    await stubProductImages(page);
    await page.goto(`/p/${catalog.activeSlug}`);
    await dismissConsent(page);
    await expect.poll(() => events.length).toBe(1);

    // Breadcrumb dẫn về trang danh mục. Trang danh mục không phải trang sản phẩm nên không gửi lượt xem.
    const crumbs = page.getByRole('navigation', { name: 'Đường dẫn' });
    await crumbs.getByRole('link', { name: catalog.categoryName }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/c/${catalog.categorySlug}`);
    await page.locator(`a[href="/p/${catalog.sparseSlug}"]`).first().click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/p/${catalog.sparseSlug}`);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(
      `Laptop Thưa C2 ${catalog.suffix}`,
    );
    await expect.poll(() => events.filter((event) => event.status !== null).length).toBe(2);
    await waitForLateRequests(page);

    expect(events).toHaveLength(2);
    expect(events.map((event) => event.status)).toEqual([204, 204]);
  });

  test('quay lại sản phẩm đã xem tính thêm đúng 1 lượt, dù dữ liệu có sẵn trong cache (tiêu chí 2)', async ({
    page,
  }) => {
    const events = trackEventRequests(page);
    await stubProductImages(page);
    await page.goto(`/p/${catalog.activeSlug}`);
    await dismissConsent(page);
    await expect.poll(() => events.length).toBe(1);

    await page
      .getByRole('navigation', { name: 'Đường dẫn' })
      .getByRole('link', { name: catalog.categoryName })
      .click();
    await page.locator(`a[href="/p/${catalog.sparseSlug}"]`).first().click();
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(
      `Laptop Thưa C2 ${catalog.suffix}`,
    );
    await expect.poll(() => events.length).toBe(2);

    // Nút Back đưa khách về sản phẩm đầu. Dữ liệu sản phẩm có sẵn trong cache nên effect chạy ngay lúc mount, và `StrictMode` của dev chạy nó lần hai.
    await page.goBack();
    await page.goBack();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/p/${catalog.activeSlug}`);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(catalog.activeName);
    await expect.poll(() => events.length).toBe(3);
    await waitForLateRequests(page);

    expect(events).toHaveLength(3);
  });

  test('slug lạ hiện Không tìm thấy sản phẩm và không gửi request (tiêu chí 5)', async ({
    page,
  }) => {
    const events = trackEventRequests(page);
    await page.goto('/p/san-pham-khong-ton-tai');
    await dismissConsent(page);

    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Không tìm thấy sản phẩm');
    await waitForLateRequests(page);

    expect(events).toHaveLength(0);
  });

  test('request lượt xem bị chặn thì trang vẫn dùng được và không có pageerror (tiêu chí 6)', async ({
    page,
  }) => {
    const pageErrors: string[] = [];
    page.on('pageerror', (error) => pageErrors.push(error.message));
    const events = trackEventRequests(page);
    await stubProductImages(page);
    // Giả lập mất mạng ở đúng route lượt xem. Các API khác của trang vẫn chạy thật.
    await page.route('**/api/events', (route) => route.abort());
    await page.goto(`/p/${catalog.activeSlug}`);
    await dismissConsent(page);

    await expect(page.getByRole('heading', { level: 1 })).toHaveText(catalog.activeName);
    await expect.poll(() => events.length).toBe(1);
    await waitForLateRequests(page);

    await expect(page.getByRole('alert')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Thêm vào giỏ' })).toBeEnabled();
    expect(pageErrors).toEqual([]);
  });
});
