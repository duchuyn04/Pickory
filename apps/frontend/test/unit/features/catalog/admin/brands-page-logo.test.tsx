// @vitest-environment jsdom
import { act } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { BrandsPage } from '../../../../../src/features/catalog/admin/brands-page';
import {
  findField,
  renderCatalogPage,
  settle,
  typeInto,
  type CatalogListHarness,
} from './catalog-list-test-kit';

let harness: CatalogListHarness;

const now = '2026-10-01T00:00:00Z';
const existingLogoUrl =
  'https://res.cloudinary.com/pickory-fake/image/upload/pickory/brands/apple-old';
const uploadedLogoUrl =
  'https://res.cloudinary.com/pickory-fake/image/upload/pickory/brands/apple-new';
const existingBrand = {
  id: 'brand-1',
  name: 'Apple',
  slug: 'apple',
  logoPublicId: 'pickory/brands/apple-old',
  logoUrl: existingLogoUrl,
  createdAt: now,
  updatedAt: now,
};
const signature = {
  cloudName: 'pickory-fake',
  apiKey: 'fake-api-key',
  uploadUrl: 'https://api.cloudinary.com/v1_1/pickory-fake/image/upload',
  timestamp: 1_800_000_000,
  folder: 'pickory/brands',
  allowedFormats: 'jpg,png,webp',
  maxFileBytes: 5 * 1024 * 1024,
  signature: 'fake-signature',
};

function openBrands(respond: (url: URL, init?: RequestInit) => unknown) {
  return renderCatalogPage({
    element: <BrandsPage />,
    routePath: '/admin/brands',
    url: '/admin/brands',
    respond,
  });
}

function buttonByText(text: string) {
  return [...harness.container.querySelectorAll<HTMLButtonElement>('button')].find(
    (button) => button.textContent?.trim() === text,
  )!;
}

async function openCreateDialog() {
  await act(async () => buttonByText('+ Thêm thương hiệu').click());
}

async function openEditDialog() {
  const editButton = harness.container.querySelector<HTMLButtonElement>(
    'button[aria-label="Sửa Apple"]',
  )!;
  await act(async () => editButton.click());
}

async function selectLogo(file: File) {
  const input = harness.container.querySelector<HTMLInputElement>('input[type="file"]')!;
  Object.defineProperty(input, 'files', { configurable: true, value: [file] });
  await act(async () => input.dispatchEvent(new Event('change', { bubbles: true })));
}

function wasSignatureRequested() {
  return vi
    .mocked(fetch)
    .mock.calls.some(([input]) => String(input) === '/api/admin/uploads/brand-logo/signature');
}

afterEach(async () => {
  await harness.cleanup();
});

