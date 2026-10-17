// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Header } from '../../../src/shared/header';
import { buildCart } from '../features/commerce/customer/cart-fixtures';
import { renderWithProviders, stubCartApi } from '../features/commerce/customer/cart-test-utils';

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

describe('Header', () => {
  it('có link Tra cứu đơn tới trang /order-lookup trong nhóm điều hướng chính', async () => {
    stubCartApi(buildCart([]));
    await renderWithProviders(root, <Header />);

    const navigation = container.querySelector('nav[aria-label="Điều hướng chính"]');
    const lookupLink = Array.from(navigation?.querySelectorAll('a') ?? []).find(
      (link) => link.textContent === 'Tra cứu đơn',
    );
    expect(lookupLink?.getAttribute('href')).toBe('/order-lookup');
  });
});
