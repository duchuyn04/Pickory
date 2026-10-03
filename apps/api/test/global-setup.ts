import { execSync } from 'node:child_process';
import pg from 'pg';
import { TEST_DATABASE_URL } from './env';

/**
 * Chạy 1 lần trước toàn bộ test integration:
 * 1. Tạo DB test nếu chưa có (volume Postgres cũ có thể chưa chạy init script).
 * 2. Áp dụng toàn bộ migration lên DB test.
 */
export default async function globalSetup(): Promise<void> {
  const url = new URL(TEST_DATABASE_URL);
  const testDbName = url.pathname.slice(1);

  // Kết nối vào DB hệ thống `postgres` để kiểm tra và tạo DB test.
  url.pathname = '/postgres';
  const admin = new pg.Client({ connectionString: url.toString() });
  try {
    await admin.connect();
  } catch (err) {
    throw new Error('Không kết nối được PostgreSQL. Đã chạy "docker compose up -d db" chưa?', {
      cause: err,
    });
  }
  try {
    const found = await admin.query('SELECT 1 FROM pg_database WHERE datname = $1', [testDbName]);
    if (found.rowCount === 0) {
      // Tên DB lấy từ cấu hình của chính dự án, không phải input người dùng.
      await admin.query(`CREATE DATABASE "${testDbName}"`);
    }
  } finally {
    await admin.end();
  }

  execSync('pnpm --filter @pickory/api db:migrate:deploy', {
    stdio: 'pipe',
    env: { ...process.env, DATABASE_URL: TEST_DATABASE_URL },
  });
}
