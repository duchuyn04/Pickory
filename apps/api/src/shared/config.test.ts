import { describe, expect, it } from 'vitest';
import { loadConfig } from './config';

describe('loadConfig', () => {
  it('dùng giá trị mặc định', () => {
    expect(loadConfig({}).PORT).toBe(3000);
  });

  it('báo rõ biến nào sai', () => {
    expect(() => loadConfig({ PORT: 'abc' })).toThrow(/PORT/);
  });
});
