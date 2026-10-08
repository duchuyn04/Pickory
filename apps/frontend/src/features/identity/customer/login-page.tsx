import { useQueryClient } from '@tanstack/react-query';
import React, { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { apiRequest } from '../../../shared/api';
import {
  FieldError,
  fieldErrorProps,
  hasFieldErrors,
  type FieldErrors,
} from '../../../shared/field-error';
import { getEmailError, getPasswordError } from '../../../shared/identity-validation';
import { useActor } from '../hooks/use-actor';
import { AuthCard, AuthDivider, GoogleSignInLink, PasswordInput } from './auth-ui';
import { ErrorAlert } from '../../../shared/error-alert';

// Trang đăng nhập của khách (nhân viên đăng nhập ở /admin/login).
export function LoginPage() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  // Lỗi theo từng ô hiện ngay dưới ô sai; `error` chỉ còn giữ lỗi trả về từ API.
  const [fieldErrors, setFieldErrors] = useState<FieldErrors<'email' | 'password'>>({});
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { data: actor } = useActor();

  React.useEffect(() => {
    // Khách đã đăng nhập mà vào lại trang này thì đưa về trang chủ, không hiện lại form đăng nhập vô nghĩa.
    // Chỉ xét khách: nhân viên (kind staff) không bị đẩy đi vì họ dùng trang đăng nhập riêng.
    if (actor?.kind === 'customer') {
      navigate('/', { replace: true });
    }
  }, [actor, navigate]);

  async function handleSubmit(e: React.FormEvent) {
    // Chặn trình duyệt tải lại trang khi gửi form.
    e.preventDefault();
    setError(null);
    // Mật khẩu chỉ cần không rỗng (độ dài tối thiểu 1): quy tắc 8 ký tự chỉ áp khi đặt mật khẩu mới, không áp khi đăng nhập tài khoản cũ.
    const nextFieldErrors = {
      email: getEmailError(email),
      password: getPasswordError(password, 1),
    };
    setFieldErrors(nextFieldErrors);
    if (hasFieldErrors(nextFieldErrors)) return;
    setLoading(true);

    try {
      await apiRequest('/api/auth/login', {
        method: 'POST',
        body: JSON.stringify({ email, password }),
      });
      // Nạp lại actor để header hiện trạng thái đã đăng nhập ngay, thay vì chờ cache hết hạn 30 giây.
      await queryClient.invalidateQueries({ queryKey: ['actor'] });
      // replace để nút Quay lại không đưa người dùng về trang đăng nhập.
      navigate('/', { replace: true });
    } catch (err: unknown) {
      // Hiện nguyên thông báo của API: sai email và sai mật khẩu cùng một câu, còn bị khóa thì có câu riêng.
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
    <AuthCard title="Đăng nhập" subtitle="Đăng nhập tài khoản khách hàng Pickory">
      {/* `role="alert"` để trình đọc màn hình đọc ngay lỗi đăng nhập vừa xuất hiện. */}
      {error && <ErrorAlert message={error} />}

      <form noValidate onSubmit={handleSubmit} className="stack">
        <div className="field">
          <label htmlFor="login-email">Email</label>
          <input
            id="login-email"
            type="email"
            required
            autoComplete="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="ban@example.com"
            {...fieldErrorProps('login-email', fieldErrors.email)}
          />
          <FieldError inputId="login-email" message={fieldErrors.email} />
        </div>

        <div className="field">
          <div className="auth-label-row">
            <label htmlFor="login-password">Mật khẩu</label>
            <Link to="/forgot-password" className="auth-link">
              Quên mật khẩu?
            </Link>
          </div>
          <PasswordInput
            id="login-password"
            value={password}
            onChange={setPassword}
            autoComplete="current-password"
            placeholder="Nhập mật khẩu"
            errorMessage={fieldErrors.password}
          />
          <FieldError inputId="login-password" message={fieldErrors.password} />
        </div>

        <button type="submit" className="btn btn-primary btn-block" disabled={loading}>
          {loading ? 'Đang đăng nhập...' : 'Đăng nhập'}
        </button>

        <AuthDivider />
        <GoogleSignInLink label="Đăng nhập bằng Google" />
      </form>

      <p className="auth-footer">
        Chưa có tài khoản? <Link to="/register">Đăng ký ngay</Link>
      </p>
    </AuthCard>
  );
}
