// Thêm tham số biến đổi của Cloudinary để trình duyệt nhận ảnh vừa kích thước, đúng định dạng nhẹ nhất.
// Spec dùng các chiều rộng 200, 400, 800 và 1200 px.
export function withCloudinaryTransform(url: string, width: number): string {
  const uploadSegment = '/image/upload/';
  // URL không phải ảnh Cloudinary thì không có chỗ chèn tham số, nên giữ nguyên.
  const segmentIndex = url.indexOf(uploadSegment);
  if (segmentIndex === -1) return url;
  const insertAt = segmentIndex + uploadSegment.length;
  return `${url.slice(0, insertAt)}f_auto,q_auto,w_${width}/${url.slice(insertAt)}`;
}
