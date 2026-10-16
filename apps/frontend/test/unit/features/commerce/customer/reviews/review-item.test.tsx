// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { PublicReview } from '../../../../../../src/features/commerce/customer/reviews/reviews-api';
import { ReviewItem } from '../../../../../../src/features/commerce/customer/reviews/review-item';

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

function buildReview(overrides: Partial<PublicReview> = {}): PublicReview {
  return {
    id: 'review-1',
    rating: 4,
    body: 'Dùng ổn, pin tốt.',
    authorName: 'Nguyễn V.',
    createdAt: '2026-10-08T03:30:00Z',
    updatedAt: '2026-10-08T03:30:00Z',
    ...overrides,
  };
}

async function renderItem(review: PublicReview) {
  await act(async () => {
    root.render(
      <ul>
        <ReviewItem review={review} />
      </ul>,
    );
  });
}

describe('ReviewItem', () => {
  it('hiện tên người viết, số sao có tên truy cập, ngày viết theo giờ Việt Nam và nhận xét', async () => {
    await renderItem(buildReview());

    expect(container.textContent).toContain('Nguyễn V.');
    expect(container.querySelector('[role="img"]')?.getAttribute('aria-label')).toBe(
      '4 trên 5 sao',
    );
    expect(container.querySelector('time')?.textContent).toBe('10:30 08/10/2026');
    expect(container.textContent).toContain('Dùng ổn, pin tốt.');
  });

  it('hiện nhận xét chứa HTML nguyên văn, không dựng phần tử img hay b', async () => {
    const hostileBody = '<img src=x onerror=alert(1)>\n<b>đậm</b>';
    await renderItem(buildReview({ body: hostileBody }));

    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('b')).toBeNull();
    expect(container.querySelector('.review-body')?.textContent).toBe(hostileBody);
  });

  it('hiện tên người viết chứa HTML nguyên văn', async () => {
    await renderItem(buildReview({ authorName: '<script>x</script>' }));

    expect(container.querySelector('script')).toBeNull();
    expect(container.textContent).toContain('<script>x</script>');
  });

  it('không dựng khối nhận xét khi khách chỉ chấm sao', async () => {
    await renderItem(buildReview({ body: null }));

    expect(container.querySelector('.review-body')).toBeNull();
  });
});
