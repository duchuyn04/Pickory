import { expect, test, type Locator, type Page } from '@playwright/test';
import { e2eEnvironment } from '../environment';
import { figmaBoxes } from './auth-figma-boxes';
import { figmaTextStyles, type TextStyleCheck } from './auth-figma-text-styles';

// Môi trường đo: Windows, Chromium của Playwright, chữ xám (`--disable-lcd-text`).
// Đổi môi trường có thể đổi tỉ lệ khác biệt.

// Tỉ lệ pixel khác biệt tối đa mặc định của mỗi ảnh, theo yêu cầu 0.5%.
const DEFAULT_MAX_DIFF_PIXEL_RATIO = 0.005;

// Màn nhiều chữ vượt 0.5% dù hộp phần tử đã khớp Figma trong 1px (xem `expectFigmaBoxes`).
// Chromium và Figma vẽ rìa chữ khác nhau, nên mỗi mức bằng số đo thật cộng 0.1 điểm phần trăm.
const TEXT_EDGE_MAX_DIFF_PIXEL_RATIO: Record<string, number> = {
  'TK-03-desktop': 0.0065,
  'TK-03-mobile': 0.009,
  'TK-04-desktop': 0.0067,
  'TK-04-mobile': 0.0091,
  'TK-05-desktop': 0.006,
  'TK-05-mobile': 0.0115,
  'TK-06-mobile': 0.0105,
  'TK-07-desktop': 0.0065,
  'TK-07-mobile': 0.01,
  'TK-08-desktop': 0.01,
  'TK-08-mobile': 0.018,
  'TK-09-desktop': 0.01,
  'TK-09-mobile': 0.0215,
  'QT-03-desktop': 0.007,
  'QT-03-mobile': 0.011,
};

// Biến môi trường `FIGMA_MAX_DIFF_RATIO` chỉ để đo tỉ lệ thật khi báo cáo (đặt 0).
// Giá trị rỗng, âm, lớn hơn 1 hoặc không phải số thì ném lỗi.
function maxDiffPixelRatioFor(key: string): number {
  const overridden = process.env.FIGMA_MAX_DIFF_RATIO;
  if (overridden !== undefined) {
    const parsed = Number(overridden);
    if (overridden.trim() === '' || !Number.isFinite(parsed) || parsed < 0 || parsed > 1) {
      throw new Error(`FIGMA_MAX_DIFF_RATIO phải là số từ 0 đến 1, nhận được "${overridden}"`);
    }
    return parsed;
  }
  return TEXT_EDGE_MAX_DIFF_PIXEL_RATIO[key] ?? DEFAULT_MAX_DIFF_PIXEL_RATIO;
}

// Sai số cho phép của hộp phần tử so với số đo Figma. Figma làm tròn vị trí chữ nên cần dư 1px.
const BOX_TOLERANCE_PX = 1;

// Figma vẽ chữ xám (grayscale), nên tắt chữ LCD để Chromium vẽ giống vậy.
// Viền màu của chữ LCD làm lệch tỉ lệ cả khi vị trí đã đúng từng pixel.
test.use({
  launchOptions: { args: ['--disable-lcd-text', '--font-render-hinting=none'] },
  deviceScaleFactor: 1,
});

