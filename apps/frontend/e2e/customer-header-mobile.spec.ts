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

test('DS-07: header hai tầng của khách và ngăn kéo menu trên di động đạt chuẩn', async ({
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

  await test.step('Màn hình máy tính 1440px: đủ hai tầng, nút Danh mục mở bảng và đóng bằng Escape', async () => {
    await page.setViewportSize({ width: 1440, height: 800 });
    await page.goto('/');

    // Giới hạn phạm vi trong `<header>` để tránh trùng chữ cam kết ở nội dung trang chủ.
    const banner = page.getByRole('banner');
    const menuButton = banner.getByRole('button', { name: 'Menu', exact: true });
    // Nút Menu chỉ dùng ở màn hình điện thoại dưới 768px.
    await expect(menuButton).toBeHidden();

    await expect(
      banner.getByText('Bảo hành ghi rõ trên từng sản phẩm', { exact: true }),
    ).toBeVisible();
    await expect(
      banner.getByText('Phí giao hàng tính theo địa chỉ nhận', { exact: true }),
    ).toBeVisible();

    const mainNav = banner.getByRole('navigation', { name: 'Điều hướng chính' });
    await expect(mainNav).toBeVisible();
    await expect(mainNav.getByRole('link', { name: 'Trang chủ', exact: true })).toBeVisible();
    await expect(
      mainNav.getByRole('link', { name: 'Tra cứu đơn hàng', exact: true }),
    ).toBeVisible();
    await expect(mainNav.getByRole('link', { name: 'Phí giao hàng', exact: true })).toBeVisible();
    await expect(mainNav.getByRole('link', { name: 'Quyền riêng tư', exact: true })).toBeVisible();

    await expect(banner.getByRole('link', { name: 'Pickory', exact: true })).toBeVisible();
    const categoryButton = banner.getByRole('button', { name: 'Danh mục', exact: true });
    await expect(categoryButton).toBeVisible();
    await expect(banner.getByRole('search')).toBeVisible();
    await expect(banner.getByRole('searchbox', { name: 'Tìm kiếm sản phẩm' })).toBeVisible();
    await expect(banner.getByRole('link', { name: 'Đăng nhập', exact: true })).toBeVisible();
    await expect(banner.getByRole('link', { name: 'Đăng ký', exact: true })).toBeVisible();
    await expect(banner.getByRole('button', { name: /^Giỏ hàng/ })).toBeVisible();

    // Bảng danh mục phải đóng bằng phím Escape và trả tiêu điểm về nút Danh mục.
    await categoryButton.click();
    await expect(categoryButton).toHaveAttribute('aria-expanded', 'true');
    await page.keyboard.press('Escape');
    await expect(categoryButton).toHaveAttribute('aria-expanded', 'false');
    await expect(categoryButton).toBeFocused();

    await expectNoHorizontalOverflow(page);
    await page.screenshot({
      path: testInfo.outputPath('header-desktop-1440.png'),
    });
  });

  await test.step('Màn hình máy tính bảng 768px: giữ nút Menu mở ngăn và các phần tử trên một hàng', async () => {
    await page.setViewportSize({ width: 768, height: 900 });
    await page.goto('/');

    const banner = page.getByRole('banner');
    const menuButton = banner.getByRole('button', { name: 'Menu', exact: true });
    await expect(menuButton).toBeVisible();
    const menuBox = await menuButton.boundingBox();
    expect(menuBox?.width).toBeGreaterThanOrEqual(44);
    expect(menuBox?.height).toBeGreaterThanOrEqual(44);

    await expect(banner.getByRole('link', { name: 'Pickory', exact: true })).toBeVisible();
    await expect(banner.getByRole('button', { name: 'Danh mục', exact: true })).toBeVisible();
    await expect(banner.getByRole('searchbox', { name: 'Tìm kiếm sản phẩm' })).toBeVisible();
    await expect(banner.getByRole('button', { name: /^Giỏ hàng/ })).toBeVisible();

    await expectNoHorizontalOverflow(page);
    await page.screenshot({
      path: testInfo.outputPath('header-tablet-768.png'),
    });

    // Mở ngăn menu ở 768px để kiểm lối vào tài khoản và liên kết nhanh, rồi đóng bằng Escape.
    await menuButton.click();
    const drawer = page.getByRole('dialog', { name: 'Menu điều hướng' });
    await expect(drawer).toBeVisible();
    await expect(drawer.getByRole('link', { name: 'Đăng nhập', exact: true })).toBeVisible();
    await expect(drawer.getByRole('link', { name: 'Tra cứu đơn hàng', exact: true })).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog', { name: 'Menu điều hướng' })).toHaveCount(0);
    await expect(menuButton).toBeFocused();
  });

  await test.step('Màn hình điện thoại 375px: một hàng gồm nút Menu, logo, tìm kiếm và giỏ hàng', async () => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto('/');

    const menuButton = page.getByRole('button', { name: 'Menu', exact: true });
    await expect(menuButton).toBeVisible();
    await expect(menuButton).toHaveAttribute('aria-expanded', 'false');
    await expect(menuButton).toHaveAttribute('aria-controls', 'site-menu-panel');

    // Nút menu đạt kích thước tối thiểu 44 x 44 px để người dùng chạm dễ dàng.
    const menuBox = await menuButton.boundingBox();
    expect(menuBox?.width).toBeGreaterThanOrEqual(44);
    expect(menuBox?.height).toBeGreaterThanOrEqual(44);

    const logo = page.getByRole('link', { name: 'Pickory', exact: true });
    await expect(logo).toBeVisible();

    await expect(page.getByRole('button', { name: 'Danh mục', exact: true })).toBeHidden();
    const searchBox = page.getByRole('searchbox', { name: 'Tìm kiếm sản phẩm' });
    await expect(searchBox).toBeVisible();
    const searchRect = await searchBox.boundingBox();
    expect(searchRect?.width).toBeGreaterThan(100);

    const cartButton = page.getByRole('button', { name: /^Giỏ hàng/ });
    await expect(cartButton).toBeVisible();
    const cartBox = await cartButton.boundingBox();
    expect(cartBox?.width).toBeGreaterThanOrEqual(44);
    expect(cartBox?.height).toBeGreaterThanOrEqual(44);

    // Kiểm tra các phần tử chính cùng nằm trên một hàng duy nhất trong khung nhìn.
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

    // Kiểm tra các dòng liên kết trong ngăn đạt chiều cao tối thiểu 44 px.
    const navLinks = drawer.getByRole('link');
    const linkCount = await navLinks.count();
    expect(linkCount).toBeGreaterThanOrEqual(4);
    for (let i = 0; i < linkCount; i += 1) {
      const box = await navLinks.nth(i).boundingBox();
      expect(box?.height).toBeGreaterThanOrEqual(44);
    }

    await page.screenshot({
      path: testInfo.outputPath('header-mobile-open-375.png'),
    });

    // Nhấn phím Escape để đóng ngăn menu và trả focus về nút Menu ban đầu.
    await page.keyboard.press('Escape');
    await expect(menuButton).toHaveAttribute('aria-expanded', 'false');
    await expect(page.getByRole('dialog', { name: 'Menu điều hướng' })).toHaveCount(0);
    await expect(menuButton).toBeFocused();
  });

  await test.step('Bấm ra ngoài ngăn menu đóng ngăn menu', async () => {
    const menuButton = page.getByRole('button', { name: 'Menu', exact: true });
    await menuButton.click();
    const drawer = page.getByRole('dialog', { name: 'Menu điều hướng' });
    await expect(drawer).toBeVisible();

    // Bấm vào vùng bên phải ngoài ngăn menu để đóng ngăn.
    await page.mouse.click(350, 400);
    await expect(menuButton).toHaveAttribute('aria-expanded', 'false');
    await expect(page.getByRole('dialog', { name: 'Menu điều hướng' })).toHaveCount(0);
  });

  await test.step('Bấm liên kết trong ngăn đóng menu và chuyển trang đúng đích', async () => {
    const menuButton = page.getByRole('button', { name: 'Menu', exact: true });
    await menuButton.click();
    const drawer = page.getByRole('dialog', { name: 'Menu điều hướng' });
    await drawer.getByRole('link', { name: 'Tra cứu đơn hàng', exact: true }).click();

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
