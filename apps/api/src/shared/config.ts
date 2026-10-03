import { z } from 'zod';
import './load-env'; // nạp .env trước khi bất kỳ ai gọi loadConfig()

// Mọi biến môi trường API cần, kèm giá trị mặc định cho dev. Module sau thêm biến vào đây.
const configSchema = z.object({
  NODE_ENV: z.enum(['development', 'test', 'production']).default('development'),
  PORT: z.coerce.number().int().positive().default(3000),
  WEB_ORIGIN: z.string().url().default('http://localhost:5173'),
  // Bắt buộc, không có mặc định: thiếu thì app không khởi động.
  DATABASE_URL: z.string().url(),
});

export type Config = z.infer<typeof configSchema>;

/**
 * Đọc và kiểm tra biến môi trường. Sai hoặc thiếu biến bắt buộc thì ném lỗi nêu rõ biến nào,
 * để app dừng ngay lúc khởi động thay vì lỗi muộn khi chạy.
 * `env` truyền vào được để test không phải đổi `process.env`.
 */
export function loadConfig(env: NodeJS.ProcessEnv = process.env): Config {
  const result = configSchema.safeParse(env);
  if (!result.success) {
    const missing = result.error.issues.map((i) => `${i.path.join('.')}: ${i.message}`).join('\n');
    throw new Error(`Cấu hình môi trường không hợp lệ:\n${missing}`);
  }
  return result.data;
}
