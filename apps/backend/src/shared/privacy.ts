const hiddenValue = '[đã ẩn]';
const emailPattern = /[\p{L}\p{N}._%+-]+@[\p{L}\p{N}.-]+\.[\p{L}]{2,}/gu;
const phonePattern = /(?<![\p{L}\p{N}])(?:\+84|0084|84|0)[ .-]?[35789](?:[ .-]?\d){8}(?!\d)/gu;
const labelledAccountPattern =
  /((?:số\s+tài\s+khoản|tài\s+khoản|stk|account(?:\s+number)?)\s*[:#-]?\s*)((?:\d[ -]?){7,18}\d)(?!\d)/giu;
const longNumberPattern = /(?<![\p{L}\p{N}])\d(?:[ -]?\d){11,18}(?![\p{L}\p{N}])/gu;

export function redactPII(text: string): string {
  // Email không phụ thuộc độ dài nên phải ẩn trước các mẫu số có thể nằm trong địa chỉ.
  const withoutEmails = text.replace(emailPattern, hiddenValue);
  // Chỉ nhận đầu số di động Việt Nam và đủ độ dài để không ẩn giá hoặc mã sản phẩm ngắn.
  const withoutPhones = withoutEmails.replace(phonePattern, hiddenValue);
  // Nhãn tài khoản cho phép nhận số ngắn mà không nhầm số tiền không có nhãn ngân hàng.
  const withoutLabelledAccounts = withoutPhones.replace(labelledAccountPattern, `$1${hiddenValue}`);
  // Chuỗi từ 12 đến 19 chữ số bao phủ số thẻ và tài khoản dài, kể cả có dấu cách phân nhóm.
  return withoutLabelledAccounts.replace(longNumberPattern, hiddenValue);
}

export function redactPIIValue(value: unknown): unknown {
  if (typeof value === 'string') return redactPII(value);
  if (typeof value === 'number') {
    const clean = redactPII(String(value));
    // Số nhạy cảm trở thành chuỗi placeholder trước khi serialize, không làm hỏng cú pháp JSON.
    return clean === String(value) ? value : clean;
  }
  if (Array.isArray(value)) return value.map(redactPIIValue);
  if (value && typeof value === 'object') {
    return Object.fromEntries(
      Object.entries(value).map(([key, entry]) => [redactPII(key), redactPIIValue(entry)]),
    );
  }
  return value ?? null;
}
