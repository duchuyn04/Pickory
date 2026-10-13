import { expect, test, type Page } from '@playwright/test';
import { expectNoHorizontalOverflow, prepareShipping, seedProduct } from './admin-order-fixtures';
import { collectConsoleIssues, loginAdmin, tinyPng } from './catalog-admin-flow';
import { e2eEnvironment } from './environment';

const ORIGIN_HEADERS = { Origin: e2eEnvironment.webOrigin };

async function loginCustomer(page: Page) {
  await page.goto('/login');
  const consent = page.getByRole('button', { name: 'Chỉ cần thiết', exact: true });
  if (await consent.isVisible()) await consent.click();
  await page.getByLabel(/^Email/).fill(e2eEnvironment.screensCustomerEmail);
  await page.getByLabel('Mật khẩu', { exact: true }).fill(e2eEnvironment.accountPassword!);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Tài khoản', exact: true })).toBeVisible();
}

// Chọn một lựa chọn của ô tìm kiếm tỉnh hoặc phường bằng cách gõ tên rồi bấm lựa chọn.
async function chooseOption(page: Page, inputId: string, name: string) {
  const input = page.locator(`#${inputId}`);
  await expect(input).toBeEnabled();
  await input.fill(name);
  await page.getByRole('option', { name, exact: true }).click();
  await expect(input).toHaveValue(name);
}

interface AddressInput {
  label: string;
  name: string;
  line: string;
}

