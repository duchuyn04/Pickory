import express from 'express';
import request from 'supertest';
import { z } from 'zod';
import { describe, expect, it } from 'vitest';
import { createApp } from '../../src/app';
import { AppError, errorHandler } from '../../src/shared/errors';

describe('GET /api/health', () => {
  it('trả ok', async () => {
    const res = await request(createApp()).get('/api/health');
    expect(res.status).toBe(200);
    expect(res.body).toEqual({ data: { status: 'ok' } });
  });

  it('route lạ trả 404 theo mẫu lỗi', async () => {
    const res = await request(createApp()).get('/api/nope');
    expect(res.status).toBe(404);
    expect(res.body.error.code).toBe('NOT_FOUND');
  });
});

describe('errorHandler', () => {
  const app = express();
  app.get('/app-error', () => {
    throw new AppError('PRODUCT_NOT_FOUND', 404, 'Không có sản phẩm.');
  });
  app.get('/zod', () => {
    z.object({ name: z.string() }).parse({});
  });
  app.get('/boom', () => {
    throw new Error('secret stack detail');
  });
  app.use(errorHandler);

  it('AppError giữ code, status, message', async () => {
    const res = await request(app).get('/app-error');
    expect(res.status).toBe(404);
    expect(res.body).toEqual({
      error: { code: 'PRODUCT_NOT_FOUND', message: 'Không có sản phẩm.' },
    });
  });

  it('ZodError trả 400', async () => {
    const res = await request(app).get('/zod');
    expect(res.status).toBe(400);
    expect(res.body.error.code).toBe('VALIDATION_ERROR');
  });

  it('lỗi lạ trả 500 và không lộ chi tiết', async () => {
    const res = await request(app).get('/boom');
    expect(res.status).toBe(500);
    expect(JSON.stringify(res.body)).not.toContain('secret stack detail');
  });
});
