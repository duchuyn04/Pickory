import React, { useState } from 'react';
import { Link } from 'react-router-dom';
import { apiRequest } from '../../../shared/api';
import {
  FieldError,
  fieldErrorProps,
  hasFieldErrors,
  type FieldErrors,
} from '../../../shared/field-error';
import { getEmailError } from '../../../shared/identity-validation';
import { AuthCard } from './auth-ui';

// Trang nhập email để nhận link đặt lại mật khẩu.
export function ForgotPasswordPage() {
  const [email, setEmail] = useState('');
  // submitted chuyển trang sang màn "đã gửi", thay thế form.
  const [submitted, setSubmitted] = useState(false);
  // Lỗi theo từng ô hiện ngay dưới ô sai; `error` chỉ còn giữ lỗi trả về từ API.
  const [fieldErrors, setFieldErrors] = useState<FieldErrors<'email'>>({});
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    // Chặn trình duyệt tải lại trang khi gửi form.
    e.preventDefault();
    setError(null);
    // Kiểm tra định dạng ngay ở trình duyệt để báo lỗi tiếng Việt tức thì, không tốn một lượt gọi API (và một lượt trong giới hạn tần suất).
    const nextFieldErrors = { email: getEmailError(email) };
    setFieldErrors(nextFieldErrors);
    if (hasFieldErrors(nextFieldErrors)) return;
    setLoading(true);

    try {
      await apiRequest('/api/auth/forgot-password', {
        method: 'POST',
        body: JSON.stringify({ email }),
      });
      // Luôn hiện cùng một màn xác nhận, vì server cũng trả cùng một phản hồi; nếu khác nhau thì dò được email nào đã đăng ký.
      setSubmitted(true);
    } catch (err: unknown) {
      // Chỉ lỗi thật (mạng, dữ liệu sai định dạng) mới tới đây; email không tồn tại không gây lỗi.
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Đã có lỗi xảy ra. Vui lòng thử lại.');
      }
    } finally {
      setLoading(false);
    }
  }

  return (
    <AuthCard title="Quên mật khẩu" subtitle="Nhập email để nhận liên kết đặt lại mật khẩu">
      {submitted ? (
        <div className="auth-result">
          {/* Cùng một câu cho email có và không có tài khoản, để không lộ email nào đã đăng ký. */}
          <p role="status" className="alert alert-ok">
            Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được gửi đến hộp thư của
            bạn.
          </p>
          <Link to="/login" className="auth-link">
            ← Quay lại trang đăng nhập
          </Link>
        </div>
      ) : (
        <>
          {error && (
            <p role="alert" className="alert">
              {error}
            </p>
          )}

          <form noValidate onSubmit={handleSubmit} className="stack">
            <div className="field">
              <label htmlFor="forgot-email">Email tài khoản</label>
              <input
                id="forgot-email"
                type="email"
                required
                autoComplete="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="ban@example.com"
                {...fieldErrorProps('forgot-email', fieldErrors.email)}
              />
              <FieldError inputId="forgot-email" message={fieldErrors.email} />
            </div>

            <button type="submit" className="btn btn-primary btn-block" disabled={loading}>
              {loading ? 'Đang gửi yêu cầu...' : 'Gửi liên kết đặt lại'}
            </button>
          </form>

          <p className="auth-footer">
            <Link to="/login">← Quay lại đăng nhập</Link>
          </p>
        </>
      )}
    </AuthCard>
  );
}
