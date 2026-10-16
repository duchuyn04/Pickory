// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ReviewSummaryPanel } from '../../../../../../src/features/commerce/customer/reviews/review-summary-panel';

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

async function renderSummary(average: number | null, count: number) {
  await act(async () => {
    root.render(
      <ReviewSummaryPanel
        summary={{
          average,
          count,
          distribution: { '1': 0, '2': 0, '3': 0, '4': 1, '5': 1 },
        }}
      />,
    );
  });
}

function accessibleLabels(): string[] {
  return Array.from(container.querySelectorAll('[aria-label]')).map(
    (element) => element.getAttribute('aria-label') ?? '',
  );
}

describe('ReviewSummaryPanel', () => {
  it.each([
    [4.5, '4,5'],
    [4.4, '4,4'],
  ])(
    'điểm %s: tên truy cập của hàng sao nói đúng điểm, không làm tròn thành số nguyên',
    async (average, text) => {
      await renderSummary(average, 2);

      const labels = accessibleLabels();
      expect(labels).toContain(`Điểm trung bình ${text} trên 5 sao`);
      expect(labels.some((label) => label === '5 trên 5 sao' || label === '4 trên 5 sao')).toBe(
        false,
      );
    },
  );

  it('chưa có đánh giá thì hiện trạng thái rỗng, không có số liệu', async () => {
    await renderSummary(null, 0);

    expect(container.textContent).toBe('Chưa có đánh giá');
  });
});
