// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  readSoundEnabled,
  storeSoundEnabled,
} from '../../../../../src/features/support-desk/admin/alert-preferences';

beforeEach(() => {
  localStorage.clear();
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('âm báo của hàng chờ', () => {
  it('mặc định bật khi chưa lưu lựa chọn', () => {
    expect(readSoundEnabled()).toBe(true);
  });

  it('đọc lại lựa chọn tắt đã lưu', () => {
    storeSoundEnabled(false);

    expect(readSoundEnabled()).toBe(false);
  });

  it('đọc lại lựa chọn bật đã lưu sau khi từng tắt', () => {
    storeSoundEnabled(false);
    storeSoundEnabled(true);

    expect(readSoundEnabled()).toBe(true);
  });

  it('dùng mặc định bật khi localStorage ném lỗi lúc đọc', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('Bị chặn', 'SecurityError');
    });

    expect(readSoundEnabled()).toBe(true);
  });

  it('không ném lỗi khi localStorage ném lỗi lúc ghi', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('Đầy bộ nhớ', 'QuotaExceededError');
    });

    expect(() => storeSoundEnabled(false)).not.toThrow();
  });
});
