// URL VietQR chỉ chứa thông tin chuyển khoản, không chứa token truy cập đơn.
export function buildVietQrUrl(account: string, bank: string, amount: number, orderCode: string) {
  // Mã hóa từng giá trị để ký tự đặc biệt không đổi nghĩa của tham số tiếp theo.
  const parameters = new URLSearchParams({
    acc: account,
    bank,
    amount: String(amount),
    des: orderCode,
  });
  return `https://vietqr.app/img?${parameters.toString()}`;
}

export function formatPaymentCountdown(deadline: string, now: number) {
  // Làm tròn lên để không hiển thị hết hạn trước thời điểm máy chủ ấn định.
  const seconds = Math.max(0, Math.ceil((Date.parse(deadline) - now) / 1000));
  const minutes = Math.floor(seconds / 60);
  return `${String(minutes).padStart(2, '0')}:${String(seconds % 60).padStart(2, '0')}`;
}
