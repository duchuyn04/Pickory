type RgbChannels = [number, number, number];

// Chỉ nhận mã hex 3 hoặc 6 ký tự. Token màu chữ và nền không dùng độ trong suốt.
function parseHexColor(hexColor: string): RgbChannels {
  const digits = hexColor.trim().replace(/^#/, '');
  if (!/^([0-9a-f]{3}|[0-9a-f]{6})$/i.test(digits)) {
    throw new Error(`Mã màu không hợp lệ: ${hexColor}`);
  }
  // Mã 3 ký tự nhân đôi từng ký tự, ví dụ `#fff` thành `#ffffff`.
  const fullDigits =
    digits.length === 3
      ? digits
          .split('')
          .map((digit) => digit + digit)
          .join('')
      : digits;
  return [0, 2, 4].map((start) => parseInt(fullDigits.slice(start, start + 2), 16)) as RgbChannels;
}

// Độ chói tương đối theo WCAG 2.x. Kênh sRGB phải đổi về giá trị tuyến tính trước khi cộng trọng số.
function relativeLuminance(hexColor: string): number {
  const [red, green, blue] = parseHexColor(hexColor).map((channel) => {
    const normalized = channel / 255;
    return normalized <= 0.03928 ? normalized / 12.92 : ((normalized + 0.055) / 1.055) ** 2.4;
  }) as RgbChannels;
  return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
}

// Tỉ lệ không phụ thuộc thứ tự tham số. Màu sáng hơn luôn nằm ở tử số.
export function contrastRatio(firstColor: string, secondColor: string): number {
  const firstLuminance = relativeLuminance(firstColor);
  const secondLuminance = relativeLuminance(secondColor);
  const lighter = Math.max(firstLuminance, secondLuminance);
  const darker = Math.min(firstLuminance, secondLuminance);
  return (lighter + 0.05) / (darker + 0.05);
}
