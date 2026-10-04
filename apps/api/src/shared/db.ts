import { PrismaPg } from '@prisma/adapter-pg';
import { PrismaClient } from '../generated/prisma/client';
import { loadConfig } from './config';

/**
 * Tạo Prisma client nối tới `connectionString`.
 * Prisma 7 cần driver adapter (`pg`) thay vì engine tích hợp sẵn.
 */
export function createDb(connectionString: string): PrismaClient {
  return new PrismaClient({ adapter: new PrismaPg({ connectionString }) });
}

// Client dùng chung cho toàn API. Chỉ module sở hữu bảng mới được ghi vào bảng đó.
export const db = createDb(loadConfig().DATABASE_URL);
