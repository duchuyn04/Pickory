import express, { type Express } from 'express';
import { pinoHttp } from 'pino-http';
import { errorHandler, notFoundHandler } from './shared/errors';
import { logger } from './shared/logger';

/**
 * Dựng Express app. Tách khỏi `server.ts` (nơi mở port) để test dùng Supertest
 * gọi thẳng app mà không cần listen.
 */
export function createApp(): Express {
  const app = express();
  app.disable('x-powered-by'); // không lộ công nghệ server
  app.use(pinoHttp({ logger }));
  app.use(express.json({ limit: '1mb' }));

  // Health check cho dev, deploy và web skeleton kiểm tra API còn sống.
  app.get('/api/health', (_req, res) => {
    res.json({ data: { status: 'ok' } });
  });

  // Thứ tự quan trọng: 404 rồi tới error handler, cả hai đặt sau mọi route.
  app.use(notFoundHandler);
  app.use(errorHandler);
  return app;
}
