import type { ProductImageDetail } from './product-form';

// Chọn ảnh đại diện của sản phẩm: ảnh có `position` nhỏ nhất. Không dựa vào thứ tự mảng, vì API có thể đổi thứ tự trả về.
export function firstProductImage(images: ProductImageDetail[]): ProductImageDetail | null {
  let first: ProductImageDetail | null = null;
  for (const image of images) {
    // So bằng `<` chứ không `<=`. Hai ảnh cùng `position` thì giữ ảnh đứng trước trong mảng.
    if (first === null || image.position < first.position) first = image;
  }
  return first;
}

// Chữ thay thế của ảnh. Ảnh chưa có mô tả thì dùng tên sản phẩm, để trình đọc màn hình vẫn đọc được ảnh này là gì.
export function resolveImageAlt(image: ProductImageDetail, productName: string): string {
  const alt = image.alt.trim();
  return alt === '' ? productName : alt;
}
