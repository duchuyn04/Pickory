import { defineConfig, env } from 'prisma/config';

// Prisma 7 không tự đọc .env nên nạp thủ công (bỏ qua nếu không có file, ví dụ trong CI).
try {
  process.loadEnvFile(new URL('../../.env', import.meta.url));
} catch {
  // không có .env: dùng biến môi trường có sẵn
}

export default defineConfig({
  schema: 'prisma/schema', // thư mục chứa nhiều file .prisma
  migrations: { path: 'prisma/migrations', seed: 'tsx prisma/seed.ts' },
  datasource: { url: env('DATABASE_URL') },
});
