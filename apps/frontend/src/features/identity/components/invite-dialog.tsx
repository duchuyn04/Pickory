import React, { useState } from 'react';
import { apiRequest } from '../../../shared/api';
import { getEmailError } from '../../../shared/identity-validation';
import { useDialogFocus } from '../../../shared/use-dialog-focus';
import type { RoleData } from './role-form';

interface InviteDialogProps {
  availableRoles: RoleData[];
  // Cha nạp lại danh sách nhân viên sau khi mời xong.
  onSuccess: () => void;
  onClose: () => void;
}

// Hộp thoại mời nhân viên mới bằng email, tên và role.
export function InviteDialog({ availableRoles, onSuccess, onClose }: InviteDialogProps) {
  // Hook đưa focus vào hộp thoại khi mở, đóng bằng phím Esc, và trả focus về nút đã mở.
  const dialogRef = useDialogFocus({ onClose });
  const [email, setEmail] = useState('');
  const [name, setName] = useState('');
  const [selectedRoles, setSelectedRoles] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Ẩn role customer vì tài khoản khách và nhân viên tách riêng, server cũng từ chối gán.
  const staffAssignableRoles = availableRoles.filter((r) => r.code !== 'customer');

  // Bấm một role thì chọn, bấm lần nữa thì bỏ chọn.
  function toggleRole(code: string) {
    if (selectedRoles.includes(code)) {
      setSelectedRoles(selectedRoles.filter((r) => r !== code));
    } else {
      setSelectedRoles([...selectedRoles, code]);
    }
  }

  async function handleSubmit(e: React.FormEvent) {
    // Chặn trình duyệt tải lại trang khi gửi form.
    e.preventDefault();
    setError(null);

    // Kiểm tra ở trình duyệt để báo lỗi tiếng Việt ngay; server vẫn kiểm tra lại mọi thứ.
    const emailErr = getEmailError(email);
    if (emailErr) {
      setError(emailErr);
      return;
    }

    if (!name.trim()) {
      setError('Họ và tên không được để trống.');
      return;
    }

    // Nhân viên không có role nào thì không làm được gì, nên bắt chọn ít nhất một.
    if (selectedRoles.length === 0) {
      setError('Vui lòng chọn ít nhất một vai trò cho nhân viên.');
      return;
    }

    setLoading(true);

    try {
      await apiRequest('/api/admin/staff/invitations', {
        method: 'POST',
        // trim để khoảng trắng thừa khi dán email không làm email sai.
        body: JSON.stringify({
          email: email.trim(),
          name: name.trim(),
          roleCodes: selectedRoles,
        }),
      });
      // onSuccess để trang cha nạp lại danh sách, rồi mới đóng.
      onSuccess();
      onClose();
    } catch (err: unknown) {
      // Hiện nguyên thông báo của API (email đã dùng, không đủ quyền để mời vào role này...) vì đã là tiếng Việt.
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Gửi lời mời thất bại.');
      }
    } finally {
      setLoading(false);
    }
  }

  return (
    <div
      style={{
        position: 'fixed',
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: 'rgba(0, 0, 0, 0.4)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 'var(--space-4)',
        zIndex: 10000,
        fontFamily: 'var(--font-sans)',
      }}
    >
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="invite-dialog-title"
        tabIndex={-1}
        style={{
          width: '100%',
          maxWidth: 500,
          backgroundColor: 'var(--color-surface)',
          borderRadius: 'var(--radius-card)',
          border: '1px solid var(--color-border)',
          boxShadow: '0 8px 30px rgba(0, 0, 0, 0.15)',
          padding: 'var(--space-6)',
          outline: 'none',
        }}
      >
        <div
          style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            marginBottom: 'var(--space-4)',
          }}
        >
          <h2
            id="invite-dialog-title"
            style={{
              fontSize: 'var(--text-lg)',
              fontWeight: 700,
              margin: 0,
              color: 'var(--color-ink)',
            }}
          >
            Mời nhân viên mới
          </h2>
          <button
            type="button"
            aria-label="Đóng"
            onClick={onClose}
            style={{
              background: 'none',
              border: 'none',
              fontSize: 'var(--text-lg)',
              cursor: 'pointer',
            }}
          >
            ✕
          </button>
        </div>

        <p
          style={{
            fontSize: 'var(--text-sm)',
            color: 'var(--color-ink-muted)',
            margin: '0 0 var(--space-4) 0',
          }}
        >
          Hệ thống sẽ gửi email chứa liên kết thiết lập mật khẩu (có hiệu lực 72 giờ) tới nhân viên.
        </p>

        {error && (
          <div
            role="alert"
            style={{
              backgroundColor: '#fee4e2',
              border: '1px solid var(--color-danger)',
              color: 'var(--color-danger)',
              padding: 'var(--space-3)',
              borderRadius: 'var(--radius-control)',
              fontSize: 'var(--text-sm)',
              marginBottom: 'var(--space-4)',
            }}
          >
            {error}
          </div>
        )}

        <form
          noValidate
          onSubmit={handleSubmit}
          style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-4)' }}
        >
          <div>
            <label
              htmlFor="inv-email"
              style={{
                display: 'block',
                fontSize: 'var(--text-sm)',
                fontWeight: 600,
                color: 'var(--color-ink)',
                marginBottom: 'var(--space-1)',
              }}
            >
              Email nhân viên *
            </label>
            <input
              id="inv-email"
              type="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="nhanvien@pickory.local"
              style={{
                width: '100%',
                boxSizing: 'border-box',
                padding: 'var(--space-3)',
                borderRadius: 'var(--radius-control)',
                border: '1px solid var(--color-border)',
                fontSize: 'var(--text-md)',
              }}
            />
          </div>

          <div>
            <label
              htmlFor="inv-name"
              style={{
                display: 'block',
                fontSize: 'var(--text-sm)',
                fontWeight: 600,
                color: 'var(--color-ink)',
                marginBottom: 'var(--space-1)',
              }}
            >
              Họ và tên *
            </label>
            <input
              id="inv-name"
              type="text"
              required
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Nguyễn Văn A"
              style={{
                width: '100%',
                boxSizing: 'border-box',
                padding: 'var(--space-3)',
                borderRadius: 'var(--radius-control)',
                border: '1px solid var(--color-border)',
                fontSize: 'var(--text-md)',
              }}
            />
          </div>

          <div>
            <label
              style={{
                display: 'block',
                fontSize: 'var(--text-sm)',
                fontWeight: 600,
                color: 'var(--color-ink)',
                marginBottom: 'var(--space-2)',
              }}
            >
              Vai trò (Role) *
            </label>
            <div
              style={{
                display: 'flex',
                flexDirection: 'column',
                gap: 'var(--space-2)',
                maxHeight: 180,
                overflowY: 'auto',
                border: '1px solid var(--color-border)',
                padding: 'var(--space-3)',
                borderRadius: 'var(--radius-control)',
              }}
            >
              {staffAssignableRoles.map((role) => (
                <label
                  key={role.code}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: 'var(--space-2)',
                    cursor: 'pointer',
                    fontSize: 'var(--text-sm)',
                  }}
                >
                  <input
                    type="checkbox"
                    checked={selectedRoles.includes(role.code)}
                    onChange={() => toggleRole(role.code)}
                    style={{ accentColor: 'var(--color-brand)' }}
                  />
                  <span>
                    <strong>{role.name}</strong> ({role.code})
                  </span>
                </label>
              ))}
            </div>
          </div>

          <div
            style={{
              display: 'flex',
              justifyContent: 'flex-end',
              gap: 'var(--space-3)',
              marginTop: 'var(--space-2)',
            }}
          >
            <button
              type="button"
              onClick={onClose}
              style={{
                padding: 'var(--space-2) var(--space-4)',
                border: '1px solid var(--color-border)',
                backgroundColor: 'transparent',
                borderRadius: 'var(--radius-control)',
                fontSize: 'var(--text-sm)',
                cursor: 'pointer',
              }}
            >
              Hủy
            </button>
            <button
              type="submit"
              disabled={loading}
              style={{
                padding: 'var(--space-2) var(--space-5)',
                backgroundColor: 'var(--color-brand)',
                color: '#ffffff',
                border: 'none',
                borderRadius: 'var(--radius-control)',
                fontSize: 'var(--text-sm)',
                fontWeight: 600,
                cursor: loading ? 'not-allowed' : 'pointer',
                opacity: loading ? 0.7 : 1,
              }}
            >
              {loading ? 'Đang gửi lời mời...' : 'Gửi lời mời'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
