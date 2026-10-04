import { afterAll, describe, expect, it } from 'vitest';
import { db } from '../../../src/shared/db';

// Test mẫu chạy qua PostgreSQL thật. Dùng bảng tạm riêng vì chưa có module nào có bảng.
describe('hạ tầng DB test', () => {
  afterAll(async () => {
    await db.$executeRawUnsafe('DROP TABLE IF EXISTS int_sample');
  });

  it('ghi và đọc được qua DB thật', async () => {
    await db.$executeRawUnsafe('CREATE TABLE IF NOT EXISTS int_sample (id serial, name text)');
    await db.$executeRawUnsafe("INSERT INTO int_sample (name) VALUES ('a')");
    const rows = await db.$queryRawUnsafe<{ name: string }[]>('SELECT name FROM int_sample');
    expect(rows).toEqual([{ name: 'a' }]);
  });

  it('bảng được dọn sạch giữa các test', async () => {
    const rows = await db.$queryRawUnsafe<unknown[]>('SELECT * FROM int_sample');
    expect(rows).toHaveLength(0);
  });

  it('extension vector đã bật từ migration đầu', async () => {
    const rows = await db.$queryRawUnsafe<{ dim: number }[]>(
      "SELECT vector_dims('[1,2,3]'::vector) AS dim",
    );
    expect(rows[0]?.dim).toBe(3);
  });

  it('đang chạy trên DB test, không phải DB dev', async () => {
    const rows = await db.$queryRaw<{ current_database: string }[]>`SELECT current_database()`;
    expect(rows[0]?.current_database).toBe('pickory_test');
  });
});
