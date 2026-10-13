import { randomInt } from 'node:crypto';
import { expect, test } from '@playwright/test';
import {
  expectNoHorizontalOverflow,
  placeCodOrder,
  prepareShipping,
  seedProduct,
} from './admin-order-fixtures';
import { collectConsoleIssues, loginAdmin } from './catalog-admin-flow';
import { e2eEnvironment } from './environment';

const accountNumber = '1017588888';
const adminHeaders = { Origin: e2eEnvironment.webOrigin };

test('COM-UI6: nhân viên đối soát giao dịch SePay chưa khớp, đổi tab bằng bàn phím, không tràn trang', async ({
  browser,
  page,
}, testInfo) => {
  test.setTimeout(240000);
  const adminContext = await browser.newContext({ baseURL: e2eEnvironment.webOrigin });
  const adminPage = await adminContext.newPage();
  const consoleIssues = collectConsoleIssues(adminPage);

  await loginAdmin(adminPage);

  // Webhook chỉ để giao dịch ở trạng thái chưa khớp khi số tài khoản trùng cấu hình. Lưu cấu hình cũ để khôi phục.
  const previousResponse = await adminPage.request.get('/api/admin/commerce/settings');
  expect(previousResponse.status()).toBe(200);
  const previous = (await previousResponse.json()).data as {
    freeShippingThreshold: number;
    codMaxAmount: number;
    maxQuantityPerSku: number;
    maxCartLines: number;
    sepayAccountNumber: string | null;
    sepayBankCode: string | null;
  };
  // Response có thêm trường như `updatedAt` mà route ghi từ chối, nên chỉ gửi đúng sáu trường cấu hình.
  const editableSettings = {
    freeShippingThreshold: previous.freeShippingThreshold,
    codMaxAmount: previous.codMaxAmount,
    maxQuantityPerSku: previous.maxQuantityPerSku,
    maxCartLines: previous.maxCartLines,
    sepayAccountNumber: previous.sepayAccountNumber,
    sepayBankCode: previous.sepayBankCode,
  };
  const withSepay = await adminPage.request.put('/api/admin/commerce/settings', {
    headers: adminHeaders,
    data: { ...editableSettings, sepayAccountNumber: accountNumber, sepayBankCode: 'VCB' },
  });
  expect(withSepay.status(), await withSepay.text()).toBe(200);

  try {
    const product = await seedProduct(adminPage);
    const address = await prepareShipping(adminPage);

    let orderCode = '';
    await test.step('Khách đặt một đơn COD để có mã đơn thật', async () => {
      orderCode = await placeCodOrder(page, product, address);
    });

    // Nội dung không chứa mã đơn nào, nên webhook ghi giao dịch ở trạng thái chưa khớp.
    const suffix = Date.now().toString(36);
    const content = `E2E KHONG KHOP ${suffix}`;
    await test.step('SePay gửi một giao dịch không khớp đơn nào', async () => {
      const webhook = await adminPage.request.post('/api/webhooks/sepay', {
        headers: { Authorization: `Apikey ${e2eEnvironment.sepayWebhookApiKey}` },
        data: {
          id: randomInt(100000000, 999999999),
          gateway: 'Vietcombank',
          transactionDate: '2026-10-12 10:00:00',
          accountNumber,
          subAccount: '',
          code: null,
          content,
          transferType: 'in',
          description: '',
          transferAmount: 123000,
          accumulated: 105000000,
          referenceCode: `E2E-${suffix}`,
        },
      });
      expect(webhook.status(), await webhook.text()).toBe(200);
    });

    await test.step('Đổi giữa tab SePay và COD bằng phím mũi tên và Enter', async () => {
      await adminPage.setViewportSize({ width: 1280, height: 800 });
      await adminPage.goto('/admin/payments');
      await expect(
        adminPage.getByRole('heading', { level: 1, name: 'Đối soát thanh toán' }),
      ).toBeVisible();
      const sepayTab = adminPage.getByRole('tab', { name: 'SePay', exact: true });
      const codTab = adminPage.getByRole('tab', { name: 'COD', exact: true });
      await expect(sepayTab).toHaveAttribute('aria-selected', 'true');

      await sepayTab.focus();
      await adminPage.keyboard.press('ArrowRight');
      await expect(codTab).toBeFocused();
      await adminPage.keyboard.press('Enter');
      await expect(adminPage).toHaveURL(/[?&]tab=cod(&|$)/);
      await expect(codTab).toHaveAttribute('aria-selected', 'true');

      await adminPage.keyboard.press('ArrowLeft');
      await expect(sepayTab).toBeFocused();
      await adminPage.keyboard.press('Enter');
      await expect(sepayTab).toHaveAttribute('aria-selected', 'true');
    });

    await test.step('Gắn vào đơn không nhận chuyển khoản hiện thông báo của API', async () => {
      await adminPage.goto('/admin/payments');
      const row = adminPage.locator('tbody tr').filter({ hasText: content });
      await expect(row).toHaveCount(1);
      await expect(row).toContainText('Chưa khớp');
      await row.getByRole('button', { name: 'Gắn vào đơn', exact: true }).click();

      const dialog = adminPage.getByRole('dialog');
      await expect(dialog).toBeVisible();
      // Nút Gắn tắt cho tới khi tra được đơn.
      await expect(dialog.getByRole('button', { name: 'Gắn', exact: true })).toBeDisabled();

      await dialog.getByLabel('Mã đơn').fill(orderCode);
      await dialog.getByRole('button', { name: 'Tìm', exact: true }).click();
      await expect(dialog.getByLabel('Tóm tắt đơn')).toContainText(orderCode);
      await dialog.getByRole('button', { name: 'Gắn', exact: true }).click();

      // Đơn COD không nhận được tiền chuyển khoản, nên backend trả 409 và hộp thoại còn mở.
      await expect(dialog.getByRole('alert')).toContainText(
        'Đơn không ở trạng thái nhận được tiền',
      );
      await expect(dialog).toBeVisible();

      // Mã không tồn tại hiện lỗi 404 và nút Gắn tắt lại.
      await dialog.getByLabel('Mã đơn').fill('PKKHONGCOTHAT');
      await dialog.getByRole('button', { name: 'Tìm', exact: true }).click();
      await expect(dialog.getByRole('alert')).toContainText('Không tìm thấy đơn hàng.');
      await expect(dialog.getByRole('button', { name: 'Gắn', exact: true })).toBeDisabled();
    });

    for (const width of [1280, 375]) {
      await test.step(`Hộp thoại gắn đơn ở ${width}px không tràn ngang`, async () => {
        await adminPage.setViewportSize({ width, height: width === 375 ? 812 : 800 });
        await expectNoHorizontalOverflow(adminPage);
        await adminPage.screenshot({ path: testInfo.outputPath(`attach-dialog-${width}.png`) });
      });
    }

    await test.step('Bỏ qua giao dịch: xác nhận rồi thấy thông báo và dòng rời danh sách', async () => {
      await adminPage.keyboard.press('Escape');
      await expect(adminPage.getByRole('dialog')).toBeHidden();

      const row = adminPage.locator('tbody tr').filter({ hasText: content });
      await row.getByRole('button', { name: 'Bỏ qua', exact: true }).click();
      const dialog = adminPage.getByRole('dialog');
      await expect(dialog).toContainText('không thuộc đơn nào');
      await dialog.getByRole('button', { name: 'Bỏ qua', exact: true }).click();

      await expect(dialog).toBeHidden();
      await expect(
        adminPage.getByRole('status').filter({ hasText: 'Đã bỏ qua giao dịch.' }),
      ).toBeVisible();
      // Bộ lọc mặc định là chưa khớp, nên dòng vừa bỏ qua rời danh sách sau khi tải lại.
      await expect(adminPage.locator('tbody tr').filter({ hasText: content })).toHaveCount(0);
    });

    for (const width of [1280, 375]) {
      await test.step(`Ảnh chụp hai tab ở ${width}px không tràn ngang`, async () => {
        await adminPage.setViewportSize({ width, height: width === 375 ? 812 : 800 });
        const screens = [
          { path: '/admin/payments?matchStatus=all', name: 'sepay' },
          { path: '/admin/payments?tab=cod', name: 'cod' },
        ];
        for (const screen of screens) {
          await adminPage.goto(screen.path);
          await expect(adminPage.getByText(/^Đang nạp/)).toHaveCount(0);
          await expectNoHorizontalOverflow(adminPage);
          await adminPage.screenshot({
            path: testInfo.outputPath(`${screen.name}-${width}.png`),
            fullPage: true,
          });
        }
      });
    }

    // Trình duyệt ghi 404 và 409 của hai lần thử cố ý ở bước gắn đơn. Đây là kết quả mong đợi, không phải lỗi của trang.
    const unexpectedIssues = consoleIssues.filter((issue) => !/status of (404|409)/.test(issue));
    expect(unexpectedIssues).toEqual([]);
  } finally {
    // Khôi phục cấu hình kể cả khi bước nào đó thất bại, để các spec khác không bị ảnh hưởng.
    const restored = await adminPage.request.put('/api/admin/commerce/settings', {
      headers: adminHeaders,
      data: editableSettings,
    });
    expect(restored.status(), await restored.text()).toBe(200);
    await adminContext.close();
  }
});
