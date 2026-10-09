import { describe, expect, it, vi } from 'vitest';
import {
  MAX_IMAGE_BYTES,
  uploadImageFiles,
  uploadToCloudinary,
  validateImageFile,
  type UploadSignature,
} from '../../../../../src/features/catalog/admin/image-upload';

const signature: UploadSignature = {
  cloudName: 'pickory-fake',
  apiKey: 'fake-api-key',
  uploadUrl: 'https://api.cloudinary.com/v1_1/pickory-fake/image/upload',
  timestamp: 1_800_000_000,
  folder: 'pickory/products',
  allowedFormats: 'jpg,png,webp',
  maxFileBytes: MAX_IMAGE_BYTES,
  signature: 'chu-ky-gia',
};

function makeFile(name: string, type: string, bytes: number): File {
  return new File([new Uint8Array(bytes)], name, { type });
}

describe('validateImageFile', () => {
  it('từ chối GIF bằng thông báo tiếng Việt', () => {
    expect(validateImageFile(makeFile('dong.gif', 'image/gif', 10))).toBe(
      'Ảnh "dong.gif" phải có định dạng JPG, PNG hoặc WebP.',
    );
  });

  it('từ chối ảnh lớn hơn 5 MB', () => {
    expect(validateImageFile(makeFile('lon.jpg', 'image/jpeg', MAX_IMAGE_BYTES + 1))).toBe(
      'Ảnh "lon.jpg" vượt quá 5 MB.',
    );
  });

  it.each([
    ['vua.jpg', 'image/jpeg'],
    ['vua.png', 'image/png'],
    ['vua.webp', 'image/webp'],
  ])('nhận %s đúng 5 MB', (name, type) => {
    expect(validateImageFile(makeFile(name, type, MAX_IMAGE_BYTES))).toBeNull();
  });
});

describe('uploadToCloudinary', () => {
  it('gửi file cùng đúng các trường đã ký và trả `publicId`, URL', async () => {
    const fetchImplementation = vi.fn(async () =>
      Response.json({
        public_id: 'pickory/products/mat-truoc',
        secure_url:
          'https://res.cloudinary.com/pickory-fake/image/upload/v1/pickory/products/mat-truoc.jpg',
      }),
    );
    const file = makeFile('mat-truoc.jpg', 'image/jpeg', 100);

    const uploaded = await uploadToCloudinary(file, signature, fetchImplementation);

    expect(uploaded).toEqual({
      publicId: 'pickory/products/mat-truoc',
      url: 'https://res.cloudinary.com/pickory-fake/image/upload/v1/pickory/products/mat-truoc.jpg',
    });
    const [url, init] = fetchImplementation.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(signature.uploadUrl);
    expect(init.method).toBe('POST');
    const body = init.body as FormData;
    expect(body.get('file')).toBe(file);
    expect(body.get('api_key')).toBe('fake-api-key');
    expect(body.get('timestamp')).toBe('1800000000');
    expect(body.get('signature')).toBe('chu-ky-gia');
    expect(body.get('folder')).toBe('pickory/products');
    expect(body.get('allowed_formats')).toBe('jpg,png,webp');
  });

  it('Cloudinary từ chối thì báo lỗi tiếng Việt có tên file', async () => {
    const fetchImplementation = vi.fn(async () => new Response('{}', { status: 400 }));

    await expect(
      uploadToCloudinary(makeFile('a.jpg', 'image/jpeg', 10), signature, fetchImplementation),
    ).rejects.toThrow('Không tải được ảnh "a.jpg" lên. Hãy thử lại.');
  });
});

describe('uploadImageFiles', () => {
  it('file sai bị chặn trước khi xin chữ ký hoặc gọi mạng', async () => {
    const requestSignature = vi.fn(async () => signature);
    const uploadFile = vi.fn();

    const result = await uploadImageFiles(
      [
        makeFile('dong.gif', 'image/gif', 10),
        makeFile('lon.png', 'image/png', MAX_IMAGE_BYTES + 1),
      ],
      { requestSignature, uploadFile },
    );

    expect(requestSignature).not.toHaveBeenCalled();
    expect(uploadFile).not.toHaveBeenCalled();
    expect(result.uploaded).toEqual([]);
    expect(result.errors).toHaveLength(2);
  });

  it('tải file hợp lệ, bỏ qua file sai và giữ thứ tự chọn file', async () => {
    const requestSignature = vi.fn(async () => signature);
    const uploadFile = vi.fn(async (file: File) => ({
      publicId: `pickory/products/${file.name}`,
      url: `https://res.cloudinary.com/pickory-fake/image/upload/pickory/products/${file.name}`,
    }));

    const result = await uploadImageFiles(
      [
        makeFile('mot.jpg', 'image/jpeg', 10),
        makeFile('dong.gif', 'image/gif', 10),
        makeFile('hai.webp', 'image/webp', 10),
      ],
      { requestSignature, uploadFile },
    );

    expect(requestSignature).toHaveBeenCalledTimes(1);
    expect(result.uploaded.map((image) => image.publicId)).toEqual([
      'pickory/products/mot.jpg',
      'pickory/products/hai.webp',
    ]);
    expect(result.errors).toEqual(['Ảnh "dong.gif" phải có định dạng JPG, PNG hoặc WebP.']);
  });

  it('chỉ tải đủ số ảnh còn trống trong giới hạn 10 ảnh', async () => {
    const requestSignature = vi.fn(async () => signature);
    const uploadFile = vi.fn(async (file: File) => ({ publicId: file.name, url: '' }));

    const result = await uploadImageFiles(
      [makeFile('a.jpg', 'image/jpeg', 10), makeFile('b.jpg', 'image/jpeg', 10)],
      { requestSignature, uploadFile },
      1,
    );

    expect(result.uploaded.map((image) => image.publicId)).toEqual(['a.jpg']);
    expect(result.errors).toEqual(['Mỗi sản phẩm có tối đa 10 ảnh. Đã bỏ qua 1 ảnh.']);
  });
});
