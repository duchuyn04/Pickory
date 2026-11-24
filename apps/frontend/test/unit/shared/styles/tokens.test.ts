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
  ['--color-on-dark', '--color-cat-dien-thoai'],
  ['--color-cat-dien-thoai-accent', '--color-cat-dien-thoai'],
  ['--color-on-dark-muted', '--color-cat-dien-thoai'],
  ['--color-banner-on-dark-muted', '--color-cat-dien-thoai'],
  ['--color-on-dark', '--color-cat-dien-thoai-cta'],
  ['--color-on-dark', '--color-cat-laptop'],
  ['--color-cat-laptop-accent', '--color-cat-laptop'],
  ['--color-on-dark-muted', '--color-cat-laptop'],
  ['--color-banner-on-dark-muted', '--color-cat-laptop'],
  ['--color-ink', '--color-cat-man-hinh'],
  ['--color-cat-man-hinh-accent', '--color-cat-man-hinh'],
  ['--color-ink-muted', '--color-cat-man-hinh'],
  ['--color-on-brand', '--color-cat-man-hinh-accent'],
  ['--color-ink', '--color-cat-tai-nghe'],
  ['--color-cat-tai-nghe-accent', '--color-cat-tai-nghe'],
  ['--color-ink-muted', '--color-cat-tai-nghe'],
  ['--color-on-brand', '--color-cat-tai-nghe-accent'],
  ['--color-ink', '--color-cat-phu-kien'],
  ['--color-cat-phu-kien-accent', '--color-cat-phu-kien'],
  ['--color-ink-muted', '--color-cat-phu-kien'],
  ['--color-on-brand', '--color-cat-phu-kien-accent'],
  ['--color-on-dark', '--color-small-banner-dark'],
  ['--color-cat-laptop-accent', '--color-small-banner-dark'],
  ['--color-on-dark-muted', '--color-small-banner-dark'],
  ['--color-banner-on-dark-muted', '--color-small-banner-dark'],
  ['--color-ink', '--color-small-banner-warm'],
  ['--color-cat-tai-nghe-accent', '--color-small-banner-warm'],
  ['--color-ink-muted', '--color-small-banner-warm'],
  ['--color-ink', '--color-small-banner-cool'],
  ['--color-small-banner-cool-accent', '--color-small-banner-cool'],
  ['--color-ink-muted', '--color-small-banner-cool'],
  ['--color-success', '--color-surface'],
];

// Mức tương phản tối thiểu cho biểu tượng đồ họa theo chuẩn WCAG.
const minimumGraphicContrast = 3;

// Danh sách token mới từ bản thiết kế kèm giá trị mong đợi để chặn thiếu hoặc sai lệch.
const storefrontRedesignTokens: [string, string][] = [
  ['--color-ink-disabled', '#c4c9d2'],
  ['--color-success-solid', '#16a34a'],
  ['--color-scrim-light', 'rgba(27, 31, 36, 0.25)'],
  ['--color-cat-dien-thoai', '#05070d'],
  ['--color-cat-dien-thoai-accent', '#8fc3ff'],
  ['--color-cat-dien-thoai-cta', '#1a5fd6'],
  ['--color-cat-laptop', '#101318'],
  ['--color-cat-laptop-accent', '#ff4d6d'],
  ['--color-cat-man-hinh', '#e9eef1'],
  ['--color-cat-man-hinh-accent', '#0a5cbc'],
  ['--color-cat-tai-nghe', '#efe6da'],
  ['--color-cat-tai-nghe-accent', '#85562a'],
  ['--color-cat-phu-kien', '#f1ebe2'],
  ['--color-cat-phu-kien-accent', '#85582e'],
  ['--color-small-banner-dark', '#1b1f24'],
  ['--color-small-banner-warm', '#efe6da'],
  ['--color-small-banner-cool', '#e7eefb'],
  ['--color-small-banner-cool-accent', '#1428a0'],
  ['--font-weight-regular', '400'],
  ['--font-weight-medium', '500'],
  ['--font-weight-semibold', '600'],
  ['--font-weight-bold', '700'],
  ['--font-weight-extrabold', '800'],
  ['--text-2xs', '10px'],
  ['--text-3xs', '11px'],
  ['--text-price', '15px'],
  ['--text-2xl', '26px'],
  ['--text-3xl', '28px'],
  ['--text-hero', '40px'],
  ['--space-1-5', '6px'],
  ['--space-2-5', '10px'],
  ['--space-3-5', '14px'],
  ['--card-padding', '20px'],
  ['--card-padding-compact', '16px'],
  ['--layout-content-width', '1200px'],
  ['--layout-gutter-mobile', '16px'],
  ['--layout-gutter-tablet', '24px'],
  ['--layout-breakpoint-tablet', '768px'],
  ['--layout-breakpoint-desktop', '1200px'],
  ['--layout-home-side-column', '224px'],
  ['--layout-home-main-column', '736px'],
  ['--layout-category-banner', '224px'],
  ['--layout-category-grid', '960px'],
  ['--layout-product-column', '592px'],
  ['--layout-summary-column', '392px'],
];

describe('design token', () => {
  const tokens = readDesignTokens();

  it('dùng màu thương hiệu đỏ và nền trang xám của bản mẫu shop-retail', () => {
    expect(readTokenValue(tokens, '--color-brand')).toBe('#d70018');
    expect(readTokenValue(tokens, '--color-page')).toBe('#f4f5f7');
  });

  it.each(storefrontRedesignTokens)(
    'khai báo đầy đủ token mới %s với giá trị %s',
    (name, expected) => {
      expect(readTokenValue(tokens, name)).toBe(expected);
    },
  );

  it.each(textOnBackgroundPairs)('%s trên %s đạt tương phản AA', (textToken, backgroundToken) => {
    const ratio = contrastRatio(
      readTokenValue(tokens, textToken),
      readTokenValue(tokens, backgroundToken),
    );
    expect(ratio).toBeGreaterThanOrEqual(minimumTextContrast);
  });

  it('--color-on-brand trên --color-success-solid đạt tương phản biểu tượng tối thiểu 3:1', () => {
    // Thiết kế chỉ dùng --color-success-solid cho nền hình tròn dấu tích không có chữ.
    // Chữ xanh lá vẫn dùng --color-success nên không áp dụng mức chữ thường 4.5:1 ở đây.
    const ratio = contrastRatio(
      readTokenValue(tokens, '--color-on-brand'),
      readTokenValue(tokens, '--color-success-solid'),
    );
    expect(ratio).toBeGreaterThanOrEqual(minimumGraphicContrast);
  });
});