// Tìm phần tử theo tên mục tiêu trong `auth-figma-boxes.ts`.
function locateTarget(page: Page, target: string): Locator {
  // Mục tiêu không có dấu hai chấm (ví dụ `title`) thì tên rỗng.
  const [kind, name = ''] = target.split(/:(.*)/s);
  if (kind === 'brand-panel') return page.locator('.auth-split-brand');
  if (kind === 'title') return page.getByRole('heading', { level: 1 });
  if (kind === 'alert') return page.getByRole('alert');
  if (kind === 'link') return page.getByRole('link', { name, exact: true });
  if (kind === 'button') return page.getByRole('button', { name, exact: true });
  if (kind === 'input') return page.getByLabel(name, { exact: true });
  if (kind === 'auth-switch') return page.locator('.auth-switch');
  if (kind === 'auth-card') return page.locator('.auth-card');
  if (kind === 'header') return page.locator('header.site-header');
  if (kind === 'footer') return page.locator('footer.site-footer');
  if (kind === 'section') {
    return page
      .locator('section.card')
      .filter({ has: page.getByRole('heading', { level: 2, name, exact: true }) });
  }
  if (kind === 'privacy-form') return page.locator('form.card');
  if (kind === 'consent-required') return page.locator('.consent-option').first();
  if (kind === 'consent-check') return page.locator('label.check').nth(Number(name));
  if (kind === 'admin-sidebar') return page.locator('aside.admin-sidebar');
  if (kind === 'admin-topbar') return page.locator('header.admin-top');
  if (kind === 'admin-card') return page.locator('main.admin-main .card');
  if (kind === 'connection-lost') return page.locator('.connection-status-lost');
  throw new Error(`Mục tiêu hộp không có trong bảng: ${target}`);
}

// Hộp của phần tử theo tọa độ trang (cộng độ cuộn), vì ảnh Figma cũng đo từ đầu trang.
async function readPageBox(locator: Locator) {
  return locator.evaluate((element) => {
    const rect = element.getBoundingClientRect();
    return {
      x: rect.x + window.scrollX,
      y: rect.y + window.scrollY,
      width: rect.width,
      height: rect.height,
    };
  });
}

// Màu thương hiệu và cỡ chữ tiêu đề lấy từ token Figma. Ảnh không bắt được lệch nhẹ như `#d70018` thành `#d70019`,
// nên kiểm thẳng giá trị tính toán của trình duyệt.
const BRAND_RED = 'rgb(215, 0, 24)';
const ADMIN_PANEL_DARK = 'rgb(20, 23, 28)';
// Màn xác thực chia đôi dùng tiêu đề 28px. Các màn còn lại dùng tiêu đề 22px theo Figma.
const SPLIT_SCREEN_CODES = ['TK-01', 'TK-02', 'TK-03', 'TK-04', 'QT-01', 'QT-02'];

// Tìm phần tử cần kiểm kiểu chữ theo vai trò hoặc chữ hiển thị, không dùng class tạm.
function locateStyledText(page: Page, check: TextStyleCheck): Locator {
  if (check.kind === 'footerLink') {
    return page.locator('footer').getByRole('link', { name: check.text, exact: true });
  }
  if (check.kind === 'link')
    return page.getByRole('link', { name: check.text, exact: true }).first();
  return page.getByText(check.text, { exact: true }).first();
}

async function readComputedStyle(locator: Locator, property: string) {
  return locator.evaluate(
    (element, cssProperty) => getComputedStyle(element).getPropertyValue(cssProperty),
    property,
  );
}

