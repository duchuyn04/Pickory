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
  ['--color-on-dark', '--color-brand-deep'],
  ['--color-highlight-on-dark', '--color-brand-deep'],
  ['--color-highlight-on-dark', '--color-dark'],
  ['--color-on-brand', '--color-header-soft'],
  ['--color-on-dark-muted', '--color-dark'],
  ['--color-on-dark', '--color-link'],
  ['--color-brand', '--color-brand-soft'],
  ['--color-banner-fold-cta-ink', '--color-on-dark'],
  ['--color-on-dark', '--color-banner-iphone'],
  ['--color-banner-iphone-accent', '--color-banner-iphone'],
  ['--color-banner-on-dark-muted', '--color-banner-iphone'],
  ['--color-banner-ink', '--color-banner-duo'],
  ['--color-banner-duo-accent', '--color-banner-duo'],
  ['--color-banner-duo-muted', '--color-banner-duo'],
  ['--color-on-dark', '--color-banner-s26'],
  ['--color-banner-s26-accent', '--color-banner-s26'],
  ['--color-banner-on-dark-muted', '--color-banner-s26'],
  ['--color-on-dark', '--color-banner-s26-cta'],
  ['--color-on-dark', '--color-banner-fold'],
  ['--color-banner-fold-accent', '--color-banner-fold'],
  ['--color-banner-fold-muted', '--color-banner-fold'],
  ['--color-banner-ink', '--color-banner-flip'],
  ['--color-banner-flip-accent', '--color-banner-flip'],
  ['--color-banner-flip-muted', '--color-banner-flip'],
  ['--color-on-dark', '--color-banner-rog'],
  ['--color-banner-rog-accent', '--color-banner-rog'],
  ['--color-banner-on-dark-muted', '--color-banner-rog'],
  ['--color-banner-ink', '--color-banner-rog-accent'],
  ['--color-banner-ink', '--color-banner-sony'],
  ['--color-banner-sony-accent', '--color-banner-sony'],
  ['--color-banner-sony-muted', '--color-banner-sony'],
  ['--color-on-dark', '--color-banner-ink'],
  ['--color-banner-xiaomi-title', '--color-banner-xiaomi'],
  ['--color-banner-xiaomi-accent', '--color-banner-xiaomi'],
  ['--color-banner-on-dark-muted', '--color-banner-xiaomi'],
  ['--color-banner-ink', '--color-banner-xiaomi-accent'],
  ['--color-banner-ink', '--color-banner-dell'],
  ['--color-on-dark', '--color-promo-laptop'],
  ['--color-promo-laptop-accent', '--color-promo-laptop'],
  ['--color-banner-on-dark-muted', '--color-promo-laptop'],
  ['--color-banner-ink', '--color-promo-laptop-accent'],
  ['--color-banner-sony-muted', '--color-promo-audio'],
  ['--color-on-dark', '--color-promo-audio-accent'],
  ['--color-banner-ink', '--color-promo-audio'],
  ['--color-promo-audio-accent', '--color-promo-audio'],
  ['--color-ink', '--color-duel-left'],
  ['--color-ink-muted', '--color-duel-left'],
  ['--color-ink', '--color-duel-right'],
  ['--color-ink-muted', '--color-duel-right'],
  ['--color-duel-right-accent', '--color-duel-right'],
  ['--color-banner-dell-accent', '--color-banner-dell'],
  ['--color-banner-dell-muted', '--color-banner-dell'],
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
