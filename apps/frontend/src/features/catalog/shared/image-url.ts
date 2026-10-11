export interface CloudinaryImageOptions {
  // Cắt viền một màu quanh ảnh, để sản phẩm lấp đầy khung.
  // Ảnh gốc của vài hãng có máy nhỏ giữa khung xám, quanh khung lại có viền trắng.
  // Cắt hai lần: lần đầu bỏ viền trắng, lần sau bỏ khung xám. Ảnh không có viền trơn thì giữ nguyên.
  trim?: boolean;
}

const TRIM_TRANSFORM = 'e_trim/e_trim/';

// Thêm tham số biến đổi của Cloudinary để trình duyệt nhận ảnh vừa kích thước, đúng định dạng nhẹ nhất.
// Spec dùng các chiều rộng 200, 400, 800 và 1200 px.
export function withCloudinaryTransform(
  url: string,
  width: number,
  options: CloudinaryImageOptions = {},
): string {
  const uploadSegment = '/image/upload/';
  // URL không phải ảnh Cloudinary thì không có chỗ chèn tham số, nên giữ nguyên.
  const segmentIndex = url.indexOf(uploadSegment);
  if (segmentIndex === -1) return url;
  const insertAt = segmentIndex + uploadSegment.length;
  // Cắt viền trước khi đổi cỡ, để chiều rộng yêu cầu áp lên phần ảnh đã cắt.
  const trim = options.trim ? TRIM_TRANSFORM : '';
  return `${url.slice(0, insertAt)}${trim}f_auto,q_auto,w_${width}/${url.slice(insertAt)}`;
}

export const CATALOG_IMAGE_WIDTHS = [200, 400, 800, 1200] as const;

// Danh sách `srcset` để trình duyệt tự chọn kích thước vừa với ô ảnh và mật độ điểm ảnh của màn hình.
export function buildCloudinarySrcSet(url: string, options: CloudinaryImageOptions = {}): string {
  return CATALOG_IMAGE_WIDTHS.map(
    (width) => `${withCloudinaryTransform(url, width, options)} ${width}w`,
  ).join(', ');
}
