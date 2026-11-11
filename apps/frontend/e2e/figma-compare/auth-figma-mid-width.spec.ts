import { expect, test, type Page } from '@playwright/test';

// Ca kiểm bố cục màn đăng nhập và đăng ký ở độ rộng giữa điện thoại và máy tính. Không so ảnh.
const MID_WIDTH_ROUTES = [
  { route: '/login', emailLabel: 'Email' },
  { route: '/register', emailLabel: 'Email *' },
  { route: '/admin/login', emailLabel: 'Email nhân viên' },
];

async function openAuthRoute(page: Page, route: string, width: number, emailLabel: string) {
  await page.setViewportSize({ width, height: 800 });
  await page.addInitScript(() => localStorage.setItem('pk_consent_chosen', 'true'));
  await page.goto(route);
  const email = page.getByLabel(emailLabel, { exact: true });
  await expect(email).toBeVisible();
  const widths = await page.evaluate(() => ({
    scroll: document.documentElement.scrollWidth,
    inner: window.innerWidth,
  }));
  expect(widths.scroll, `trang cuộn ngang ở ${width}px`).toBeLessThanOrEqual(widths.inner);
  return email;
}

for (const { route, emailLabel } of MID_WIDTH_ROUTES) {
  test(`${route} ở 900x800: không cuộn ngang, ô Email đủ rộng, nút Hiện bấm được`, async ({
    page,
  }) => {
    const email = await openAuthRoute(page, route, 900, emailLabel);
    const emailBox = await email.boundingBox();
    expect(emailBox?.width ?? 0, 'ô Email bị bóp ở 900px').toBeGreaterThanOrEqual(300);

    // Điểm cách hộp nút "Hiện" 8px phía trên phải rơi vào chính nút, nhờ vùng bấm mở rộng.
    const toggle = page.getByRole('button', { name: 'Hiện', exact: true });
    const toggleBox = await toggle.boundingBox();
    if (!toggleBox) throw new Error('Không thấy nút Hiện');
    const hitText = await page.evaluate(
      ([x, y]) => document.elementFromPoint(x ?? 0, y ?? 0)?.textContent,
      [toggleBox.x + toggleBox.width / 2, toggleBox.y - 8],
    );
    expect(hitText).toBe('Hiện');
  });

  // 1024px là độ rộng nhỏ nhất dùng bố cục chia đôi: 560 + 400 + 2 x 24 = 1008px nên ô Email đủ 400px.
  test(`${route} ở 1024x800: không cuộn ngang, ô Email rộng 400px`, async ({ page }) => {
    const email = await openAuthRoute(page, route, 1024, emailLabel);
    const emailBox = await email.boundingBox();
    expect(emailBox?.width ?? 0, 'ô Email không đủ 400px ở 1024px').toBeGreaterThanOrEqual(399);
  });
}