describe('form thương hiệu tải logo bằng file', () => {
  it('từ chối định dạng sai trước khi xin chữ ký', async () => {
    harness = await openBrands((url) => {
      if (url.pathname === '/api/brands') return [];
      throw new Error(`Unexpected request: ${url}`);
    });
    await openCreateDialog();

    await selectLogo(new File(['image'], 'logo.gif', { type: 'image/gif' }));

    expect(harness.container.textContent).toContain(
      'Ảnh "logo.gif" phải có định dạng JPG, PNG hoặc WebP.',
    );
    expect(wasSignatureRequested()).toBe(false);
  });

  it('tải ảnh lên đúng thư mục logo và chỉ lưu publicId sau khi thành công', async () => {
    let brands: unknown[] = [];
    let savedPayload: Record<string, unknown> | undefined;
    harness = await openBrands((url, init) => {
      if (url.pathname === '/api/brands') return brands;
      if (url.pathname === '/api/admin/uploads/brand-logo/signature') return signature;
      if (url.hostname === 'api.cloudinary.com') {
        return Response.json({
          public_id: 'pickory/brands/apple-new',
          secure_url: uploadedLogoUrl,
        });
      }
      if (url.pathname === '/api/admin/brands' && init?.method === 'POST') {
        savedPayload = JSON.parse(String(init.body)) as Record<string, unknown>;
        const created = {
          ...existingBrand,
          id: 'brand-new',
          name: savedPayload.name,
          logoPublicId: savedPayload.logoPublicId,
          logoUrl: uploadedLogoUrl,
        };
        brands = [created];
        return created;
      }
      throw new Error(`Unexpected request: ${url}`);
    });
    await openCreateDialog();
    expect(harness.container.querySelector('#brand-logo')).toBeNull();
    await typeInto(findField(harness.container, 'Tên thương hiệu *')!, 'Apple');

    await selectLogo(new File(['image'], 'logo.png', { type: 'image/png' }));
    await settle();

    const preview = harness.container.querySelector<HTMLImageElement>('img[alt="Logo của Apple"]');
    expect(preview).not.toBeNull();
    expect(preview!.src).toContain('f_auto,q_auto,w_200/pickory/brands/apple-new');
    const cloudinaryCall = vi
      .mocked(fetch)
      .mock.calls.find(([input]) => String(input).startsWith('https://api.cloudinary.com'));
    const uploadBody = cloudinaryCall?.[1]?.body as FormData;
    expect(uploadBody.get('folder')).toBe('pickory/brands');

    await act(async () => buttonByText('Tạo thương hiệu').click());
    await settle();

    expect(savedPayload).toEqual({
      name: 'Apple',
      logoPublicId: 'pickory/brands/apple-new',
    });
    expect(harness.container.querySelector('[role="dialog"]')).toBeNull();
  });

  it('giữ logo cũ và hiện lỗi khi Cloudinary từ chối ảnh mới', async () => {
    let savedPayload: Record<string, unknown> | undefined;
    harness = await openBrands((url, init) => {
      if (url.pathname === '/api/brands') return [existingBrand];
      if (url.pathname === '/api/admin/uploads/brand-logo/signature') return signature;
      if (url.hostname === 'api.cloudinary.com') return new Response('{}', { status: 400 });
      if (url.pathname === '/api/admin/brands/brand-1' && init?.method === 'PATCH') {
        savedPayload = JSON.parse(String(init.body)) as Record<string, unknown>;
        return existingBrand;
      }
      throw new Error(`Unexpected request: ${url}`);
    });
    await openEditDialog();
    const oldPreview = harness.container.querySelector<HTMLImageElement>(
      'img[alt="Logo của Apple"]',
    );
    expect(oldPreview!.src).toContain('pickory/brands/apple-old');

    await selectLogo(new File(['image'], 'broken.png', { type: 'image/png' }));
    await settle();

    expect(harness.container.textContent).toContain(
      'Không tải được ảnh "broken.png" lên. Hãy thử lại.',
    );
    const currentPreview = harness.container.querySelector<HTMLImageElement>(
      'img[alt="Logo của Apple"]',
    );
    expect(currentPreview!.src).toBe(oldPreview!.src);

    await act(async () => buttonByText('Lưu thay đổi').click());
    await settle();
    expect(savedPayload).toEqual({ name: 'Apple', logoPublicId: 'pickory/brands/apple-old' });
  });

  it('sửa tên mà không chọn ảnh thì giữ nguyên logo và không xin chữ ký', async () => {
    let savedPayload: Record<string, unknown> | undefined;
    harness = await openBrands((url, init) => {
      if (url.pathname === '/api/brands') return [existingBrand];
      if (url.pathname === '/api/admin/brands/brand-1' && init?.method === 'PATCH') {
        savedPayload = JSON.parse(String(init.body)) as Record<string, unknown>;
        return existingBrand;
      }
      throw new Error(`Unexpected request: ${url}`);
    });
    await openEditDialog();
    await typeInto(findField(harness.container, 'Tên thương hiệu *')!, 'Apple Việt Nam');

    await act(async () => buttonByText('Lưu thay đổi').click());
    await settle();

    expect(savedPayload).toEqual({
      name: 'Apple Việt Nam',
      logoPublicId: 'pickory/brands/apple-old',
    });
    expect(wasSignatureRequested()).toBe(false);
  });

  it('khóa nút lưu và các nút đóng trong lúc đang tải logo', async () => {
    let resolveSignature: ((value: typeof signature) => void) | null = null;
    harness = await openBrands((url) => {
      if (url.pathname === '/api/brands') return [];
      if (url.pathname === '/api/admin/uploads/brand-logo/signature') {
        return new Promise<typeof signature>((resolve) => {
          resolveSignature = resolve;
        });
      }
      if (url.hostname === 'api.cloudinary.com') {
        return Response.json({
          public_id: 'pickory/brands/apple-new',
          secure_url: uploadedLogoUrl,
        });
      }
      throw new Error(`Unexpected request: ${url}`);
    });
    await openCreateDialog();

    await selectLogo(new File(['image'], 'logo.png', { type: 'image/png' }));

    expect(buttonByText('Tạo thương hiệu').disabled).toBe(true);
    expect(buttonByText('Hủy').disabled).toBe(true);
    expect(
      harness.container.querySelector<HTMLButtonElement>('button[aria-label="Đóng"]')!.disabled,
    ).toBe(true);
    expect(harness.container.querySelector('[role="status"]')!.textContent).toBe(
      'Đang tải logo lên...',
    );
    await act(async () => resolveSignature!(signature));
    await settle();
    expect(buttonByText('Tạo thương hiệu').disabled).toBe(false);
    expect(buttonByText('Hủy').disabled).toBe(false);
    expect(
      harness.container.querySelector<HTMLButtonElement>('button[aria-label="Đóng"]')!.disabled,
    ).toBe(false);
  });
});
