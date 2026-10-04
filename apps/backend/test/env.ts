// Nạp .env (nếu có) và trỏ DATABASE_URL sang DB test, dùng chung cho global setup và setup từng file test.
// Mọi test integration chỉ được chạm DB test, không bao giờ chạm DB dev.
try {
  process.loadEnvFile(new URL('../../../.env', import.meta.url));
} catch {
  // không có .env: dùng biến môi trường có sẵn (ví dụ trong CI)
}

export const TEST_DATABASE_URL =
  process.env.TEST_DATABASE_URL ?? 'postgresql://pickory:pickory@localhost:5432/pickory_test';

process.env.DATABASE_URL = TEST_DATABASE_URL;
