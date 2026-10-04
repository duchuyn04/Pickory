import cookieParser from 'cookie-parser';
import express, { type Express } from 'express';
import { pinoHttp } from 'pino-http';
import { actorMiddleware, identityRouter } from './modules/identity';
import { errorHandler, notFoundHandler } from './shared/errors';
import { logger } from './shared/logger';
import type { Config } from './shared/config';
import { loadConfig } from './shared/config';
/**
 * Dựng Express app. Tách khỏi `server.ts` (nơi mở port) để test dùng Supertest
 * gọi thẳng app mà không cần listen.
 */
export function createApp(configOverrides?: Partial<Config>): Express {
  const app = express();
  const baseConfig = loadConfig();
  const config = configOverrides ? { ...baseConfig, ...configOverrides } : baseConfig;
  if (config.TRUST_PROXY !== false) {
    // Chỉ tin cậy số hop xác định (ví dụ 1 hop) hoặc danh sách IP/subnet proxy cụ thể.
    // Tuyệt đối không dùng true để tránh kẻ xấu giả mạo header X-Forwarded-For vượt qua rate limit.
    app.set('trust proxy', config.TRUST_PROXY);
  }
  app.disable('x-powered-by'); // không lộ công nghệ server
  app.use(pinoHttp({ logger }));
  app.use(express.json({ limit: '1mb' }));
  app.use(cookieParser());
  app.use(actorMiddleware);

  // Health check cho dev, deploy và web skeleton kiểm tra API còn sống.
  app.get('/api/health', (_req, res) => {
    res.json({ data: { status: 'ok' } });
  });

  app.use(identityRouter);

  // Thứ tự quan trọng: 404 rồi tới error handler, cả hai đặt sau mọi route.
  app.use(notFoundHandler);
  app.use(errorHandler);
  return app;
}
