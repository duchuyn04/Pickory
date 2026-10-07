// Luật tải ảnh phải khớp `ImageUploadRules` của backend. Backend vẫn kiểm lại khi gắn ảnh.
export const MAX_IMAGE_BYTES = 5 * 1024 * 1024;
export const MAX_IMAGES_PER_PRODUCT = 10;
const ALLOWED_IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp'];

// Dữ liệu `POST /api/admin/uploads/signature` trả về. Không có secret.
export interface UploadSignature {
  cloudName: string;
  apiKey: string;
  uploadUrl: string;
  timestamp: number;
  folder: string;
  allowedFormats: string;
  maxFileBytes: number;
  signature: string;
}

export interface UploadedImage {
  publicId: string;
  url: string;
}

// Chặn file sai trước khi tải, để người dùng không chờ tải một file chắc chắn bị từ chối.
export function validateImageFile(file: File, maxBytes = MAX_IMAGE_BYTES): string | null {
  if (!ALLOWED_IMAGE_TYPES.includes(file.type)) {
    return `Ảnh "${file.name}" phải có định dạng JPG, PNG hoặc WebP.`;
  }
  if (file.size > maxBytes) {
    return `Ảnh "${file.name}" vượt quá 5 MB.`;
  }
  return null;
}

// Tải thẳng lên Cloudinary. Ảnh không đi qua server API của Pickory.
export async function uploadToCloudinary(
  file: File,
  signature: UploadSignature,
  fetchImplementation: typeof fetch = fetch,
): Promise<UploadedImage> {
  // Gửi đúng các giá trị đã ký. Đổi một giá trị thì Cloudinary báo sai chữ ký và từ chối.
  const body = new FormData();
  body.append('file', file);
  body.append('api_key', signature.apiKey);
  body.append('timestamp', String(signature.timestamp));
  body.append('signature', signature.signature);
  body.append('folder', signature.folder);
  body.append('allowed_formats', signature.allowedFormats);

  const response = await fetchImplementation(signature.uploadUrl, { method: 'POST', body });
  // Lỗi của Cloudinary là tiếng Anh, nên giao diện báo câu tiếng Việt có tên file.
  if (!response.ok) {
    throw new Error(`Không tải được ảnh "${file.name}" lên. Hãy thử lại.`);
  }
  const result = (await response.json()) as { public_id: string; secure_url: string };
  return { publicId: result.public_id, url: result.secure_url };
}

interface UploadDependencies {
  requestSignature: () => Promise<UploadSignature>;
  uploadFile: (file: File, signature: UploadSignature) => Promise<UploadedImage>;
}

export interface UploadBatchResult {
  uploaded: UploadedImage[];
  errors: string[];
}

// Tải nhiều file theo thứ tự chọn. Một file lỗi không chặn các file còn lại.
export async function uploadImageFiles(
  files: File[],
  dependencies: UploadDependencies,
  remainingSlots = MAX_IMAGES_PER_PRODUCT,
): Promise<UploadBatchResult> {
  const errors: string[] = [];
  const validFiles: File[] = [];
  for (const file of files) {
    const error = validateImageFile(file);
    if (error) errors.push(error);
    else validFiles.push(file);
  }

  // Chỉ tải số ảnh còn chỗ, để sản phẩm không vượt giới hạn 10 ảnh mà backend sẽ từ chối.
  const acceptedFiles = validFiles.slice(0, Math.max(remainingSlots, 0));
  const skippedCount = validFiles.length - acceptedFiles.length;
  if (skippedCount > 0) {
    errors.push(
      `Mỗi sản phẩm có tối đa ${MAX_IMAGES_PER_PRODUCT} ảnh. Đã bỏ qua ${skippedCount} ảnh.`,
    );
  }
  // Không xin chữ ký khi không có file hợp lệ, để file sai không gây lời gọi mạng nào.
  if (acceptedFiles.length === 0) return { uploaded: [], errors };

  const signature = await dependencies.requestSignature();
  const uploaded: UploadedImage[] = [];
  for (const file of acceptedFiles) {
    try {
      uploaded.push(await dependencies.uploadFile(file, signature));
    } catch (error) {
      errors.push(
        error instanceof Error ? error.message : `Không tải được ảnh "${file.name}" lên.`,
      );
    }
  }
  return { uploaded, errors };
}
