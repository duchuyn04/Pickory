// Giá trị thay cho hai biến của mẫu soạn sẵn. SD-UI3 đã nối giá trị thật từ dữ liệu khách của phiên đang mở.
export interface CannedResponseVariables {
  tenKhach: string | null;
  maDon: string | null;
}

// Biến thiếu giá trị giữ nguyên dạng `{ten_khach}` hoặc `{ma_don}`, giống luật của backend.
// Thay một lượt bằng hàm replacer, để giá trị chứa chuỗi giống biến không bị thay tiếp.
export function renderCannedResponse(body: string, values: CannedResponseVariables): string {
  return body.replace(/\{ten_khach\}|\{ma_don\}/g, (placeholder) => {
    if (placeholder === '{ten_khach}') return values.tenKhach ?? placeholder;
    return values.maDon ?? placeholder;
  });
}
