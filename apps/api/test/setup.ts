// Chạy trước mỗi file test integration: trỏ sang DB test và dọn bảng giữa các test.
import './env';
import { afterAll, beforeEach } from 'vitest';
import { resetDatabase } from './db';

// Import động để `db` được tạo SAU KHI env.ts đã đổi DATABASE_URL sang DB test.
const { db } = await import('../src/shared/db');

beforeEach(async () => {
  await resetDatabase(db);
});

afterAll(async () => {
  await db.$disconnect();
});
