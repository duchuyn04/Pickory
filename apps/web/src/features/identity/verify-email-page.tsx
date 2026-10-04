import React, { useEffect, useRef, useState } from 'react';
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { apiRequest } from '../../shared/api';

export function VerifyEmailPage() {
  const [searchParams] = useSearchParams();
  const location = useLocation();
  const navigate = useNavigate();

  // Lưu token vào state nội bộ để giữ lại giá trị phục vụ gọi API sau khi đã xóa token khỏi URL hiển thị.
  const [token] = useState(() => searchParams.get('token') || '');

  const [loading, setLoading] = useState(false);
  const [success, setSuccess] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Dùng ref lưu token đã gửi API nhằm ngăn chặn việc gọi 2 lần trong môi trường React StrictMode khiến token bị lỗi đã dùng.
  const calledTokenRef = useRef<string | null>(null);

  // Xóa tham số token khỏi thanh URL trình duyệt để tránh rò rỉ token dùng một lần qua lịch sử hoặc header Referer.
  useEffect(() => {
    if (searchParams.has('token')) {
      navigate(location.pathname, { replace: true });
    }
  }, [searchParams, location.pathname, navigate]);

  // Tự động kích hoạt quy trình xác thực khi trang được tải kèm token hợp lệ từ đường dẫn trong email.
  useEffect(() => {
    if (!token || calledTokenRef.current === token) return;
    calledTokenRef.current = token;

    setLoading(true);
    setError(null);

    apiRequest('/api/auth/verify-email', {
      method: 'POST',
      body: JSON.stringify({ token }),
    })
      .then(() => {
        setSuccess(true);
      })
      .catch((err: unknown) => {
        if (err instanceof Error) {
          setError(err.message);
        } else {
          setError('Xác thực email thất bại hoặc liên kết đã hết hạn.');
        }
      })
      .finally(() => {
        setLoading(false);
      });
  }, [token]);

  return (
    <div
      style={{
        minHeight: '100vh',
        backgroundColor: 'var(--color-surface-muted)',
        fontFamily: 'var(--font-sans)',
      }}
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          padding: 'var(--space-7) var(--space-4)',
        }}
      >
        <div
          style={{
            width: '100%',
            maxWidth: 420,
            backgroundColor: 'var(--color-surface)',
            borderRadius: 'var(--radius-card)',
            border: '1px solid var(--color-border)',
            padding: 'var(--space-6)',
            boxShadow: '0 4px 20px rgba(0,0,0,0.06)',
            textAlign: 'center',
          }}
        >
          <h1
            style={{
              fontSize: 'var(--text-xl)',
              color: 'var(--color-ink)',
              margin: '0 0 var(--space-2) 0',
              fontWeight: 700,
            }}
          >
            Xác thực email
          </h1>

          {!token && (
            <div
              role="alert"
              style={{
                backgroundColor: '#fee4e2',
                border: '1px solid var(--color-danger)',
                color: 'var(--color-danger)',
                padding: 'var(--space-4)',
                borderRadius: 'var(--radius-control)',
                fontSize: 'var(--text-sm)',
                margin: 'var(--space-4) 0',
              }}
            >
              Liên kết xác thực thiếu mã token. Vui lòng kiểm tra lại liên kết trong email của bạn.
            </div>
          )}

          {loading && (
            <div
              role="status"
              style={{
                color: 'var(--color-ink-muted)',
                fontSize: 'var(--text-sm)',
                margin: 'var(--space-5) 0',
              }}
            >
              Đang xác thực địa chỉ email của bạn...
            </div>
          )}

          {!loading && success && (
            <div>
              <div
                role="status"
                style={{
                  backgroundColor: '#e6f4ea',
                  border: '1px solid var(--color-success)',
                  color: 'var(--color-success)',
                  padding: 'var(--space-4)',
                  borderRadius: 'var(--radius-control)',
                  fontSize: 'var(--text-sm)',
                  margin: 'var(--space-5) 0',
                }}
              >
                Chúc mừng! Địa chỉ email của bạn đã được xác thực thành công.
              </div>
              <Link
                to="/"
                style={{
                  display: 'inline-block',
                  backgroundColor: 'var(--color-brand)',
                  color: '#ffffff',
                  textDecoration: 'none',
                  fontWeight: 600,
                  fontSize: 'var(--text-sm)',
                  padding: 'var(--space-3) var(--space-5)',
                  borderRadius: 'var(--radius-control)',
                }}
              >
                Về trang chủ mua sắm
              </Link>
            </div>
          )}

          {!loading && error && (
            <div>
              <div
                role="alert"
                style={{
                  backgroundColor: '#fee4e2',
                  border: '1px solid var(--color-danger)',
                  color: 'var(--color-danger)',
                  padding: 'var(--space-4)',
                  borderRadius: 'var(--radius-control)',
                  fontSize: 'var(--text-sm)',
                  margin: 'var(--space-5) 0',
                }}
              >
                {error}
              </div>
              <Link
                to="/"
                style={{
                  color: 'var(--color-brand)',
                  textDecoration: 'none',
                  fontWeight: 600,
                  fontSize: 'var(--text-sm)',
                }}
              >
                ← Quay lại trang chủ
              </Link>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