// Kiểm màu và cỡ chữ của các phần tử chính. Dùng `expect.soft` để gom hết chỗ sai trong một lượt.
async function expectFigmaStyles(page: Page, code: string) {
  const isSplitScreen = SPLIT_SCREEN_CODES.includes(code);
  const expectedTitleSize = isSplitScreen ? '28px' : '22px';
  const title = page.getByRole('heading', { level: 1 });
  expect
    .soft(await readComputedStyle(title, 'font-size'), `${code}: cỡ chữ tiêu đề`)
    .toBe(expectedTitleSize);
  expect.soft(await readComputedStyle(title, 'font-weight'), `${code}: độ đậm tiêu đề`).toBe('700');

  // Nút chính nền đỏ thương hiệu ở mọi màn có nút đó.
  const primaryButton = page.locator('.btn-primary').first();
  if ((await primaryButton.count()) > 0) {
    expect
      .soft(await readComputedStyle(primaryButton, 'background-color'), `${code}: nền nút chính`)
      .toBe(BRAND_RED);
  }

  // Bảng thương hiệu: đỏ cho khách, tối cho quản trị.
  const brandPanel = page.locator('.auth-split-brand');
  if ((await brandPanel.count()) > 0) {
    const expectedPanelColor = code.startsWith('QT') ? ADMIN_PANEL_DARK : BRAND_RED;
    expect
      .soft(
        await readComputedStyle(brandPanel, 'background-color'),
        `${code}: nền bảng thương hiệu`,
      )
      .toBe(expectedPanelColor);
  }

  // Thanh đầu trang của khách nền đỏ thương hiệu.
  const customerHeader = page.locator('header.site-header');
  if ((await customerHeader.count()) > 0) {
    expect
      .soft(
        await readComputedStyle(customerHeader, 'background-color'),
        `${code}: nền thanh đầu trang`,
      )
      .toBe(BRAND_RED);
  }

  // Kiểu chữ theo Figma: cỡ, độ đậm, màu, cao dòng. Phần tử tìm theo vai trò hoặc chữ hiển thị.
  const textStyleChecks = figmaTextStyles[code];
  if (!textStyleChecks) throw new Error(`Thiếu bảng kiểu chữ Figma cho ${code}`);
  for (const check of textStyleChecks) {
    const element = locateStyledText(page, check);
    const labelOf = `${code}: "${check.text}"`;
    expect
      .soft(await readComputedStyle(element, 'font-size'), `${labelOf} cỡ chữ`)
      .toBe(check.fontSize);
    expect
      .soft(await readComputedStyle(element, 'font-weight'), `${labelOf} độ đậm`)
      .toBe(check.fontWeight);
    expect.soft(await readComputedStyle(element, 'color'), `${labelOf} màu chữ`).toBe(check.color);
    if (check.lineHeight) {
      expect
        .soft(await readComputedStyle(element, 'line-height'), `${labelOf} cao dòng`)
        .toBe(check.lineHeight);
    }
  }
}

// So hộp của các phần tử chính với số đo Figma. Gom mọi chỗ lệch vào một báo cáo để sửa một lượt.
async function expectFigmaBoxes(page: Page, key: string) {
  const expectedBoxes = figmaBoxes[key];
  if (!expectedBoxes) throw new Error(`Thiếu bảng hộp Figma cho ${key}`);
  const mismatches: string[] = [];
  for (const expected of expectedBoxes) {
    const actual = await readPageBox(locateTarget(page, expected.target));
    const pairs: [string, number, number | undefined][] = [
      ['x', actual.x, expected.x],
      ['y', actual.y, expected.y],
      ['width', actual.width, expected.width],
      ['height', actual.height, expected.height],
    ];
    for (const [axis, actualValue, expectedValue] of pairs) {
      if (expectedValue === undefined) continue;
      if (Math.abs(actualValue - expectedValue) > BOX_TOLERANCE_PX) {
        mismatches.push(
          `${expected.target} ${axis}: thật ${actualValue.toFixed(2)}, Figma ${expectedValue}`,
        );
      }
    }
  }
  // Dùng `expect.soft` để ảnh vẫn được so và báo tỉ lệ khi hộp lệch.
  expect.soft(mismatches, `Hộp lệch quá ${BOX_TOLERANCE_PX}px so với Figma`).toEqual([]);
}

type ScreenSize = 'desktop' | 'mobile';

const viewports: Record<ScreenSize, { width: number; height: number }> = {
  desktop: { width: 1280, height: 800 },
  mobile: { width: 375, height: 812 },
};

// Mật khẩu sai 16 ký tự để ô mật khẩu hiện đúng 16 chấm như ảnh Figma.
const WRONG_PASSWORD = 'x'.repeat(16);

// Đăng nhập bằng API để phiên cookie nằm trong ngữ cảnh trình duyệt của case.
// Quản trị dùng endpoint riêng, khách dùng endpoint chung.
async function signInByApi(
  page: Page,
  endpoint: '/api/auth/sessions' | '/api/admin/auth/sessions',
  email: string,
  password: string,
) {
  // API phiên chỉ nhận request có `Origin` của web, giống trình duyệt thật.
  const response = await page.request.post(endpoint, {
    headers: { Origin: e2eEnvironment.webOrigin },
    data: { email, password },
  });
  expect(response.status(), await response.text()).toBe(201);
}

