import { describe, expect, it } from 'vitest';
import { sanitizeAuditData } from '../../../../src/modules/identity/audit';

describe('ID-04 audit log sensitive data sanitization', () => {
  it('loại bỏ các trường nhạy cảm như password, token, secret, apiKey', () => {
    const input = {
      name: 'Nguyen Van A',
      email: 'a@example.com',
      password: 'plain_password',
      passwordHash: '$argon2id$something',
      token: 'session_token_xyz',
      tokenHash: 'sha256_hash',
      apiKey: 'sk-12345',
      secret: 'my_secret',
      cardNumber: '1234567812345678',
      nested: {
        normalField: 123,
        api_key: 'nested_key',
        password_hash: 'nested_hash',
      },
      roles: ['sales', 'support'],
    };

    const sanitized = sanitizeAuditData(input) as Record<string, unknown>;

    expect(sanitized.name).toBe('Nguyen Van A');
    expect(sanitized.email).toBe('a@example.com');
    expect(sanitized.password).toBe('[REDACTED]');
    expect(sanitized.passwordHash).toBe('[REDACTED]');
    expect(sanitized.token).toBe('[REDACTED]');
    expect(sanitized.tokenHash).toBe('[REDACTED]');
    expect(sanitized.apiKey).toBe('[REDACTED]');
    expect(sanitized.secret).toBe('[REDACTED]');
    expect(sanitized.cardNumber).toBe('[REDACTED]');

    const nested = sanitized.nested as Record<string, unknown>;
    expect(nested.normalField).toBe(123);
    expect(nested.api_key).toBe('[REDACTED]');
    expect(nested.password_hash).toBe('[REDACTED]');
  });
});
