import { useState, type ReactNode } from 'react';
import { fieldErrorProps } from '../../../shared/field-error';
import './customer-auth.css';

interface AuthCardProps {
  // Màn kết quả bỏ tiêu đề thẻ, vì tiêu đề của khối kết quả đã nói rõ chủ đề.
  title?: string;
  subtitle?: string;
  children: ReactNode;
}

// Khung thẻ chung của các màn tài khoản khách: đăng nhập, đăng ký, quên và đặt lại mật khẩu, xác minh email.
export function AuthCard({ title, subtitle, children }: AuthCardProps) {
  return (
    <main className="auth-page">
      <div className="card auth-card">
        {title && (
          <div className="auth-head">
            <h1>{title}</h1>
            {subtitle && <p>{subtitle}</p>}
          </div>
        )}
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

// Đường kẻ có chữ, tách nút đăng nhập Google khỏi form email. Chữ khác nhau giữa đăng nhập và đăng ký.
export function AuthDivider({ label }: { label: string }) {
  return (
    <div className="auth-divider" aria-hidden="true">
      {label}
    </div>
  );
}

// Link tới luồng OAuth của backend. Dùng thẻ `a` vì trình duyệt phải rời ứng dụng để sang Google.
export function GoogleSignInLink({ label }: { label: string }) {
  return (
    <a href="/api/auth/google/start" className="btn btn-block">
      {/* Biểu tượng Google 18x18 xuất từ Figma, giữ đúng nét và màu của bản thiết kế. */}
      <svg
        width="18"
        height="18"
        viewBox="0 0 18 18"
        fill="none"
        aria-hidden="true"
        focusable="false"
      >
        <path
          fill="#4285F4"
          d="M16.92 9.1875C16.92 8.6025 16.8675 8.04 16.77 7.5H9V10.695H13.44C13.245 11.7225 12.66 12.5925 11.7825 13.1775V15.255H14.46C16.02 13.815 16.92 11.7 16.92 9.1875Z"
        />
        <path
          fill="#34A853"
          d="M9 17.25C11.2275 17.25 13.095 16.515 14.46 15.255L11.7825 13.1775C11.0475 13.6725 10.11 13.9725 9 13.9725C6.855 13.9725 5.0325 12.525 4.38 10.575H1.635V12.705C2.9925 15.3975 5.775 17.25 9 17.25Z"
        />
        <path
          fill="#FBBC05"
          d="M4.38 10.5675C4.215 10.0725 4.1175 9.5475 4.1175 9C4.1175 8.4525 4.215 7.9275 4.38 7.4325V5.295H1.635C1.0725 6.4125 0.75 7.665 0.75 9C0.75 10.335 1.0725 11.5875 1.635 12.705L3.7725 11.04L4.38 10.5675Z"
        />
        <path
          fill="#EA4335"
          d="M9 4.035C10.215 4.035 11.295 4.455 12.1575 5.265L14.52 2.9025C13.0875 1.5675 11.2275 0.75 9 0.75C5.775 0.75 2.9925 2.6025 1.635 5.295L4.38 7.425C5.0325 5.475 6.855 4.035 9 4.035Z"
        />
      </svg>
      {label}
    </a>
  );
}
