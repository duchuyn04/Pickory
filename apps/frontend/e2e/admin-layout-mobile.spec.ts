import { expect, test } from '@playwright/test';
import { e2eEnvironment } from './environment';

test('Menu quản trị: thanh trên cùng ở 375px, menu trái ở 1280px', async ({ page }, testInfo) => {
  // Ghi lỗi console và lỗi trang. Mỗi trang trong spec phải có 0 lỗi và 0 cảnh báo.
  const consoleIssues: string[] = [];
  page.on('console', (message) => {
    // Bỏ qua lỗi tải `favicon.ico`. Chromium có giao diện tự gọi tệp này, còn ứng dụng chưa có favicon.
    const isFaviconRequest = message.location().url.endsWith('/favicon.ico');
    if (isFaviconRequest) return;
    if (message.type() === 'error' || message.type() === 'warning') {
      consoleIssues.push(`[${message.type()}] ${message.text()}`);
    }
  });
  page.on('pageerror', (error) => {
    consoleIssues.push(`[pageerror] ${error.message}`);
  });

  await test.step('Admin đăng nhập ở 1280px', async () => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/admin/login');
    const cookieButton = page.getByRole('button', { name: 'Chỉ cần thiết', exact: true });
    if (await cookieButton.isVisible()) {
      await cookieButton.click();
    }
    await page.getByLabel(/^Email/).fill(e2eEnvironment.adminEmail);
    await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.adminPassword);
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin`);
  });

  await test.step('Ở 1280px, menu trái hiện sẵn và không có nút Menu', async () => {
    const menuToggle = page.getByRole('button', { name: 'Menu', exact: true });
    await expect(page.getByRole('link', { name: 'Danh mục', exact: true })).toBeVisible();
    await expect(menuToggle).toBeHidden();

    // Menu trái nằm bên trái vùng nội dung, nên vùng nội dung bắt đầu sau 250px của menu.
    const mainBox = await page.getByRole('main').boundingBox();
    expect(mainBox?.x ?? 0).toBeGreaterThanOrEqual(250);

    // Ở màn rộng, lề trái của mục menu khớp với lề trái của thanh đầu trang.
    const headerPaddingLeft = await page
      .locator('.admin-sidebar-header')
      .evaluate((element) => getComputedStyle(element).paddingLeft);
    const linkPaddingLeft = await page
      .getByRole('link', { name: 'Danh mục', exact: true })
      .evaluate((element) => getComputedStyle(element).paddingLeft);
    expect(headerPaddingLeft).toBe('24px');
    expect(linkPaddingLeft).toBe('24px');
  });
  await test.step('Ở 375px, menu thu thành thanh trên cùng có nút Menu', async () => {
    await page.setViewportSize({ width: 375, height: 812 });
    const menuToggle = page.getByRole('button', { name: 'Menu', exact: true });
    const categoriesLink = page.getByRole('link', { name: 'Danh mục', exact: true });

    // Menu trái chiếm gần nửa màn 375px. Vùng nội dung cần ít nhất 300px để đọc được.
    const mainBox = await page.getByRole('main').boundingBox();
    expect(mainBox?.width ?? 0).toBeGreaterThanOrEqual(300);

    // Menu đóng mặc định. Nút Menu báo trạng thái và trỏ tới danh sách nó điều khiển.
    await expect(menuToggle).toBeVisible();
    await expect(menuToggle).toHaveAttribute('aria-expanded', 'false');
    const controlledId = await menuToggle.getAttribute('aria-controls');
    expect(controlledId).toBeTruthy();
    await expect(categoriesLink).toBeHidden();

    // Bấm Menu thì danh sách mở ra dưới thanh trên cùng.
    await menuToggle.click();
    await expect(menuToggle).toHaveAttribute('aria-expanded', 'true');
    await expect(page.locator(`#${controlledId}`)).toBeVisible();
    await expect(categoriesLink).toBeVisible();
    const toggleBox = await menuToggle.boundingBox();
    const linkBox = await categoriesLink.boundingBox();
    expect(linkBox?.y ?? 0).toBeGreaterThan(toggleBox?.y ?? 0);
    // Ở màn hẹp, lề trái của mục menu phải bằng lề trái của thanh đầu trang.
    const mobileHeaderPaddingLeft = await page
      .locator('.admin-sidebar-header')
      .evaluate((element) => getComputedStyle(element).paddingLeft);
    const mobileLinkPaddingLeft = await categoriesLink.evaluate(
      (element) => getComputedStyle(element).paddingLeft,
    );
    expect(mobileLinkPaddingLeft).toBe(mobileHeaderPaddingLeft);
    await page.screenshot({
      path: testInfo.outputPath('admin-menu-open-mobile.png'),
      fullPage: true,
    });

    // Bấm một mục thì sang trang đó và menu đóng lại để nội dung không bị che.
    await categoriesLink.click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/categories`);
    await expect(menuToggle).toHaveAttribute('aria-expanded', 'false');
    await expect(categoriesLink).toBeHidden();
  });

  expect(consoleIssues).toEqual([]);
});
