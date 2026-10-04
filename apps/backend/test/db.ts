import type { PrismaClient } from '../src/generated/prisma/client';

/**
 * Xóa sạch dữ liệu mọi bảng trong DB test (giữ nguyên cấu trúc và bảng migration của Prisma).
 * Tự động chạy trước mỗi test integration, nên test không phụ thuộc thứ tự chạy.
 */
export async function resetDatabase(db: PrismaClient): Promise<void> {
  const tables = await db.$queryRaw<{ tablename: string }[]>`
    SELECT tablename FROM pg_tables
    WHERE schemaname = 'public' AND tablename <> '_prisma_migrations'
  `;
  if (tables.length === 0) return;
  const names = tables.map((t) => `"${t.tablename}"`).join(', ');
  await db.$executeRawUnsafe(`TRUNCATE TABLE ${names} RESTART IDENTITY CASCADE`);
}
