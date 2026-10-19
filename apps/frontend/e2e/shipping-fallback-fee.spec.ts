import { expect, test, type Page } from '@playwright/test';
import { e2eEnvironment } from './environment';

const FALLBACK_FEE_PATH = '/admin/shipping/fallback-fee';
const FALLBACK_FEE_API = '**/api/admin/shipping/fallback-fee';

// Chín giá nhập trong spec. Gói mẫu có khối lượng tính cước 1.500 g, nên phí ví dụ tính ra từ các giá này.
const PRICES = {
  sameProvince: { baseFee: '20000', baseWeightGrams: '1000', stepFee: '5000' },
  sameRegion: { baseFee: '30000', baseWeightGrams: '1000', stepFee: '6000' },
  crossRegion: { baseFee: '40000', baseWeightGrams: '2000', stepFee: '7000' },
} as const;

type TierName = keyof typeof PRICES;

// Id ô theo quy ước của trang: `fallback-fee-<tuyến>-<giá trị>`, viết gạch nối.
const FIELD_ID = {
  sameProvince: {
    baseFee: 'fallback-fee-same-province-base-fee',
    baseWeightGrams: 'fallback-fee-same-province-base-weight-grams',
    stepFee: 'fallback-fee-same-province-step-fee',
  },
  sameRegion: {
    baseFee: 'fallback-fee-same-region-base-fee',
    baseWeightGrams: 'fallback-fee-same-region-base-weight-grams',
    stepFee: 'fallback-fee-same-region-step-fee',
  },
  crossRegion: {
    baseFee: 'fallback-fee-cross-region-base-fee',
    baseWeightGrams: 'fallback-fee-cross-region-base-weight-grams',
    stepFee: 'fallback-fee-cross-region-step-fee',
  },
} as const;

async function loginAdmin(page: Page) {
  await page.goto('/admin/login');
  const consent = page.getByRole('button', { name: 'Chỉ cần thiết', exact: true });
  if (await consent.isVisible()) await consent.click();
  await page.getByLabel(/^Email/).fill(e2eEnvironment.adminEmail);
  await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.adminPassword);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}/admin`);
}

// Trang không được rộng hơn khung nhìn. Ảnh rộng hơn viewport nghĩa là người dùng phải cuộn ngang.
async function expectNoHorizontalOverflow(page: Page) {
  const widths = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth,
  }));
  expect(widths.content).toBeLessThanOrEqual(widths.viewport);
}

// Chờ màn tải xong trước khi đo và chụp, để ảnh phản ánh nội dung thật.
async function openFallbackFeePage(page: Page) {
  await page.goto(FALLBACK_FEE_PATH);
  await expect(
    page.getByRole('heading', { level: 1, name: 'Phí ship dự phòng', exact: true }),
  ).toBeVisible();
  await expect(page.locator(`#${FIELD_ID.sameProvince.baseFee}`)).toBeVisible();
  await expect(page.getByRole('status')).toHaveCount(0);
}

// Ô trống hoặc ô tiền hiện dấu chấm nghìn, nên gõ từng phím để ô tiền định dạng như người dùng thật.
async function fillTier(page: Page, tier: TierName, skip?: keyof (typeof PRICES)[TierName]) {
  for (const kind of ['baseFee', 'baseWeightGrams', 'stepFee'] as const) {
    if (kind === skip) continue;
    await page.locator(`#${FIELD_ID[tier][kind]}`).fill(PRICES[tier][kind]);
  }
}

async function fillAllTiers(page: Page) {
  for (const tier of Object.keys(PRICES) as TierName[]) await fillTier(page, tier);
}

// Đưa bảng về trạng thái tắt và trống bằng API, để spec khác không bị ảnh hưởng dù spec này dừng giữa chừng.
async function resetFallbackFee(page: Page) {
  const response = await page.request.put('/api/admin/shipping/fallback-fee', {
    headers: { Origin: e2eEnvironment.webOrigin },
    data: {
      enabled: false,
      sameProvinceBaseFee: null,
      sameProvinceBaseWeightGrams: null,
      sameProvinceStepFee: null,
      sameRegionBaseFee: null,
      sameRegionBaseWeightGrams: null,
      sameRegionStepFee: null,
      crossRegionBaseFee: null,
      crossRegionBaseWeightGrams: null,
      crossRegionStepFee: null,
    },
  });
  expect(response.status(), await response.text()).toBe(200);
}

