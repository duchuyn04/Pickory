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
import {
  getEmailError,
  getNameError,
  getPasswordError,
  getPhoneError,
} from '../../../shared/identity-validation';
import { useActor } from '../hooks/use-actor';
import { AuthCard, AuthDivider, GoogleSignInLink, PasswordInput } from './auth-ui';
import { ErrorAlert } from '../../../shared/error-alert';

// Trang đăng ký tài khoản khách bằng email và mật khẩu.
export function RegisterPage() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  // Giá trị của ô bẫy bot (honeypot): người thật không thấy ô này nên luôn rỗng.
  const [website, setWebsite] = useState('');
  // Lỗi theo từng ô hiện ngay dưới ô sai; `error` chỉ còn giữ lỗi trả về từ API.
  const [fieldErrors, setFieldErrors] = useState<
    FieldErrors<'email' | 'password' | 'name' | 'phone'>
  >({});
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { data: actor } = useActor();

  React.useEffect(() => {
    // Khách đã đăng nhập thì không cần đăng ký nữa, đưa về trang chủ.
    if (actor?.kind === 'customer') {
      navigate('/', { replace: true });
    }
  }, [actor, navigate]);

  async function handleSubmit(e: React.FormEvent) {
    // Chặn trình duyệt tải lại trang khi gửi form.
    e.preventDefault();
    setError(null);

    // Kiểm ở trình duyệt để báo lỗi ngay. Lỗi nhập liệu không tốn một trong 5 lượt đăng ký mỗi giờ của IP.
    const nextFieldErrors = {
      email: getEmailError(email),
      password: getPasswordError(password),
      name: getNameError(name),
      phone: getPhoneError(phone),
    };
    setFieldErrors(nextFieldErrors);
    if (hasFieldErrors(nextFieldErrors)) return;

    setLoading(true);

    try {
      await apiRequest('/api/auth/registrations', {
        method: 'POST',
        // Trường để trống không gửi (undefined). `website` luôn rỗng với người thật, nên server từ chối khi nó có giá trị.
        body: JSON.stringify({
          email,
          password,
          name: name || undefined,
          phone: phone || undefined,
          website: website || undefined,
        }),
      });
      // Server đăng nhập luôn sau khi đăng ký, nên nạp lại actor để header hiện trạng thái đã đăng nhập.
      await queryClient.invalidateQueries({ queryKey: ['actor'] });
      // replace để nút Quay lại không đưa người dùng về trang đăng ký.
      navigate('/', { replace: true });
    } catch (err: unknown) {
      // Hiện nguyên thông báo của API (email đã dùng, quá nhiều lần đăng ký...) vì đã là tiếng Việt.
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
    <AuthCard title="Đăng ký tài khoản" subtitle="Tạo tài khoản để quản lý đơn hàng và nhận ưu đãi">
      {error && <ErrorAlert message={error} />}

      <form noValidate onSubmit={handleSubmit} className="stack">
        <div className="field">
          <label htmlFor="reg-email">Email *</label>
          <input
            id="reg-email"
            type="email"
            required
            autoComplete="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="ban@example.com"
            {...fieldErrorProps('reg-email', fieldErrors.email)}
          />
          <FieldError inputId="reg-email" message={fieldErrors.email} />
        </div>

        <div className="field">
          <label htmlFor="reg-password">Mật khẩu * (tối thiểu 8 ký tự)</label>
          <PasswordInput
            id="reg-password"
            value={password}
            onChange={setPassword}
            autoComplete="new-password"
            placeholder="Nhập tối thiểu 8 ký tự"
            errorMessage={fieldErrors.password}
          />
          <FieldError inputId="reg-password" message={fieldErrors.password} />
        </div>

        <div className="field">
          <label htmlFor="reg-name">Họ và tên (tùy chọn)</label>
          <input
            id="reg-name"
            type="text"
            autoComplete="name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Nguyễn Văn A"
            {...fieldErrorProps('reg-name', fieldErrors.name)}
          />
          <FieldError inputId="reg-name" message={fieldErrors.name} />
        </div>

        <div className="field">
          <label htmlFor="reg-phone">Số điện thoại (tùy chọn)</label>
          <input
            id="reg-phone"
            type="tel"
            autoComplete="tel"
            value={phone}
            onChange={(e) => setPhone(e.target.value)}
            placeholder="0912 345 678"
            {...fieldErrorProps('reg-phone', fieldErrors.phone)}
          />
          <FieldError inputId="reg-phone" message={fieldErrors.phone} />
        </div>

        {/* Honeypot: ô ẩn để bẫy bot tự động điền form, ẩn hoàn toàn với người dùng và trình đọc */}
        <div className="honeypot" aria-hidden="true">
          <label htmlFor="reg-website">Website</label>
          <input
            id="reg-website"
            type="text"
            name="website"
            value={website}
            onChange={(e) => setWebsite(e.target.value)}
            tabIndex={-1}
            autoComplete="off"
          />
        </div>

        <button type="submit" className="btn btn-primary btn-block" disabled={loading}>
          {loading ? 'Đang đăng ký...' : 'Tạo tài khoản'}
        </button>

        <AuthDivider />
        <GoogleSignInLink label="Đăng ký bằng Google" />
      </form>

      <p className="auth-footer">
        Đã có tài khoản? <Link to="/login">Đăng nhập ngay</Link>
      </p>
    </AuthCard>
  );
}
