import { expect, test } from '@playwright/test';
import {
  expectNoHorizontalOverflow,
  placeCodOrder,
  prepareShipping,
  seedProduct,
} from './admin-order-fixtures';
import { collectConsoleIssues, loginAdmin } from './catalog-admin-flow';
import { e2eEnvironment } from './environment';

test('COM-UI5: nhân viên tìm đơn theo mã, mở chi tiết bằng bàn phím, không tràn trang', async ({
  browser,
  page,
}, testInfo) => {
  test.setTimeout(240000);
  const adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
  const adminPage = await adminContext.newPage();
  const consoleIssues = collectConsoleIssues(adminPage);

  await loginAdmin(adminPage);
  const product = await seedProduct(adminPage);
  const address = await prepareShipping(adminPage);

  let orderCode = '';
  await test.step('Khách đặt một đơn COD', async () => {
    orderCode = await placeCodOrder(page, product, address);
  });

  await test.step('Tìm theo mã đơn, URL giữ từ khóa, mở chi tiết bằng phím Enter', async () => {
    await adminPage.setViewportSize({ width: 1280, height: 800 });
    await adminPage.goto('/admin/orders');
    await expect(
      adminPage.getByRole('heading', { level: 1, name: 'Quản lý đơn hàng' }),
    ).toBeVisible();

    const searchBox = adminPage.getByLabel('Tìm theo mã đơn, tên, số điện thoại hoặc email', {
      exact: true,
    });
    await searchBox.fill(orderCode);
    // Ô tìm kiếm chờ 300 ms sau lần gõ cuối rồi mới ghi từ khóa lên URL.
    await expect(adminPage).toHaveURL(/[?&]search=/);
    const rows = adminPage.locator('table.order-table tbody tr');
    await expect(rows).toHaveCount(1);
    await expect(rows.first()).toContainText('Trần Thị Bình');
    await expect(rows.first()).toContainText('Chờ xác nhận');

    // Tải lại giữ từ khóa và danh sách.
    await adminPage.reload();
    await expect(searchBox).toHaveValue(orderCode);
    await expect(rows).toHaveCount(1);

    // Mã đơn là liên kết: đưa focus vào rồi Enter để chứng minh dùng được bằng bàn phím.
    const link = rows.first().getByRole('link', { name: orderCode, exact: true });
    await link.focus();
    await adminPage.keyboard.press('Enter');
    await expect(adminPage).toHaveURL(`${e2eEnvironment.webOrigin}/admin/orders/${orderCode}`);
    await expect(
      adminPage.getByRole('heading', { level: 1, name: `Đơn hàng ${orderCode}` }),
    ).toBeVisible();
    await expect(adminPage.getByText('Trần Thị Bình')).toBeVisible();
    await expect(adminPage.getByText('15 Đường Đơn Admin')).toBeVisible();
    await expect(adminPage.getByText(product.name)).toBeVisible();
  });

  await test.step('Tab trạng thái lọc danh sách và mũi tên chuyển focus', async () => {
    await adminPage.goto('/admin/orders');
    const pendingTab = adminPage.getByRole('tab', { name: 'Chờ xác nhận', exact: true });
    await pendingTab.click();
    await expect(adminPage).toHaveURL(/[?&]status=pending_confirmation(&|$)/);
    await expect(pendingTab).toHaveAttribute('aria-selected', 'true');

    await pendingTab.focus();
    await adminPage.keyboard.press('ArrowRight');
    await expect(adminPage.getByRole('tab', { name: 'Đã xác nhận', exact: true })).toBeFocused();
  });

  await test.step('Nhân viên xác nhận đơn và thấy trạng thái Đã xác nhận', async () => {
    await adminPage.setViewportSize({ width: 1280, height: 800 });
    await adminPage.goto(`/admin/orders/${orderCode}`);
    const overview = adminPage.getByLabel('Tổng quan đơn');
    await expect(overview).toContainText('Chờ xác nhận');

    await adminPage.getByRole('button', { name: 'Xác nhận đơn', exact: true }).click();
    const dialog = adminPage.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Xác nhận', exact: true }).click();

    await expect(dialog).toBeHidden();
    await expect(overview).toContainText('Đã xác nhận');
    await expect(
      adminPage.getByRole('status').filter({ hasText: 'Đã xác nhận đơn.' }),
    ).toBeVisible();
  });

  for (const width of [1280, 375]) {
    await test.step(`Hủy đơn bắt buộc lý do, ảnh có nút và hộp thoại ở ${width}px`, async () => {
      await adminPage.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      await adminPage.goto(`/admin/orders/${orderCode}`);
      await expect(adminPage.getByLabel('Tổng quan đơn')).toContainText('Đã xác nhận');
      await adminPage.screenshot({
        path: testInfo.outputPath(`actions-${width}.png`),
        fullPage: true,
      });

      const cancelButton = adminPage.getByRole('button', { name: 'Hủy đơn', exact: true });
      await cancelButton.click();
      const dialog = adminPage.getByRole('dialog');
      await expect(dialog).toBeVisible();
      // Bấm thực hiện khi chưa nhập lý do thì thấy lỗi dưới ô và hộp thoại còn mở.
      await dialog.getByRole('button', { name: 'Xác nhận hủy đơn', exact: true }).click();
      await expect(dialog.getByText('Nhập lý do hủy đơn.')).toBeVisible();
      await expectNoHorizontalOverflow(adminPage);
      await adminPage.screenshot({ path: testInfo.outputPath(`cancel-dialog-${width}.png`) });

      // Escape đóng hộp thoại và trả focus về nút đã mở.
      await adminPage.keyboard.press('Escape');
      await expect(dialog).toBeHidden();
      await expect(cancelButton).toBeFocused();
    });
  }

  await test.step('Tạo vận đơn với kiện điền sẵn rồi thấy trạng thái Đang giao hàng', async () => {
    await adminPage.setViewportSize({ width: 1280, height: 800 });
    await adminPage.goto(`/admin/orders/${orderCode}`);
    await adminPage.getByRole('button', { name: 'Tạo vận đơn', exact: true }).click();
    const dialog = adminPage.getByRole('dialog');
    await expect(dialog).toBeVisible();
    // Kiện gợi ý do backend dựng từ dòng hàng, nên bốn ô đều có giá trị.
    for (const label of ['Cân nặng (g)', 'Dài (cm)', 'Rộng (cm)', 'Cao (cm)']) {
      await expect(dialog.getByLabel(label)).not.toHaveValue('');
    }
    await dialog.getByRole('button', { name: 'Tạo vận đơn', exact: true }).click();

    await expect(dialog).toBeHidden();
    await expect(adminPage.getByLabel('Tổng quan đơn')).toContainText('Đang giao hàng');
    await expect(adminPage.getByRole('button', { name: 'Hủy vận đơn', exact: true })).toBeVisible();
  });

  for (const width of [1280, 375]) {
    await test.step(`Ảnh chụp danh sách và chi tiết ở ${width}px không tràn ngang`, async () => {
      await adminPage.setViewportSize({ width, height: width === 375 ? 812 : 800 });
      const screens = [
        { path: `/admin/orders?search=${encodeURIComponent(orderCode)}`, name: 'list' },
        { path: `/admin/orders/${orderCode}`, name: 'detail' },
      ];
      for (const screen of screens) {
        await adminPage.goto(screen.path);
        await expect(adminPage.getByText(/^Đang nạp/)).toHaveCount(0);
        await expect(adminPage.getByText(orderCode).first()).toBeVisible();
        await expectNoHorizontalOverflow(adminPage);
        await adminPage.screenshot({
          path: testInfo.outputPath(`${screen.name}-${width}.png`),
          fullPage: true,
        });
      }
    });
  }

  expect(consoleIssues).toEqual([]);
  await adminContext.close();
});
