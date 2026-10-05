import { z } from 'zod';
import './load-env'; // nạp .env trước khi bất kỳ ai gọi loadConfig()

// Mọi biến môi trường API cần, kèm giá trị mặc định cho dev. Module sau thêm biến vào đây.
const configSchema = z
  .object({
    NODE_ENV: z.enum(['development', 'test', 'production']).default('development'),
    PORT: z.coerce.number().int().positive().default(3000),
    WEB_ORIGIN: z.string().url().default('http://localhost:5173'),
    API_BASE_URL: z.string().url().default('http://localhost:3000'),
    GOOGLE_CLIENT_ID: z.string().default('mock_google_client_id'),
    GOOGLE_CLIENT_SECRET: z.string().default('mock_google_client_secret'),
    // TRUST_PROXY: số hop (số nguyên >= 0, ví dụ 1) hoặc danh sách IP/subnet proxy phân tách bằng dấu phẩy.
    // Mặc định tắt (0 / false). Tuyệt đối không dùng true để tránh tin cậy toàn bộ header X-Forwarded-For giả mạo.
    TRUST_PROXY: z
      .preprocess(
        (val) => {
          if (
            val === undefined ||
            val === null ||
            val === '' ||
            val === false ||
            val === '0' ||
            val === 0 ||
            val === 'false'
          ) {
            return false;
          }
          if (typeof val === 'number') {
            return val > 0 ? Math.floor(val) : false;
          }
          if (typeof val === 'string') {
            const trimmed = val.trim();
            if (trimmed === '' || trimmed === '0' || trimmed.toLowerCase() === 'false') {
              return false;
            }
            if (/^\d+$/.test(trimmed)) {
              const hops = parseInt(trimmed, 10);
              return hops > 0 ? hops : false;
            }
            const parts = trimmed
              .split(',')
              .map((p) => p.trim())
              .filter(Boolean);
            return parts.length > 0 ? parts : false;
          }
          if (Array.isArray(val)) {
            const filtered = val.filter(
              (item) => typeof item === 'string' && item.trim().length > 0,
            );
            return filtered.length > 0 ? filtered : false;
          }
          return false;
        },
        z.union([z.number().int().positive(), z.array(z.string().min(1)).min(1), z.literal(false)]),
      )
      .default(false),
    // Bắt buộc, không có mặc định: thiếu thì app không khởi động.
    DATABASE_URL: z.string().url(),
    // Không chọn model chat ngầm định vì ID model phải được người vận hành xác nhận.
    LLM_MODEL: z.string().default(''),
    LLM_BASE_URL: z.string().url().default('https://api.deepseek.com'),
    LLM_API_KEY: z.string().default(''),
    EMBEDDING_API_KEY: z.string().default(''),
    EMBEDDING_MODEL: z.string().min(1).default('text-embedding-3-small'),
    // Biến môi trường chỉ cấp giá trị khởi tạo, thay đổi sau đó được lưu trong DB.
    AI_MONTHLY_BUDGET_USD: z.coerce.number().finite().nonnegative().default(30),
    AI_CHAT_DAILY_LIMIT_USER: z.coerce.number().int().positive().default(50),
    AI_CHAT_DAILY_LIMIT_ANON: z.coerce.number().int().positive().default(15),
    AI_LIVE_TESTS: z
      .enum(['0', '1'])
      .default('0')
      .transform((value) => value === '1'),
  })
  .superRefine((data, ctx) => {
    // Có khóa nhưng thiếu model phải dừng cấu hình thay vì gửi request với ID rỗng.
    if (data.LLM_API_KEY && !data.LLM_MODEL.trim()) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'LLM_MODEL bắt buộc khi có LLM_API_KEY.',
        path: ['LLM_MODEL'],
      });
    }
    if (data.NODE_ENV === 'production') {
      if (!data.GOOGLE_CLIENT_ID || data.GOOGLE_CLIENT_ID.startsWith('mock_')) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: 'GOOGLE_CLIENT_ID bắt buộc và không được dùng mock ở môi trường production.',
          path: ['GOOGLE_CLIENT_ID'],
        });
      }
      if (!data.GOOGLE_CLIENT_SECRET || data.GOOGLE_CLIENT_SECRET.startsWith('mock_')) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: 'GOOGLE_CLIENT_SECRET bắt buộc và không được dùng mock ở môi trường production.',
          path: ['GOOGLE_CLIENT_SECRET'],
        });
      }
    }
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
