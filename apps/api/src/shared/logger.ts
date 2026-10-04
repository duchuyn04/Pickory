import pino, { type DestinationStream, type Logger } from 'pino';

// Tên key mà giá trị không bao giờ được xuất hiện trong log (mật khẩu, token, khóa API...).
const SENSITIVE_KEYS = ['password', 'passwordHash', 'token', 'apiKey', 'authorization', 'cookie'];

// Che key nhạy cảm ở mức gốc và tối đa 3 cấp lồng nhau.
export const REDACT_PATHS = SENSITIVE_KEYS.flatMap((key) => [
  key,
  `*.${key}`,
  `*.*.${key}`,
  `*.*.*.${key}`,
]);

/**
 * Tạo logger. Production dùng `logger` bên dưới; test truyền `destination` để bắt dòng log
 * và `level` để ép bật log (mặc định logger tắt khi NODE_ENV=test cho đỡ nhiễu).
 */
export function createLogger(destination?: DestinationStream, level?: string): Logger {
  return pino(
    {
      level:
        level ?? process.env.LOG_LEVEL ?? (process.env.NODE_ENV === 'test' ? 'silent' : 'info'),
      redact: { paths: REDACT_PATHS, censor: '[đã ẩn]' },
    },
    destination,
  );
}

// Logger dùng chung cho toàn API. Không dùng console.log trong code thật.
export const logger = createLogger();
