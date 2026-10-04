import { useQueryClient } from '@tanstack/react-query';
import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { apiRequest } from '../../../shared/api';

// Khối xóa tài khoản trong trang tài khoản, có bước xác nhận bằng mật khẩu trước khi xóa.
export function DeleteAccountDialog() {
  // open điều khiển việc hiện form xác nhận; bấm "Xóa tài khoản" chỉ mở form chứ chưa xóa ngay.
  const [open, setOpen] = useState(false);
  const [password, setPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const queryClient = useQueryClient();
  const navigate = useNavigate();

  async function handleDelete(e: React.FormEvent) {
    // Chặn trình duyệt tải lại trang khi gửi form.
    e.preventDefault();
    setError(null);
    setLoading(true);

    try {
      await apiRequest('/api/me', {
        method: 'DELETE',
        // Mật khẩu rỗng không gửi, vì tài khoản chỉ dùng Google không có mật khẩu và xác nhận bằng cách đăng nhập lại Google.
        body: JSON.stringify({ password: password || undefined }),
      });
      // Server đã xóa phiên, nên nạp lại actor để giao diện về trạng thái khách.
      await queryClient.invalidateQueries({ queryKey: ['actor'] });
      setOpen(false);
      // replace để nút Quay lại không đưa người dùng về trang tài khoản đã xóa.
      navigate('/', { replace: true });
    } catch (err: unknown) {
      // Hiện nguyên thông báo của API (ví dụ sai mật khẩu, cần đăng nhập lại Google) vì đã là tiếng Việt.
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Xóa tài khoản thất bại. Vui lòng thử lại.');
      }
    } finally {
      setLoading(false);
    }
  }

  return (
    <div
      style={{
        backgroundColor: 'var(--color-surface)',
        borderRadius: 'var(--radius-card)',
        border: '1px solid var(--color-danger)',
        padding: 'var(--space-6)',
      }}
    >
      <h2
        style={{
          fontSize: 'var(--text-lg)',
          fontWeight: 700,
          margin: '0 0 var(--space-2) 0',
          color: 'var(--color-danger)',
        }}
      >
        Khu vực nguy hiểm: Xóa tài khoản
      </h2>
      <p
        style={{
          fontSize: 'var(--text-sm)',
          color: 'var(--color-ink-muted)',
          margin: '0 0 var(--space-4) 0',
        }}
      >
        Khi xóa tài khoản, mọi thông tin cá nhân (tên, email, số điện thoại) sẽ được ẩn danh vĩnh
        viễn và không thể phục hồi.
      </p>

      {!open ? (
        <button
          type="button"
          onClick={() => setOpen(true)}
          style={{
            backgroundColor: 'transparent',
            border: '1px solid var(--color-danger)',
            color: 'var(--color-danger)',
            padding: 'var(--space-2) var(--space-4)',
            borderRadius: 'var(--radius-control)',
            fontSize: 'var(--text-sm)',
            fontWeight: 600,
            cursor: 'pointer',
          }}
        >
          Yêu cầu xóa tài khoản
        </button>
      ) : (
        <div
          style={{
            backgroundColor: '#fffbfa',
            border: '1px solid #fecdca',
            borderRadius: 'var(--radius-control)',
            padding: 'var(--space-4)',
          }}
        >
          <div
            style={{
              fontWeight: 600,
              color: 'var(--color-danger)',
              marginBottom: 'var(--space-2)',
            }}
          >
            Xác nhận xóa tài khoản vĩnh viễn
          </div>
          <p
            style={{
              fontSize: 'var(--text-sm)',
              color: 'var(--color-ink)',
              margin: '0 0 var(--space-3) 0',
            }}
          >
            Hành động này không thể hoàn tác. Để tiếp tục, vui lòng nhập mật khẩu tài khoản của bạn.
            Nếu tài khoản đăng nhập bằng Google (không có mật khẩu), vui lòng đăng nhập lại Google
            trong vòng 5 phút gần nhất rồi để trống mật khẩu và bấm Xác nhận.
          </p>

          {error && (
            <div
              style={{
                backgroundColor: '#fee4e2',
                border: '1px solid var(--color-danger)',
                color: 'var(--color-danger)',
                padding: 'var(--space-2) var(--space-3)',
                borderRadius: 'var(--radius-control)',
                fontSize: 'var(--text-xs)',
                marginBottom: 'var(--space-3)',
              }}
            >
              {error}
            </div>
          )}

          <form
            noValidate
            onSubmit={handleDelete}
            style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-3)' }}
          >
            <div>
              <label
                htmlFor="del-pw"
                style={{
                  display: 'block',
                  fontSize: 'var(--text-xs)',
                  fontWeight: 600,
                  color: 'var(--color-ink)',
                  marginBottom: 'var(--space-1)',
                }}
              >
                Mật khẩu xác nhận
              </label>
              <input
                id="del-pw"
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="Nhập mật khẩu của bạn"
                style={{
                  width: '100%',
                  boxSizing: 'border-box',
                  padding: 'var(--space-2) var(--space-3)',
                  borderRadius: 'var(--radius-control)',
                  border: '1px solid var(--color-border)',
                  fontSize: 'var(--text-sm)',
                  outline: 'none',
                }}
              />
              <div style={{ fontSize: 'var(--text-xs)', color: 'var(--color-ink-muted)' }}>
                Tài khoản Google?{' '}
                <a
                  href="/api/auth/google/start?returnTo=/me"
                  style={{ color: 'var(--color-brand)', textDecoration: 'underline' }}
                >
                  Đăng nhập lại bằng Google để xác nhận
                </a>
              </div>
            </div>

            <div style={{ display: 'flex', gap: 'var(--space-2)' }}>
              <button
                type="submit"
                disabled={loading}
                style={{
                  backgroundColor: 'var(--color-danger)',
                  color: '#ffffff',
                  border: 'none',
                  padding: 'var(--space-2) var(--space-4)',
                  borderRadius: 'var(--radius-control)',
                  fontSize: 'var(--text-sm)',
                  fontWeight: 600,
                  cursor: loading ? 'not-allowed' : 'pointer',
                  opacity: loading ? 0.7 : 1,
                }}
              >
                {loading ? 'Đang xóa...' : 'Xác nhận xóa ngay'}
              </button>
              <button
                type="button"
                onClick={() => {
                  setOpen(false);
                  setPassword('');
                  setError(null);
                }}
                style={{
                  backgroundColor: 'transparent',
                  border: '1px solid var(--color-border)',
                  color: 'var(--color-ink)',
                  padding: 'var(--space-2) var(--space-4)',
                  borderRadius: 'var(--radius-control)',
                  fontSize: 'var(--text-sm)',
                  cursor: 'pointer',
                }}
              >
                Hủy bỏ
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
}