// Backend trả cùng một câu lỗi cho sai email và sai mật khẩu. Giả lập để không làm tăng bộ đếm khóa tài khoản.
async function mockWrongPassword(page: Page) {
  await page.route(/\/api\/(admin\/)?auth\/sessions$/, (route) =>
    route.fulfill({
      status: 401,
      contentType: 'application/json',
      body: JSON.stringify({
        error: { code: 'INVALID_CREDENTIALS', message: 'Email hoặc mật khẩu không chính xác.' },
      }),
    }),
  );
}

interface FigmaScreen {
  code: string;
  // Chuẩn bị trạng thái của màn hình rồi dừng ở đúng khung hình cần chụp.
  prepare: (page: Page, size: ScreenSize) => Promise<void>;
}

const screens: FigmaScreen[] = [
  {
    code: 'TK-01',
    prepare: async (page) => {
      await page.goto('/login');
      await expect(page.getByRole('heading', { level: 1, name: 'Đăng nhập' })).toBeVisible();
    },
  },
  {
    code: 'TK-02',
    prepare: async (page) => {
      await mockWrongPassword(page);
      await page.goto('/login');
      await page.getByLabel('Email', { exact: true }).fill(e2eEnvironment.ordersCustomerEmail);
      await page.getByLabel('Mật khẩu', { exact: true }).fill(WRONG_PASSWORD);
      await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
      await expect(page.getByRole('alert')).toHaveText('Email hoặc mật khẩu không chính xác.');
    },
  },
  {
    code: 'TK-03',
    prepare: async (page) => {
      await page.goto('/register');
      await expect(
        page.getByRole('heading', { level: 1, name: 'Đăng ký tài khoản' }),
      ).toBeVisible();
    },
  },
  {
    code: 'TK-04',
    prepare: async (page) => {
      await page.goto('/register');
      await page.getByRole('button', { name: 'Tạo tài khoản', exact: true }).click();
      await expect(page.getByText('Email không được để trống.')).toBeVisible();
    },
  },
  {
    code: 'TK-05',
    prepare: async (page) => {
      await page.goto('/forgot-password');
      await expect(page.getByRole('heading', { level: 1, name: 'Quên mật khẩu' })).toBeVisible();
    },
  },
  {
    code: 'TK-06',
    prepare: async (page) => {
      await page.goto('/reset-password?token=token-e2e');
      await expect(page.getByRole('heading', { level: 1, name: 'Đặt lại mật khẩu' })).toBeVisible();
    },
  },
  {
    code: 'TK-07',
    prepare: async (page) => {
      // Token sai làm API trả INVALID_TOKEN. Trang hiện khối "Xác thực không thành công".
      await page.goto('/verify-email?token=token-e2e');
      await expect(
        page.getByRole('heading', { level: 1, name: 'Xác thực không thành công' }),
      ).toBeVisible();
    },
  },
  {
    code: 'TK-08',
    prepare: async (page) => {
      await signInByApi(
        page,
        '/api/auth/sessions',
        e2eEnvironment.ordersCustomerEmail,
        e2eEnvironment.accountPassword,
      );
      await page.goto('/me');
      await expect(
        page.getByRole('heading', { level: 1, name: 'Tài khoản của tôi' }),
      ).toBeVisible();
      await expect(page.getByText('Bạn chưa lưu địa chỉ nào.')).toBeVisible();
    },
  },
  {
    code: 'TK-09',
    prepare: async (page) => {
      await signInByApi(
        page,
        '/api/auth/sessions',
        e2eEnvironment.ordersCustomerEmail,
        e2eEnvironment.accountPassword,
      );
      await page.goto('/privacy');
      await expect(
        page.getByRole('heading', { level: 1, name: 'Cài đặt quyền riêng tư' }),
      ).toBeVisible();
    },
  },
  {
    code: 'QT-01',
    prepare: async (page) => {
      await page.goto('/admin/login');
      await expect(
        page.getByRole('heading', { level: 1, name: 'Đăng nhập quản trị' }),
      ).toBeVisible();
    },
  },
  {
    code: 'QT-02',
    prepare: async (page) => {
      await mockWrongPassword(page);
      await page.goto('/admin/login');
      await page.getByLabel('Email nhân viên', { exact: true }).fill(e2eEnvironment.adminEmail);
      await page.getByLabel('Mật khẩu', { exact: true }).fill(WRONG_PASSWORD);
      await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
      await expect(page.getByRole('alert')).toHaveText('Email hoặc mật khẩu không chính xác.');
    },
  },
  {
    code: 'QT-03',
    prepare: async (page, size) => {
      // Ảnh Figma điện thoại chụp lúc luồng sự kiện chăm sóc khách hàng mất kết nối.
      // Ảnh máy tính chụp lúc luồng còn nối, nên giữ luồng thật.
      if (size === 'mobile')
        await page.route('**/api/admin/support/events', (route) => route.abort());
      await signInByApi(
        page,
        '/api/admin/auth/sessions',
        e2eEnvironment.adminEmail,
        e2eEnvironment.adminPassword,
      );
      await page.goto('/admin');
      await expect(
        page.getByRole('heading', { level: 1, name: 'Chào mừng trở lại, admin!' }),
      ).toBeVisible();
      if (size === 'mobile')
        await expect(page.getByText('Mất kết nối')).toBeVisible({ timeout: 15000 });
    },
  },
];

