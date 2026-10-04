// Chạy trước mỗi file test integration: trỏ sang DB test và dọn bảng giữa các test.
import './env';
import request from 'supertest';

// Tự động gắn Origin cho request integration test để vượt qua kiểm tra CSRF
// ngoại trừ các test chủ động kiểm tra thiếu Origin bằng header `x-skip-origin: true`
interface SupertestModule {
  Test?: {
    prototype: {
      end: (fn?: unknown) => unknown;
      set: (field: string, val: string) => void;
      header?: Record<string, string>;
    };
  };
}

const reqMod: unknown = request;
if (
  (typeof reqMod === 'function' || typeof reqMod === 'object') &&
  reqMod !== null &&
  'Test' in reqMod
) {
  const supertestModule = reqMod as SupertestModule;
  const supertestTest = supertestModule.Test;
  if (supertestTest?.prototype) {
    const origEnd = supertestTest.prototype.end;
    supertestTest.prototype.end = function (fn?: unknown) {
      const target = this as {
        getHeader?: (name: string) => string | undefined;
        header?: Record<string, string>;
        set: (k: string, v: string) => void;
      };
      const existingOrigin = target.getHeader
        ? target.getHeader('origin')
        : target.header?.Origin || target.header?.origin;
      const skipOrigin = target.getHeader
        ? target.getHeader('x-skip-origin')
        : target.header?.['x-skip-origin'] || target.header?.['X-Skip-Origin'];

      if (!existingOrigin && !skipOrigin) {
        target.set('Origin', 'http://localhost:5173');
      }
      if (skipOrigin && target.header) {
        delete target.header['x-skip-origin'];
        delete target.header['X-Skip-Origin'];
      }
      return origEnd.call(this, fn);
    };
  }
}
import { afterAll, beforeEach } from 'vitest';
import { resetDatabase } from './db';
import { clearAllRateLimits } from '../src/modules/identity/security/rate-limit';

// Import động để `db` được tạo SAU KHI env.ts đã đổi DATABASE_URL sang DB test.
const { db } = await import('../src/shared/db');

beforeEach(async () => {
  await resetDatabase(db);
  clearAllRateLimits();
});

afterAll(async () => {
  await db.$disconnect();
});
