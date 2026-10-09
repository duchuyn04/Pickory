import { useState, type ReactNode } from 'react';
import { fieldErrorProps } from '../../../shared/field-error';
import './customer-auth.css';

interface AuthCardProps {
  title: string;
  subtitle?: string;
  children: ReactNode;
}

// Khung thẻ chung của các màn tài khoản khách: đăng nhập, đăng ký, quên và đặt lại mật khẩu, xác minh email.
export function AuthCard({ title, subtitle, children }: AuthCardProps) {
  return (
    <main className="auth-page">
      <div className="card auth-card">
        <div className="auth-head">
          <h1>{title}</h1>
          {subtitle && <p>{subtitle}</p>}
        </div>
        {children}
      </div>
    </main>
  );
}

interface PasswordInputProps {
  id: string;
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  autoComplete?: string;
  // Lỗi của ô; trang tự hiện câu lỗi bằng `FieldError` ngay dưới ô.
  errorMessage?: string | null;
  // Truyền từ cha khi hai ô mật khẩu cần cùng hiện hoặc cùng ẩn.
  visible?: boolean;
  onVisibleChange?: (visible: boolean) => void;
}

// Ô mật khẩu có nút Hiện, để người dùng kiểm tra đã gõ đúng, nhất là trên điện thoại.
export function PasswordInput({
  id,
  value,
  onChange,
  placeholder,
  autoComplete,
  errorMessage,
  visible,
  onVisibleChange,
}: PasswordInputProps) {
  const [ownVisible, setOwnVisible] = useState(false);
  // Cha giữ trạng thái khi có truyền `visible`; nếu không, ô tự giữ trạng thái của mình.
  const isVisible = visible ?? ownVisible;
  function toggleVisible() {
    if (onVisibleChange) onVisibleChange(!isVisible);
    else setOwnVisible(!isVisible);
  }

  return (
    <div className="password-input">
      <input
        id={id}
        type={isVisible ? 'text' : 'password'}
        required
        value={value}
        autoComplete={autoComplete}
        onChange={(event) => onChange(event.target.value)}
        placeholder={placeholder}
        {...fieldErrorProps(id, errorMessage)}
      />
      <button type="button" className="btn btn-ghost btn-sm" onClick={toggleVisible}>
        {isVisible ? 'Ẩn' : 'Hiện'}
      </button>
    </div>
  );
}

// Đường kẻ có chữ HOẶC, tách form email khỏi nút đăng nhập Google.
export function AuthDivider() {
  return (
    <div className="auth-divider" aria-hidden="true">
      HOẶC
    </div>
  );
}

// Link tới luồng OAuth của backend. Dùng thẻ `a` vì trình duyệt phải rời ứng dụng để sang Google.
export function GoogleSignInLink({ label }: { label: string }) {
  return (
    <a href="/api/auth/google/start" className="btn btn-block">
      <svg width="18" height="18" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
        <path
          fill="#4285F4"
          d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92c-.26 1.37-1.04 2.53-2.21 3.31v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.09z"
        />
        <path
          fill="#34A853"
          d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z"
        />
        <path
          fill="#FBBC05"
          d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.06H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.94l2.85-2.22.81-.63z"
        />
        <path
          fill="#EA4335"
          d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.06l3.66 2.84c.87-2.6 3.3-4.52 6.16-4.52z"
        />
      </svg>
      {label}
    </a>
  );
}