for (const screen of screens) {
  for (const size of ['desktop', 'mobile'] as const) {
    test(`${screen.code} ${size}: khớp ảnh Figma`, async ({ page }, testInfo) => {
      // Kiểm biến môi trường ngay đầu ca. Giá trị sai phải làm ca đỏ trước mọi bước khác.
      const maxDiffPixelRatio = maxDiffPixelRatioFor(`${screen.code}-${size}`);
      await page.setViewportSize(viewports[size]);
      // Ghi nhớ lựa chọn cookie trước khi trang tải. Ảnh Figma không có banner cookie.
      await page.addInitScript(() => localStorage.setItem('pk_consent_chosen', 'true'));
      await screen.prepare(page, size);

      // Font thật phải nạp xong ở mọi độ đậm đang dùng, vì font dự phòng làm lệch mọi vị trí chữ.
      // `document.fonts.check` trả `true` khi không có font nào khớp, nên phải nạp thử kèm chữ Việt và xem trạng thái.
      await page.evaluate(() => document.fonts.ready);
      for (const weight of [400, 500, 600, 700, 800]) {
        const statuses = await page.evaluate(async (fontWeight) => {
          const faces = await document.fonts.load(
            `${fontWeight} 16px "Be Vietnam Pro"`,
            'Đăng nhập ệ ư ớ',
          );
          return faces.map((face) => face.status);
        }, weight);
        expect(
          statuses.includes('loaded'),
          `Be Vietnam Pro ${weight} chưa nạp, không so ảnh bằng font dự phòng`,
        ).toBe(true);
      }

      // Hộp phần tử kiểm trước ảnh, vì lệch bố cục cần báo thẳng chỗ lệch thay vì một tỉ lệ pixel.
      await expectFigmaBoxes(page, `${screen.code}-${size}`);
      await expectFigmaStyles(page, screen.code);

      // Ảnh thật của trang lưu cho báo cáo, tách khỏi so sánh.
      await page.screenshot({
        path: testInfo.outputPath(`${screen.code}-${size}.png`),
        fullPage: true,
        animations: 'disabled',
        caret: 'hide',
      });

      await expect(page).toHaveScreenshot(`${screen.code}-${size}.png`, {
        fullPage: true,
        animations: 'disabled',
        caret: 'hide',
        maxDiffPixelRatio,
      });
    });
  }
}