async function addAddress(
  page: Page,
  input: AddressInput,
  place: { provinceName: string; wardName: string },
) {
  await page.getByRole('button', { name: 'Thêm địa chỉ', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await dialog.getByLabel('Nhãn địa chỉ').fill(input.label);
  await dialog.getByLabel('Họ và tên người nhận').fill(input.name);
  await dialog.getByLabel('Số điện thoại').fill('0987001122');
  await chooseOption(page, 'book-province', place.provinceName);
  await chooseOption(page, 'book-ward', place.wardName);
  await dialog.locator('#book-line').fill(input.line);
  await dialog.getByRole('button', { name: 'Lưu địa chỉ', exact: true }).click();
  await expect(dialog).toBeHidden();
}

// Xóa mọi địa chỉ của khách E2E qua API. Spec khác dùng cùng khách và cần sổ rỗng như ban đầu.
async function clearAddressBook(page: Page) {
  const list = await page.request.get('/api/me/addresses');
  if (list.status() !== 200) return;
  const items = (await list.json()).data as { id: string }[];
  for (const item of items) {
    await page.request.delete(`/api/me/addresses/${item.id}`, { headers: ORIGIN_HEADERS });
  }
}

test('COM-UI8: khách quản lý sổ địa chỉ và thấy địa chỉ mặc định điền sẵn ở checkout', async ({
  browser,
  page,
}, testInfo) => {
  test.setTimeout(240000);
  const adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
  const adminPage = await adminContext.newPage();
  await loginAdmin(adminPage);
  const product = await seedProduct(adminPage);
  const place = await prepareShipping(adminPage);
  await adminContext.close();

  const consoleIssues = collectConsoleIssues(page);
  await page.route('https://res.cloudinary.com/**', (route) =>
    route.fulfill({ body: tinyPng, contentType: 'image/png' }),
  );

  try {
    await page.setViewportSize({ width: 1280, height: 800 });
    await loginCustomer(page);
    // Bắt đầu từ sổ rỗng để các bước đếm địa chỉ chính xác.
    await clearAddressBook(page);

    await test.step('Thêm hai địa chỉ, đặt địa chỉ thứ hai làm mặc định', async () => {
      await page.goto('/me');
      const book = page.getByRole('region', { name: 'Sổ địa chỉ' });
      await expect(book.getByText('Bạn chưa lưu địa chỉ nào.')).toBeVisible();

      await addAddress(page, { label: 'Nhà', name: 'Khách Nhà', line: '1 Phố Nhà' }, place);
      await expect(book.getByRole('listitem')).toHaveCount(1);
      // Địa chỉ đầu tiên tự là mặc định.
      await expect(book.getByRole('listitem').first()).toContainText('Mặc định');

      await addAddress(
        page,
        { label: 'Công ty', name: 'Khách Công Ty', line: '2 Phố Công Ty' },
        place,
      );
      await expect(book.getByRole('listitem')).toHaveCount(2);

      const company = book.getByRole('listitem').filter({ hasText: 'Công ty' });
      await company.getByRole('button', { name: 'Đặt làm mặc định', exact: true }).click();
      await expect(company).toContainText('Mặc định');
      await expect(
        book
          .getByRole('listitem')
          .filter({ hasText: 'Nhà' })
          .getByText('Mặc định', { exact: true }),
      ).toHaveCount(0);
    });

    for (const width of [1280, 375]) {
      await test.step(`Ảnh sổ địa chỉ và hộp thoại thêm ở ${width}px không tràn ngang`, async () => {
        await page.setViewportSize({ width, height: width === 375 ? 812 : 800 });
        await page.goto('/me');
        const book = page.getByRole('region', { name: 'Sổ địa chỉ' });
        await expect(book.getByRole('listitem')).toHaveCount(2);
        await expectNoHorizontalOverflow(page);
        await book.scrollIntoViewIfNeeded();
        await book.screenshot({ path: testInfo.outputPath(`address-book-${width}.png`) });

        await book.getByRole('button', { name: 'Thêm địa chỉ', exact: true }).click();
        await expect(page.getByRole('dialog')).toBeVisible();
        await expectNoHorizontalOverflow(page);
        await page.screenshot({ path: testInfo.outputPath(`address-dialog-${width}.png`) });
        await page.keyboard.press('Escape');
        await expect(page.getByRole('dialog')).toBeHidden();
      });
    }

    await test.step('Sửa nhãn một địa chỉ rồi xóa địa chỉ đó', async () => {
      await page.setViewportSize({ width: 1280, height: 800 });
      await page.goto('/me');
      const book = page.getByRole('region', { name: 'Sổ địa chỉ' });
      const home = book.getByRole('listitem').filter({ hasText: 'Nhà' });
      await home.getByRole('button', { name: 'Sửa', exact: true }).click();
      const dialog = page.getByRole('dialog');
      await expect(dialog.getByLabel('Nhãn địa chỉ')).toHaveValue('Nhà');
      await dialog.getByLabel('Nhãn địa chỉ').fill('Nhà riêng');
      await dialog.getByRole('button', { name: 'Lưu địa chỉ', exact: true }).click();
      await expect(dialog).toBeHidden();
      await expect(book.getByRole('listitem').filter({ hasText: 'Nhà riêng' })).toHaveCount(1);

      const renamed = book.getByRole('listitem').filter({ hasText: 'Nhà riêng' });
      await renamed.getByRole('button', { name: 'Xóa', exact: true }).click();
      const confirm = page.getByRole('dialog');
      await expect(confirm).toContainText('Nhà riêng');
      await confirm.getByRole('button', { name: 'Xóa địa chỉ', exact: true }).click();
      await expect(confirm).toBeHidden();
      await expect(book.getByRole('listitem')).toHaveCount(1);
      await expect(book.getByRole('listitem').first()).toContainText('Công ty');
    });

    await test.step('Checkout điền sẵn địa chỉ mặc định và hiện phí vận chuyển', async () => {
      await page.setViewportSize({ width: 375, height: 812 });
      await page.goto(`/p/${product.slug}`);
      await page.getByRole('button', { name: 'Thêm vào giỏ' }).click();
      await expect(page.getByRole('status').filter({ hasText: 'Đã thêm vào giỏ' })).toBeVisible();
      await page.goto('/checkout');

      await expect(page.getByLabel('Họ và tên', { exact: true })).toHaveValue('Khách Công Ty');
      await expect(page.locator('#checkout-address-line')).toHaveValue('2 Phố Công Ty');
      await expect(page.getByLabel('Địa chỉ đã lưu')).toContainText('Công ty');
      // Phí vận chuyển hiện sau khi preview chạy với địa chỉ điền sẵn.
      // Nút đặt hàng chỉ bật khi preview đã trả phí cho địa chỉ điền sẵn, nên chờ nút bật là chờ phí hiện.
      await expect(page.getByRole('button', { name: /Đặt hàng$/ })).toBeEnabled();
      await expect(page.getByText('Chọn địa chỉ để tính phí vận chuyển')).toHaveCount(0);
      await expectNoHorizontalOverflow(page);
      await page.screenshot({
        path: testInfo.outputPath('checkout-prefilled-375.png'),
        fullPage: true,
      });
    });
  } finally {
    await clearAddressBook(page);
  }

  expect(consoleIssues).toEqual([]);
});
