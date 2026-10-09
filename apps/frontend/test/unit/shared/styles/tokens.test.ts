import { describe, expect, it } from 'vitest';
import { contrastRatio } from './color-contrast';
import { readDesignTokens, readTokenValue } from './design-tokens';

// Mức AA của WCAG cho chữ cỡ thường.
const minimumTextContrast = 4.5;

// Mỗi cặp là màu chữ và màu nền xuất hiện cùng nhau trên giao diện.
const textOnBackgroundPairs: [string, string][] = [
  ['--color-ink', '--color-page'],
  ['--color-ink', '--color-surface'],
  ['--color-ink-muted', '--color-page'],
  ['--color-ink-muted', '--color-surface'],
  ['--color-ink-muted', '--color-surface-muted'],
  ['--color-on-brand', '--color-brand'],
  ['--color-on-brand', '--color-brand-hover'],
  ['--color-brand', '--color-surface'],
  ['--color-price', '--color-surface'],
  ['--color-link', '--color-surface'],
  ['--color-danger', '--color-surface'],
  ['--color-success', '--color-success-soft'],
  ['--color-warning', '--color-warning-soft'],
  ['--color-danger', '--color-danger-soft'],
  ['--color-info', '--color-info-soft'],
  ['--color-ink-secondary', '--color-neutral-soft'],
  ['--color-ink-secondary', '--color-surface-muted'],
  ['--color-on-dark', '--color-dark'],
  ['--color-on-brand', '--color-header-soft'],
];

describe('design token', () => {
  const tokens = readDesignTokens();

  it('dùng màu thương hiệu đỏ và nền trang xám của bản mẫu shop-retail', () => {
    expect(readTokenValue(tokens, '--color-brand')).toBe('#d70018');
    expect(readTokenValue(tokens, '--color-page')).toBe('#f4f5f7');
  });

  it.each(textOnBackgroundPairs)('%s trên %s đạt tương phản AA', (textToken, backgroundToken) => {
    const ratio = contrastRatio(
      readTokenValue(tokens, textToken),
      readTokenValue(tokens, backgroundToken),
    );
    expect(ratio).toBeGreaterThanOrEqual(minimumTextContrast);
  });
});
