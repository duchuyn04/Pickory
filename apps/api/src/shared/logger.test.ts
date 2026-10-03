import { Writable } from 'node:stream';
import { describe, expect, it } from 'vitest';
import { createLogger } from './logger';

function capture() {
  const lines: string[] = [];
  const stream = new Writable({
    write(chunk, _enc, cb) {
      lines.push(String(chunk));
      cb();
    },
  });
  return { lines, logger: createLogger(stream, 'info') };
}

describe('logger redact', () => {
  it.each(['password', 'token', 'apiKey'])('không in giá trị của %s', (key) => {
    const { lines, logger } = capture();
    logger.info({ [key]: 'SECRET-VALUE', nested: { deep: { [key]: 'SECRET-VALUE' } } }, 'x');
    expect(lines.join('')).toContain('[đã ẩn]');
    expect(lines.join('')).not.toContain('SECRET-VALUE');
  });

  it('vẫn in field thường', () => {
    const { lines, logger } = capture();
    logger.info({ email: 'a@b.vn' }, 'x');
    expect(lines.join('')).toContain('a@b.vn');
  });
});
