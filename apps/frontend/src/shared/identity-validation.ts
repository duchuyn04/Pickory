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

// Ô nhập lại mật khẩu chỉ kiểm được ở giao diện, vì server chỉ nhận mật khẩu mới.
export function getConfirmPasswordError(password: string, confirmPassword: string): string | null {
  if (!confirmPassword) return 'Vui lòng xác nhận lại mật khẩu.';
  if (password !== confirmPassword) return 'Mật khẩu xác nhận không khớp.';
  return null;
}
