import { expect, test, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';

// Trang không được rộng hơn khung nhìn. Bảng rộng phải cuộn trong khung riêng của nó.
async function expectNoHorizontalOverflow(page: Page) {
  const widths = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth,
  }));
  expect(widths.content).toBeLessThanOrEqual(widths.viewport);
}

test('Khung quản trị: sidebar thu gọn ở 1280px, ngăn kéo menu ở 375px', async ({
  page,
}, testInfo) => {
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

  const menuToggle = page.getByRole('button', { name: 'Menu', exact: true });
  const categoriesLink = page.getByRole('link', { name: 'Danh mục', exact: true });
  const collapseButton = page.getByRole('button', { name: 'Thu gọn menu', exact: true });

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

  await test.step('Ở 1280px, sidebar hiện sẵn, có nhóm menu và đánh dấu trang đang mở', async () => {
    await expect(categoriesLink).toBeVisible();
    await expect(menuToggle).toBeHidden();
    await expect(page.getByText('Bán hàng', { exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Tổng quan', exact: true })).toHaveAttribute(
      'aria-current',
      'page',
    );

    // Sidebar rộng 252px nằm bên trái vùng nội dung.
    const mainBox = await page.getByRole('main').boundingBox();
    expect(mainBox?.x ?? 0).toBeGreaterThanOrEqual(250);
    await page.screenshot({ path: testInfo.outputPath('admin-shell-desktop.png') });
  });

  await test.step('Nút thu gọn đổi sidebar thành dải icon và nhớ lựa chọn sau khi tải lại', async () => {
    await collapseButton.click();
    await expect(collapseButton).toHaveAttribute('aria-pressed', 'true');
    // Dải icon rộng 68px, nên vùng nội dung bắt đầu gần mép trái hơn.
    await expect
      .poll(async () => (await page.getByRole('main').boundingBox())?.x ?? 0)
      .toBeLessThan(100);
    // Chữ chỉ ẩn bằng mắt. Link vẫn có tên truy cập và vẫn bấm được.
    await expect(categoriesLink).toBeVisible();
    await page.screenshot({ path: testInfo.outputPath('admin-shell-collapsed.png') });

    await page.reload();
    await expect(collapseButton).toHaveAttribute('aria-pressed', 'true');
    await categoriesLink.click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/categories`);
    await expect(categoriesLink).toHaveAttribute('aria-current', 'page');

    await collapseButton.click();
    await expect(collapseButton).toHaveAttribute('aria-pressed', 'false');
  });

  await test.step('Ở 375px, sidebar thành ngăn kéo mở bằng nút Menu', async () => {
    await page.setViewportSize({ width: 375, height: 812 });

    // Ngăn kéo đóng không chiếm chỗ, nên vùng nội dung dùng gần hết chiều rộng màn.
    const mainBox = await page.getByRole('main').boundingBox();
    expect(mainBox?.width ?? 0).toBeGreaterThanOrEqual(340);
    await expectNoHorizontalOverflow(page);

    // Ngăn kéo đóng mặc định. Nút Menu báo trạng thái và trỏ tới sidebar nó điều khiển.
    await expect(menuToggle).toBeVisible();
    await expect(menuToggle).toHaveAttribute('aria-expanded', 'false');
    const controlledId = await menuToggle.getAttribute('aria-controls');
    expect(controlledId).toBeTruthy();
    await expect(categoriesLink).toBeHidden();
    await expect(collapseButton).toBeHidden();

    await menuToggle.click();
    await expect(menuToggle).toHaveAttribute('aria-expanded', 'true');
    await expect(page.locator(`#${controlledId}`)).toBeVisible();
    await expect(categoriesLink).toBeVisible();
    await expectNoHorizontalOverflow(page);
    await page.screenshot({
      path: testInfo.outputPath('admin-menu-open-mobile.png'),
      fullPage: true,
    });
  });

  await test.step('Phím Escape đóng ngăn kéo và trả focus về nút Menu', async () => {
    await page.keyboard.press('Escape');
    await expect(menuToggle).toHaveAttribute('aria-expanded', 'false');
    await expect(categoriesLink).toBeHidden();
    await expect(menuToggle).toBeFocused();

    // Mở lại chỉ bằng bàn phím: nút Menu đang có focus nhận phím Enter.
    await page.keyboard.press('Enter');
    await expect(menuToggle).toHaveAttribute('aria-expanded', 'true');
    await expect(categoriesLink).toBeVisible();
  });

  await test.step('Chọn một trang thì sang trang đó và ngăn kéo đóng lại', async () => {
    await page.getByRole('link', { name: 'Thương hiệu', exact: true }).click();
    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin/brands`);
    await expect(menuToggle).toHaveAttribute('aria-expanded', 'false');
    await expect(categoriesLink).toBeHidden();
    await expectNoHorizontalOverflow(page);
  });

  expect(consoleIssues).toEqual([]);
});
