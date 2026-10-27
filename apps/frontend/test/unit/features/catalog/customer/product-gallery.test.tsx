// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ProductGallery } from '../../../../../src/features/catalog/customer/product-gallery';
import { imageBlack, imageShared } from './product-fixtures';

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

async function renderGallery() {
  await act(async () =>
    root.render(<ProductGallery images={[imageBlack, imageShared]} productName="Laptop 1" />),
  );
}

describe('ProductGallery', () => {
  it('tắt ảnh trước ở ảnh đầu và tắt ảnh sau ở ảnh cuối', async () => {
    await renderGallery();

    const previous = container.querySelector<HTMLButtonElement>('button[aria-label="Ảnh trước"]');
    const next = container.querySelector<HTMLButtonElement>('button[aria-label="Ảnh sau"]');
    expect(previous?.disabled).toBe(true);
    expect(next?.disabled).toBe(false);
  });

  it('bấm ảnh sau đổi ảnh chính rồi tắt ở ảnh cuối', async () => {
    await renderGallery();

    const next = container.querySelector<HTMLButtonElement>('button[aria-label="Ảnh sau"]');
    await act(async () => next?.click());

    expect(container.querySelector('.gallery-main img')?.getAttribute('src')).toContain(
      imageShared.url.split('/image/upload/')[1],
    );
    expect(next?.disabled).toBe(true);
    expect(
      container.querySelector<HTMLButtonElement>('button[aria-label="Ảnh trước"]')?.disabled,
    ).toBe(false);
  });
});
