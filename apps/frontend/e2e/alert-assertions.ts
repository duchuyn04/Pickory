import { expect, type Locator } from '@playwright/test';

// Thông báo lỗi dạng danh sách phải có icon và không có chấm tròn mặc định của trình duyệt.
export async function expectListAlertStyle(alert: Locator) {
  await expect(alert.locator('svg[aria-hidden="true"]').first()).toBeVisible();
  const listStyleTypes = await alert
    .locator('li')
    .evaluateAll((items) => items.map((item) => getComputedStyle(item).listStyleType));
  expect(listStyleTypes.length).toBeGreaterThan(0);
  expect(listStyleTypes.every((type) => type === 'none')).toBe(true);
}
