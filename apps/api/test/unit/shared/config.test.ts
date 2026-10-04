import { describe, expect, it } from 'vitest';
import { loadConfig } from '../../../src/shared/config';

const validEnv = { DATABASE_URL: 'postgresql://u:p@localhost:5432/db' };

describe('loadConfig', () => {
  it('dùng giá trị mặc định cho biến không bắt buộc', () => {
    expect(loadConfig(validEnv).PORT).toBe(3000);
  });

  it('TRUST_PROXY mặc định là false, nhận số hop hoặc mảng IP/subnet', () => {
    expect(loadConfig(validEnv).TRUST_PROXY).toBe(false);
    expect(loadConfig({ ...validEnv, TRUST_PROXY: '0' }).TRUST_PROXY).toBe(false);
    expect(loadConfig({ ...validEnv, TRUST_PROXY: 'false' }).TRUST_PROXY).toBe(false);
    expect(loadConfig({ ...validEnv, TRUST_PROXY: '' }).TRUST_PROXY).toBe(false);
    expect(loadConfig({ ...validEnv, TRUST_PROXY: '1' }).TRUST_PROXY).toBe(1);
    expect(loadConfig({ ...validEnv, TRUST_PROXY: '2' }).TRUST_PROXY).toBe(2);
    expect(loadConfig({ ...validEnv, TRUST_PROXY: '127.0.0.1, 10.0.0.0/8' }).TRUST_PROXY).toEqual([
      '127.0.0.1',
      '10.0.0.0/8',
    ]);
  });

  it('báo rõ biến nào sai', () => {
    expect(() => loadConfig({ ...validEnv, PORT: 'abc' })).toThrow(/PORT/);
  });

  it('thiếu biến bắt buộc thì báo đúng tên biến', () => {
    expect(() => loadConfig({})).toThrow(/DATABASE_URL/);
  });
});