test('UI-S3: admin bật phí dự phòng, nhập giá, lưu, tải lại thấy đúng giá, lỗi hiện đúng ô', async ({
  page,
}, testInfo) => {
  test.setTimeout(180000);

  // Ghi lỗi console và lỗi trang. Mọi bước trong spec phải có 0 lỗi và 0 cảnh báo ngoài dòng của request cố ý làm lỗi.
  const consoleIssues: string[] = [];
  page.on('console', (message) => {
    // Chromium có giao diện tự gọi `favicon.ico`, còn ứng dụng chưa có favicon.
    if (message.location().url.endsWith('/favicon.ico')) return;
    if (message.type() === 'error' || message.type() === 'warning') {
      consoleIssues.push(`[${message.type()}] ${message.text()}`);
    }
  });
  page.on('pageerror', (error) => consoleIssues.push(`[pageerror] ${error.message}`));

  // Đếm PUT tới API phí dự phòng để khẳng định lỗi nhập liệu hoặc lưu thành công gửi đúng số lần.
  let putCount = 0;
  page.on('request', (request) => {
    if (
      request.method() === 'PUT' &&
      new URL(request.url()).pathname === '/api/admin/shipping/fallback-fee'
    ) {
      putCount += 1;
    }
  });

  try {
    await test.step('Admin đăng nhập và mở màn qua menu Phí ship dự phòng', async () => {
      await page.setViewportSize({ width: 1280, height: 800 });
      await loginAdmin(page);
      await resetFallbackFee(page);
      const navigation = page.getByRole('navigation', { name: 'Quản trị', exact: true });
      await navigation.getByRole('link', { name: 'Phí ship dự phòng', exact: true }).click();
      await expect(page).toHaveURL(`${e2eEnvironment.webOrigin}${FALLBACK_FEE_PATH}`);
      await expect(
        page.getByRole('heading', { level: 1, name: 'Phí ship dự phòng', exact: true }),
      ).toBeVisible();
      await expect(page.getByText(/GHN không phản hồi lúc báo phí/)).toBeVisible();
    });

    await test.step('Trang mới: công tắc tắt, ba nhóm đúng thứ tự, ô trống, ví dụ chưa đủ giá', async () => {
      for (const width of [1280, 375]) {
        await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
        await openFallbackFeePage(page);
        await expect(
          page.getByRole('checkbox', { name: 'Dùng phí dự phòng khi GHN không phản hồi' }),
        ).not.toBeChecked();
        // Mỗi tuyến là một `fieldset` có `legend`, nên tên truy cập của nhóm là tên tuyến.
        const groups = page.locator('fieldset');
        await expect(groups).toHaveCount(3);
        await expect(groups.nth(0)).toHaveAccessibleName('Nội tỉnh');
        await expect(groups.nth(1)).toHaveAccessibleName('Nội vùng');
        await expect(groups.nth(2)).toHaveAccessibleName('Liên vùng');
        for (const tier of Object.keys(PRICES) as TierName[]) {
          await expect(page.locator(`#${FIELD_ID[tier].baseFee}`)).toHaveValue('');
          await expect(page.locator(`#${FIELD_ID[tier].baseWeightGrams}`)).toHaveValue('');
        }
        await expect(page.locator('#fallback-fee-example')).toContainText('1.500 g');
        await expect(page.locator('#fallback-fee-example-same-province')).toHaveText(
          'Chưa đủ giá để tính',
        );
        await expectNoHorizontalOverflow(page);
        await page.screenshot({
          path: testInfo.outputPath(`fallback-fee-new-${width}.png`),
          fullPage: true,
        });
      }
    });

    await test.step('Bật mà để trống một ô: lỗi hiện dưới đúng ô đó', async () => {
      for (const width of [1280, 375]) {
        await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
        await openFallbackFeePage(page);
        await page
          .getByRole('checkbox', { name: 'Dùng phí dự phòng khi GHN không phản hồi' })
          .check();
        await fillTier(page, 'sameProvince');
        await fillTier(page, 'sameRegion', 'stepFee');
        await fillTier(page, 'crossRegion');

        await page.getByRole('button', { name: 'Lưu', exact: true }).click();

        await expect(page.locator(`#${FIELD_ID.sameRegion.stepFee}-error`)).toHaveText(
          'Cần nhập giá trị này khi bật phí dự phòng.',
        );
        // Chỉ ô trống có lỗi. Tám ô đã nhập không bị đánh dấu sai.
        await expect(page.locator('.form-error')).toHaveCount(1);
        await expect(page.getByText('Đã lưu phí ship dự phòng.', { exact: true })).toHaveCount(0);
        await expectNoHorizontalOverflow(page);
        // Cuộn lên đầu để thanh menu cố định trên màn hẹp không đè lên tiêu đề trong ảnh chụp cả trang.
        await page.evaluate(() => window.scrollTo(0, 0));
        await page.screenshot({
          path: testInfo.outputPath(`fallback-fee-missing-${width}.png`),
          fullPage: true,
        });
      }
    });

    await test.step('Điền đủ chín giá, ví dụ đổi theo, lưu thành công', async () => {
      await page.setViewportSize({ width: 1280, height: 800 });
      await openFallbackFeePage(page);
      await page
        .getByRole('checkbox', { name: 'Dùng phí dự phòng khi GHN không phản hồi' })
        .check();
      await fillAllTiers(page);

      // Gói mẫu 1.500 g: nội tỉnh vượt 500 g là 1 bước, liên vùng có khối lượng gốc 2.000 g nên chỉ giá gốc.
      await expect(page.locator('#fallback-fee-example-same-province')).toHaveText('25.000 đ');
      await expect(page.locator('#fallback-fee-example-same-region')).toHaveText('36.000 đ');
      await expect(page.locator('#fallback-fee-example-cross-region')).toHaveText('40.000 đ');

      const putsBefore = putCount;
      await page.getByRole('button', { name: 'Lưu', exact: true }).click();

      await expect(page.getByText('Đã lưu phí ship dự phòng.', { exact: true })).toBeVisible();
      expect(putCount).toBe(putsBefore + 1);
      await expect(page.locator('.form-error')).toHaveCount(0);
      await expectNoHorizontalOverflow(page);
      await page.screenshot({
        path: testInfo.outputPath('fallback-fee-saved-1280.png'),
        fullPage: true,
      });
    });

    await test.step('Tải lại trang: đúng chín giá và công tắc đang bật', async () => {
      for (const width of [1280, 375]) {
        await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
        await openFallbackFeePage(page);
        await expect(
          page.getByRole('checkbox', { name: 'Dùng phí dự phòng khi GHN không phản hồi' }),
        ).toBeChecked();
        // Ô tiền hiện dấu chấm nghìn. Ô khối lượng hiện số nguyên gram.
        await expect(page.locator(`#${FIELD_ID.sameProvince.baseFee}`)).toHaveValue('20.000');
        await expect(page.locator(`#${FIELD_ID.sameProvince.baseWeightGrams}`)).toHaveValue('1000');
        await expect(page.locator(`#${FIELD_ID.sameProvince.stepFee}`)).toHaveValue('5.000');
        await expect(page.locator(`#${FIELD_ID.sameRegion.baseFee}`)).toHaveValue('30.000');
        await expect(page.locator(`#${FIELD_ID.sameRegion.baseWeightGrams}`)).toHaveValue('1000');
        await expect(page.locator(`#${FIELD_ID.sameRegion.stepFee}`)).toHaveValue('6.000');
        await expect(page.locator(`#${FIELD_ID.crossRegion.baseFee}`)).toHaveValue('40.000');
        await expect(page.locator(`#${FIELD_ID.crossRegion.baseWeightGrams}`)).toHaveValue('2000');
        await expect(page.locator(`#${FIELD_ID.crossRegion.stepFee}`)).toHaveValue('7.000');
        await expect(page.locator('#fallback-fee-example-same-province')).toHaveText('25.000 đ');
        await expectNoHorizontalOverflow(page);
        await page.screenshot({
          path: testInfo.outputPath(`fallback-fee-reloaded-${width}.png`),
          fullPage: true,
        });
      }
    });

    await test.step('Lỗi 422 và 400 từ backend thật hiện dưới đúng ô', async () => {
      // Sửa body của PUT trước khi tới backend, nên lỗi do backend thật trả chứ không do giả lập.
      let bodyChange: Record<string, unknown> = {};
      await page.route(FALLBACK_FEE_API, async (route) => {
        const request = route.request();
        if (request.method() !== 'PUT') {
          await route.continue();
          return;
        }
        await route.continue({
          postData: JSON.stringify({ ...request.postDataJSON(), ...bodyChange }),
        });
      });

      for (const width of [1280, 375]) {
        await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
        await openFallbackFeePage(page);

        // Bật mà thiếu giá: backend trả 422 và chỉ ô thiếu có lỗi.
        bodyChange = { crossRegionBaseFee: null };
        await page.getByRole('button', { name: 'Lưu', exact: true }).click();
        await expect(page.locator(`#${FIELD_ID.crossRegion.baseFee}-error`)).toHaveText(
          'Cần nhập giá trị này khi bật phí dự phòng.',
        );
        await expect(page.locator('.form-error')).toHaveCount(1);
        await expect(page.locator('[role="alert"]')).toHaveCount(0);
        await expectNoHorizontalOverflow(page);
        await page.evaluate(() => window.scrollTo(0, 0));
        await page.screenshot({
          path: testInfo.outputPath(`fallback-fee-server-422-${width}.png`),
          fullPage: true,
        });

        // Khối lượng gốc 0 nằm ngoài khoảng: backend trả 400 và lỗi hiện dưới ô khối lượng.
        bodyChange = { sameRegionBaseWeightGrams: 0 };
        await page.getByRole('button', { name: 'Lưu', exact: true }).click();
        await expect(page.locator(`#${FIELD_ID.sameRegion.baseWeightGrams}-error`)).toHaveText(
          'Khối lượng gốc phải là số nguyên từ 1 đến 20.000 gram.',
        );
        await expect(page.locator(`#${FIELD_ID.crossRegion.baseFee}-error`)).toHaveCount(0);
        await expect(page.locator('[role="alert"]')).toHaveCount(0);
        await page.evaluate(() => window.scrollTo(0, 0));
        await page.screenshot({
          path: testInfo.outputPath(`fallback-fee-server-400-${width}.png`),
          fullPage: true,
        });
      }
      await page.unroute(FALLBACK_FEE_API);
    });

    await test.step('Tắt công tắc: ô giá vẫn sửa được, lưu giữ giá và tải lại thấy công tắc tắt', async () => {
      await page.setViewportSize({ width: 1280, height: 800 });
      await openFallbackFeePage(page);
      const toggle = page.getByRole('checkbox', {
        name: 'Dùng phí dự phòng khi GHN không phản hồi',
      });
      await toggle.uncheck();
      await expect(page.locator(`#${FIELD_ID.crossRegion.stepFee}`)).toBeEnabled();
      await page.getByRole('button', { name: 'Lưu', exact: true }).click();
      await expect(page.getByText('Đã lưu phí ship dự phòng.', { exact: true })).toBeVisible();

      await openFallbackFeePage(page);
      await expect(toggle).not.toBeChecked();
      await expect(page.locator(`#${FIELD_ID.crossRegion.stepFee}`)).toHaveValue('7.000');
    });
  } finally {
    // Dù spec lỗi ở bước nào, đưa bảng về tắt và trống để spec báo phí khác không dùng nhầm phí dự phòng.
    await resetFallbackFee(page);
  }

  // Chromium ghi dòng lỗi cho phản hồi 400 và 422 của request cố ý làm lỗi. Chỉ bỏ hai dòng này, giữ mọi lỗi khác.
  const unexpectedIssues = consoleIssues.filter(
    (issue) => !issue.includes('status of 400') && !issue.includes('status of 422'),
  );
  expect(unexpectedIssues).toEqual([]);
});
