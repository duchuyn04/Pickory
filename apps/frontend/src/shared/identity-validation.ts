// Tự kiểm tra thay cho tooltip mặc định của trình duyệt để lỗi luôn bằng tiếng Việt.
export function getEmailError(email: string): string | null {
  if (!email.trim()) return 'Email không được để trống.';
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) return 'Email không đúng định dạng.';
  return null;
}

export function getPasswordError(password: string, minLength = 8): string | null {
  if (!password) return 'Mật khẩu không được để trống.';
  if (password.length < minLength) return `Mật khẩu phải chứa tối thiểu ${minLength} ký tự.`;
  if (password.length > 128) return 'Mật khẩu không được vượt quá 128 ký tự.';
  return null;
}

const NAME_MAX_LENGTH = 100;
// Số di động Việt Nam: số 0 hoặc `+84`, theo sau 9 chữ số, chữ số đầu thuộc 3, 5, 7, 8, 9.
const PHONE_PATTERN = /^(?:0|\+84)[35789][0-9]{8}$/;
// Ký tự điều khiển U+0000 đến U+001F và U+007F phá bố cục thư và log.
function hasControlCharacter(text: string): boolean {
  return [...text].some((character) => {
    const codePoint = character.codePointAt(0) ?? 0;
    return codePoint <= 0x1f || codePoint === 0x7f;
  });
}

const graphemeSegmenter = new Intl.Segmenter('vi', { granularity: 'grapheme' });

// Tên không bắt buộc, vì giá trị trống xóa tên đã lưu. Luật khớp `ProfileFieldRules.cs` ở backend.
export function getNameError(name: string): string | null {
  if (!name.trim()) return null;
  // Kiểm ký tự điều khiển trước độ dài, cùng thứ tự với backend.
  if (hasControlCharacter(name)) return 'Họ và tên chứa ký tự không hợp lệ.';
  // Đếm theo ký tự người đọc thấy. Chữ có dấu viết dạng tổ hợp vẫn tính là một ký tự.
  const length = [...graphemeSegmenter.segment(name.trim())].length;
  if (length > NAME_MAX_LENGTH) return 'Họ và tên tối đa 100 ký tự.';
  return null;
}

// Số điện thoại không bắt buộc. Người dùng hay chia nhóm số bằng khoảng trắng, dấu chấm và dấu gạch ngang.
export function getPhoneError(phone: string): string | null {
  if (!phone.trim()) return null;
  const compact = phone.replace(/[\s.-]/g, '');
  if (!PHONE_PATTERN.test(compact)) return 'Số điện thoại không đúng định dạng.';
  return null;
}

// Ô nhập lại mật khẩu chỉ kiểm được ở giao diện, vì server chỉ nhận mật khẩu mới.
export function getConfirmPasswordError(password: string, confirmPassword: string): string | null {
  if (!confirmPassword) return 'Vui lòng xác nhận lại mật khẩu.';
  if (password !== confirmPassword) return 'Mật khẩu xác nhận không khớp.';
  return null;
}
