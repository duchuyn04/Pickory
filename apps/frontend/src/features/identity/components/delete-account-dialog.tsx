import { useQueryClient } from '@tanstack/react-query';
import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { apiRequest } from '../../../shared/api';
import { ErrorAlert } from '../../../shared/error-alert';
import '../customer/account.css';
import '../customer/customer-auth.css';

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
    <section className="card account-section account-danger">
      <h2>Khu vực nguy hiểm: Xóa tài khoản</h2>
      <p className="muted small">
        Khi xóa tài khoản, mọi thông tin cá nhân (tên, email, số điện thoại) sẽ được ẩn danh vĩnh
        viễn và không thể phục hồi.
      </p>

      {/* Bước xác nhận chỉ hiện sau lần bấm đầu, để khách không xóa nhầm bằng một cú bấm. */}
      {!open ? (
        <div>
          <button type="button" className="btn btn-danger" onClick={() => setOpen(true)}>
            Yêu cầu xóa tài khoản
          </button>
        </div>
      ) : (
        <div className="stack-sm">
          <h3 className="account-danger-title">Xác nhận xóa tài khoản vĩnh viễn</h3>
          <p className="small">
            Hành động này không thể hoàn tác. Để tiếp tục, vui lòng nhập mật khẩu tài khoản của bạn.
            Nếu tài khoản đăng nhập bằng Google (không có mật khẩu), vui lòng đăng nhập lại Google
            trong vòng 5 phút gần nhất rồi để trống mật khẩu và bấm Xác nhận.
          </p>

          {error && <ErrorAlert message={error} />}

          <form noValidate onSubmit={handleDelete} className="stack">
            <div className="field">
              <label htmlFor="del-pw">Mật khẩu xác nhận</label>
              <input
                id="del-pw"
                type="password"
                autoComplete="current-password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="Nhập mật khẩu của bạn"
              />
              <small>
                Tài khoản Google?{' '}
                <a href="/api/auth/google/start?returnTo=/me" className="auth-link">
                  Đăng nhập lại bằng Google để xác nhận
                </a>
              </small>
            </div>

            <div className="row-actions">
              {/* Nút xác nhận nền đỏ vì đây là hành động chính và không hoàn tác được. */}
              <button type="submit" className="btn btn-primary" disabled={loading}>
                {loading ? 'Đang xóa...' : 'Xác nhận xóa ngay'}
              </button>
              <button
                type="button"
                className="btn"
                onClick={() => {
                  setOpen(false);
                  setPassword('');
                  setError(null);
                }}
              >
                Hủy bỏ
              </button>
            </div>
          </form>
        </div>
      )}
    </section>
  );
}
