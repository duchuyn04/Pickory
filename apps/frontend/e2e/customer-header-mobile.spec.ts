import { expect, test, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';

// Kiểm tra trang không bị tràn ngang ở mọi kích thước màn hình.
async function expectNoHorizontalOverflow(page: Page) {
  const widths = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth,
  }));
  expect(widths.content).toBeLessThanOrEqual(widths.viewport);
}

// Tắt banner đồng ý cookie nếu banner xuất hiện trên trang.
async function dismissConsent(page: Page) {
  const consentButton = page.getByRole('button', { name: 'Chỉ cần thiết', exact: true });
  if (await consentButton.isVisible()) {
    await consentButton.click();
    await expect(consentButton).toBeHidden();
  }
}

test('DS-07: header của khách trên mobile có menu ngăn kéo và vùng bấm đạt chuẩn', async ({
  page,
}, testInfo) => {
  test.setTimeout(120000);
  const consoleIssues: string[] = [];
  page.on('console', (message) => {
    if (message.location().url.endsWith('/favicon.ico')) return;
    if (message.type() === 'error' || message.type() === 'warning') {
      consoleIssues.push(`[${message.type()}] ${message.text()}`);
    }
  });
  page.on('pageerror', (error) => consoleIssues.push(`[pageerror] ${error.message}`));

  await test.step('Màn hình rộng 1280px: thanh ngang đầy đủ, nút menu ẩn', async () => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/');
    await dismissConsent(page);

    const menuButton = page.getByRole('button', { name: 'Menu', exact: true });
    // Nút Menu burger phải ẩn trên màn hình máy tính để bàn.
    await expect(menuButton).toBeHidden();

    const mainNav = page.getByRole('navigation', { name: 'Điều hướng chính' });
    await expect(mainNav).toBeVisible();
    await expect(mainNav.getByRole('link', { name: 'Trang chủ' })).toBeVisible();
    await expect(mainNav.getByRole('link', { name: 'Tra cứu đơn' })).toBeVisible();
    await expect(mainNav.getByRole('link', { name: 'Quyền riêng tư' })).toBeVisible();

    const cartButton = page.getByRole('button', { name: /^Giỏ hàng/ });
    await expect(cartButton).toBeVisible();

    await expectNoHorizontalOverflow(page);
    await page.screenshot({
      path: testInfo.outputPath('header-desktop-1280.png'),
    });
  });

  await test.step('Màn hình hẹp 375px: header có 1 hàng gồm nút Menu, logo và giỏ hàng', async () => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto('/');
    await dismissConsent(page);

    const menuButton = page.getByRole('button', { name: 'Menu', exact: true });
    await expect(menuButton).toBeVisible();
    await expect(menuButton).toHaveAttribute('aria-expanded', 'false');
    await expect(menuButton).toHaveAttribute('aria-controls', 'site-menu-panel');

    // Nút menu đạt kích thước tối thiểu 44 x 44 px để người dùng chạm dễ dàng.
    const menuBox = await menuButton.boundingBox();
    expect(menuBox?.width).toBeGreaterThanOrEqual(44);
    expect(menuBox?.height).toBeGreaterThanOrEqual(44);

    const logo = page.locator('.site-logo');
    await expect(logo).toBeVisible();

    const cartButton = page.getByRole('button', { name: /^Giỏ hàng/ });
    await expect(cartButton).toBeVisible();
    const cartBox = await cartButton.boundingBox();
    expect(cartBox?.width).toBeGreaterThanOrEqual(44);
    expect(cartBox?.height).toBeGreaterThanOrEqual(44);

    // Kiểm tra ba phần tử cùng nằm trên 1 hàng duy nhất trong khung nhìn.
    expect(Math.abs((menuBox?.y ?? 0) - (cartBox?.y ?? 0))).toBeLessThanOrEqual(10);

    // Khi ngăn đóng, hộp thoại menu không nhận trạng thái dialog mở.
    await expect(page.getByRole('dialog', { name: 'Menu điều hướng' })).toHaveCount(0);
    await expectNoHorizontalOverflow(page);

    await page.screenshot({
      path: testInfo.outputPath('header-mobile-closed-375.png'),
    });
  });

  await test.step('Mở ngăn menu: focus trong ngăn, Escape đóng và trả focus về nút Menu', async () => {
    const menuButton = page.getByRole('button', { name: 'Menu', exact: true });
    await menuButton.click();
    await expect(menuButton).toHaveAttribute('aria-expanded', 'true');

    const drawer = page.getByRole('dialog', { name: 'Menu điều hướng' });
    await expect(drawer).toBeVisible();

    // Tiêu điểm phải chuyển vào bên trong ngăn menu khi ngăn mở ra.
    const closeButton = page.getByRole('button', { name: 'Đóng menu', exact: true });
    await expect(closeButton).toBeFocused();
    await expect(closeButton).toBeVisible();
    const closeBox = await closeButton.boundingBox();
    expect(closeBox?.width).toBeGreaterThanOrEqual(44);
    expect(closeBox?.height).toBeGreaterThanOrEqual(44);

    // Kiểm tra các dòng trong ngăn đạt chiều cao tối thiểu 44 px.
    const navLinks = drawer.getByRole('link');
    const linkCount = await navLinks.count();
    expect(linkCount).toBeGreaterThanOrEqual(3);
    for (let i = 0; i < linkCount; i += 1) {
      const box = await navLinks.nth(i).boundingBox();
      expect(box?.height).toBeGreaterThanOrEqual(44);
    }

    await page.screenshot({
      path: testInfo.outputPath('header-mobile-open-375.png'),
    });

    // Nhấn phím Escape để đóng ngăn kéo và trả focus về nút Menu ban đầu.
    await page.keyboard.press('Escape');
    await expect(menuButton).toHaveAttribute('aria-expanded', 'false');
    await expect(page.getByRole('dialog', { name: 'Menu điều hướng' })).toHaveCount(0);
    await expect(menuButton).toBeFocused();
  });

  await test.step('Bấm nền mờ đóng ngăn menu', async () => {
    const menuButton = page.getByRole('button', { name: 'Menu', exact: true });
    await menuButton.click();
    const scrim = page.locator('[data-site-menu-scrim]');
    await expect(scrim).toBeVisible();

    // Bấm vào lớp nền mờ ở vùng bên phải ngoài ngăn kéo để đóng menu.
    await page.mouse.click(350, 400);
    await expect(menuButton).toHaveAttribute('aria-expanded', 'false');
    await expect(page.getByRole('dialog', { name: 'Menu điều hướng' })).toHaveCount(0);
  });

  await test.step('Bấm liên kết trong ngăn đóng menu và chuyển trang đúng đích', async () => {
    const menuButton = page.getByRole('button', { name: 'Menu', exact: true });
    await menuButton.click();
    const drawer = page.getByRole('dialog', { name: 'Menu điều hướng' });
    await drawer.getByRole('link', { name: 'Tra cứu đơn' }).click();

    await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/order-lookup`);
    await expect(page.getByRole('heading', { level: 1, name: 'Tra cứu đơn hàng' })).toBeVisible();
    await expect(menuButton).toHaveAttribute('aria-expanded', 'false');
    await expect(page.getByRole('dialog', { name: 'Menu điều hướng' })).toHaveCount(0);
    await expectNoHorizontalOverflow(page);
  });

  await test.step('Mini cart mở ở 375px không làm tràn trang', async () => {
    await page.goto('/');
    const cartButton = page.getByRole('button', { name: /^Giỏ hàng/ });
    await cartButton.click();
    const cartPanel = page.getByRole('dialog', { name: 'Giỏ hàng' });
    await expect(cartPanel).toBeVisible();
    await expectNoHorizontalOverflow(page);

    await page.screenshot({
      path: testInfo.outputPath('mini-cart-mobile-375.png'),
    });

    const closeCart = cartPanel.getByRole('button', { name: 'Đóng giỏ hàng nhỏ' });
    await closeCart.click();
    await expect(cartPanel).toBeHidden();
  });

  expect(consoleIssues).toEqual([]);
});
