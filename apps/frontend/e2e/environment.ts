import { randomBytes } from 'node:crypto';
import { existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const environmentFile = new URL('../../../.env', import.meta.url);
// Đọc credential từ môi trường hiện tại, không lưu trong Git.
if (existsSync(environmentFile)) process.loadEnvFile(environmentFile);

const configuredE2eDatabase = process.env.E2E_DATABASE_URL?.trim();
const databaseConnection = configuredE2eDatabase || process.env.DATABASE_URL;
if (!databaseConnection) {
  throw new Error('Cần DATABASE_URL hoặc E2E_DATABASE_URL để kết nối PostgreSQL cho E2E.');
}
const databaseUrl = new URL(databaseConnection);
// Chỉ đổi tên DB. Giữ host và credential của PostgreSQL.
if (!configuredE2eDatabase) databaseUrl.pathname = '/pickory_e2e';
// Giới hạn tên DB để ngăn reset nhầm dữ liệu.
if (
  !['postgres:', 'postgresql:'].includes(databaseUrl.protocol) ||
  databaseUrl.pathname !== '/pickory_e2e'
) {
  throw new Error('DB E2E bắt buộc là PostgreSQL với tên pickory_e2e.');
}
for (const existingConnection of [process.env.DATABASE_URL, process.env.TEST_DATABASE_URL]) {
  // Từ chối tên DB trùng với dev hoặc integration.
  if (existingConnection && new URL(existingConnection).pathname === databaseUrl.pathname) {
    throw new Error('DB E2E phải khác DB dev và DB integration.');
  }
}

// Sinh mật khẩu một lần. Truyền qua env để server và worker dùng cùng giá trị.
process.env.E2E_ADMIN_PASSWORD ??= randomBytes(24).toString('base64url');
process.env.E2E_ACCOUNT_PASSWORD ??= randomBytes(24).toString('base64url');

export const e2eEnvironment = {
  // Khóa giả chỉ dùng cho API E2E, không dùng cho môi trường thật.
  sepayWebhookApiKey: 'com-ui4-e2e-key',
  databaseUrl: databaseUrl.toString(),
  webOrigin: 'http://127.0.0.1:4173',
  apiOrigin: 'http://127.0.0.1:3101',
  adminEmail: 'e2e-identity-admin@pickory.local',
  adminPassword: process.env.E2E_ADMIN_PASSWORD,
  customerEmail: 'e2e-identity-customer@pickory.local',
  // Mỗi spec dùng một khách riêng, nên khóa khách không ảnh hưởng đơn hàng hay màn hình khác.
  lockCustomerEmail: 'e2e-lock-customer@pickory.local',
  screensCustomerEmail: 'e2e-screens-customer@pickory.local',
  ordersCustomerEmail: 'e2e-orders-customer@pickory.local',
  seedVerificationEmail: 'e2e-seed-verification@pickory.local',
  salesEmail: 'e2e-identity-sales@pickory.local',
  catalogSalesEmail: 'e2e-catalog-sales@pickory.local',
  inventorySalesEmail: 'e2e-inventory-sales@pickory.local',
  productStatusSalesEmail: 'e2e-product-status-sales@pickory.local',
  shippingSalesEmail: 'e2e-shipping-sales@pickory.local',
  commerceSalesEmail: 'e2e-commerce-sales@pickory.local',
  accountPassword: process.env.E2E_ACCOUNT_PASSWORD,
  outboxDirectory: fileURLToPath(
    new URL('../../backend/.dev-outbox/e2e-identity/', import.meta.url),
  ),
};
