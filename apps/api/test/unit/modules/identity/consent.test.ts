import { describe, expect, it } from 'vitest';
import { resolveLatestConsent } from '../../../../src/modules/identity/consent';

describe('ID-08 consent resolution unit', () => {
  it('cả hai null trả về mặc định false', () => {
    const resolved = resolveLatestConsent(null, null);
    expect(resolved).toEqual({ personalization: false, marketing: false });
  });

  it('chỉ có guest thì lấy của guest', () => {
    const guest = {
      personalization: true,
      marketing: false,
      createdAt: new Date('2026-10-01T10:00:00Z'),
    };
    const resolved = resolveLatestConsent(guest, null);
    expect(resolved).toEqual({ personalization: true, marketing: false });
  });

  it('chỉ có user thì lấy của user', () => {
    const user = {
      personalization: false,
      marketing: true,
      createdAt: new Date('2026-10-01T10:00:00Z'),
    };
    const resolved = resolveLatestConsent(null, user);
    expect(resolved).toEqual({ personalization: false, marketing: true });
  });

  it('cả hai cùng có: lấy bản ghi có thời điểm tạo mới hơn', () => {
    const olderGuest = {
      personalization: false,
      marketing: false,
      createdAt: new Date('2026-10-01T08:00:00Z'),
    };
    const newerUser = {
      personalization: true,
      marketing: true,
      createdAt: new Date('2026-10-01T12:00:00Z'),
    };

    expect(resolveLatestConsent(olderGuest, newerUser)).toEqual({
      personalization: true,
      marketing: true,
    });

    const newerGuest = {
      personalization: true,
      marketing: false,
      createdAt: new Date('2026-10-01T14:00:00Z'),
    };
    expect(resolveLatestConsent(newerGuest, newerUser)).toEqual({
      personalization: true,
      marketing: false,
    });
  });
});
