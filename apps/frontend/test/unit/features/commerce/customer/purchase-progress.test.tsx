// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PurchaseProgress } from '../../../../../src/features/commerce/customer/purchase-progress';

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

async function renderProgress(stage: 'cart' | 'checkout' | 'payment' | 'success') {
  await act(async () =>
    root.render(
      <MemoryRouter>
        <PurchaseProgress stage={stage} />
      </MemoryRouter>,
    ),
  );
}

describe('PurchaseProgress', () => {
  it('đánh dấu bước hiện tại và liên kết giỏ hàng ở bước đặt hàng', async () => {
    await renderProgress('checkout');

    const steps = container.querySelectorAll('.purchase-progress-step');
    expect(steps).toHaveLength(3);
    expect(steps[0]?.getAttribute('data-state')).toBe('done');
    expect(steps[1]?.getAttribute('data-state')).toBe('current');
    expect(steps[1]?.getAttribute('aria-current')).toBe('step');
    expect(steps[2]?.getAttribute('data-state')).toBe('upcoming');
    expect(steps[0]?.querySelector('a')?.getAttribute('href')).toBe('/cart');
  });

  it('đánh dấu bước thanh toán là bước hiện tại', async () => {
    await renderProgress('payment');

    const currentStep = container.querySelector('.purchase-progress-step[data-state="current"]');
    expect(currentStep?.textContent).toContain('Thanh toán và hoàn tất');
    expect(currentStep?.getAttribute('aria-current')).toBe('step');
  });

  it('đánh dấu cả ba bước đã xong ở trang thành công', async () => {
    await renderProgress('success');

    expect(container.querySelectorAll('.purchase-progress-step[data-state="done"]')).toHaveLength(
      3,
    );
    expect(container.querySelector('[aria-current="step"]')).toBeNull();
  });
});
