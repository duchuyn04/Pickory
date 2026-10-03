import { describe, expect, it } from 'vitest';
import { loadConfig } from './config';

const validEnv = { DATABASE_URL: 'postgresql://u:p@localhost:5432/db' };

describe('loadConfig', () => {
  it('dùng giá trị mặc định cho biến không bắt buộc', () => {
    expect(loadConfig(validEnv).PORT).toBe(3000);
  });

  it('báo rõ biến nào sai', () => {
    expect(() => loadConfig({ ...validEnv, PORT: 'abc' })).toThrow(/PORT/);
  });

  it('thiếu biến bắt buộc thì báo đúng tên biến', () => {
    expect(() => loadConfig({})).toThrow(/DATABASE_URL/);
  });
});
