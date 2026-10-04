import { useQueryClient } from '@tanstack/react-query';
import React, { useEffect, useState } from 'react';
import { Link, Navigate } from 'react-router-dom';
import { apiRequest } from '../../shared/api';
import { ChangePasswordForm } from './change-password-form';
import { DeleteAccountDialog } from './delete-account-dialog';
import { useActor } from './use-actor';

export function AccountPage() {
  const { data: actor, isLoading } = useActor();
  const queryClient = useQueryClient();

  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  const [saving, setSaving] = useState(false);
  const [success, setSuccess] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (actor) {
      setName(actor.name || '');
      setPhone(actor.phone || '');
    }
  }, [actor]);

  if (isLoading) {
    return (
      <div
        style={{
          minHeight: '100vh',
          backgroundColor: 'var(--color-surface-muted)',
          fontFamily: 'var(--font-sans)',
        }}
      >
        <div style={{ textAlign: 'center', padding: 'var(--space-7)' }}>Đang nạp thông tin...</div>
      </div>
    );
  }

  if (!actor || actor.kind !== 'customer') {
    return <Navigate to="/login" replace />;
  }

  async function handleUpdateProfile(e: React.FormEvent) {
    e.preventDefault();
    setSaving(true);
    setError(null);
    setSuccess(false);

    try {
      await apiRequest('/api/me', {
        method: 'PATCH',
        body: JSON.stringify({
          name: name.trim() === '' ? null : name.trim(),
          phone: phone.trim() === '' ? null : phone.trim(),
        }),
      });
      await queryClient.invalidateQueries({ queryKey: ['actor'] });
      setSuccess(true);
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Cập nhật thông tin thất bại.');
      }
    } finally {
      setSaving(false);
    }
  }

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
          maxWidth: 680,
          margin: '0 auto',
          padding: 'var(--space-6) var(--space-4)',
          display: 'flex',
          flexDirection: 'column',
          gap: 'var(--space-6)',
        }}
      >
        <h1
          style={{
            fontSize: 'var(--text-xl)',
            color: 'var(--color-ink)',
            margin: 0,
            fontWeight: 700,
          }}
        >
          Tài khoản của tôi
        </h1>

        {/* 1. Thông tin cá nhân */}
        <div
          style={{
            backgroundColor: 'var(--color-surface)',
            borderRadius: 'var(--radius-card)',
            border: '1px solid var(--color-border)',
            padding: 'var(--space-6)',
          }}
        >
          <h2
            style={{
              fontSize: 'var(--text-lg)',
              fontWeight: 700,
              margin: '0 0 var(--space-4) 0',
              color: 'var(--color-ink)',
            }}
          >
            Thông tin cá nhân
          </h2>

          {success && (
            <div
              style={{
                backgroundColor: '#e6f4ea',
                border: '1px solid var(--color-success)',
                color: 'var(--color-success)',
                padding: 'var(--space-3)',
                borderRadius: 'var(--radius-control)',
                fontSize: 'var(--text-sm)',
                marginBottom: 'var(--space-4)',
              }}
            >
              Cập nhật thông tin thành công!
            </div>
          )}

          {error && (
            <div
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
            onSubmit={handleUpdateProfile}
            style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-4)' }}
          >
            <div>
              <label
                htmlFor="acc-name"
                style={{
                  display: 'block',
                  fontSize: 'var(--text-sm)',
                  fontWeight: 600,
                  color: 'var(--color-ink)',
                  marginBottom: 'var(--space-1)',
                }}
              >
                Họ và tên
              </label>
              <input
                id="acc-name"
                type="text"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Nhập họ và tên"
                style={{
                  width: '100%',
                  boxSizing: 'border-box',
                  padding: 'var(--space-3)',
                  borderRadius: 'var(--radius-control)',
                  border: '1px solid var(--color-border)',
                  fontSize: 'var(--text-md)',
                  outline: 'none',
                }}
              />
            </div>

            <div>
              <label
                htmlFor="acc-phone"
                style={{
                  display: 'block',
                  fontSize: 'var(--text-sm)',
                  fontWeight: 600,
                  color: 'var(--color-ink)',
                  marginBottom: 'var(--space-1)',
                }}
              >
                Số điện thoại
              </label>
              <input
                id="acc-phone"
                type="tel"
                value={phone}
                onChange={(e) => setPhone(e.target.value)}
                placeholder="0912 345 678"
                style={{
                  width: '100%',
                  boxSizing: 'border-box',
                  padding: 'var(--space-3)',
                  borderRadius: 'var(--radius-control)',
                  border: '1px solid var(--color-border)',
                  fontSize: 'var(--text-md)',
                  outline: 'none',
                }}
              />
            </div>

            <button
              type="submit"
              disabled={saving}
              style={{
                alignSelf: 'flex-start',
                backgroundColor: 'var(--color-brand)',
                color: '#ffffff',
                border: 'none',
                padding: 'var(--space-3) var(--space-5)',
                borderRadius: 'var(--radius-control)',
                fontSize: 'var(--text-md)',
                fontWeight: 600,
                cursor: saving ? 'not-allowed' : 'pointer',
                opacity: saving ? 0.7 : 1,
              }}
            >
              {saving ? 'Đang lưu...' : 'Lưu thay đổi'}
            </button>
          </form>
        </div>

        {/* 2. Đổi mật khẩu */}
        <ChangePasswordForm />

        {/* 3. Cài đặt quyền riêng tư */}
        <div
          style={{
            backgroundColor: 'var(--color-surface)',
            borderRadius: 'var(--radius-card)',
            border: '1px solid var(--color-border)',
            padding: 'var(--space-6)',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
          }}
        >
          <div>
            <h2
              style={{
                fontSize: 'var(--text-lg)',
                fontWeight: 700,
                margin: '0 0 var(--space-1) 0',
                color: 'var(--color-ink)',
              }}
            >
              Quyền riêng tư & Cookie
            </h2>
            <p style={{ fontSize: 'var(--text-sm)', color: 'var(--color-ink-muted)', margin: 0 }}>
              Tùy chỉnh sự cho phép tính năng cá nhân hóa và thông báo tiếp thị.
            </p>
          </div>
          <Link
            to="/privacy"
            style={{
              padding: 'var(--space-2) var(--space-4)',
              border: '1px solid var(--color-border)',
              borderRadius: 'var(--radius-control)',
              color: 'var(--color-ink)',
              textDecoration: 'none',
              fontSize: 'var(--text-sm)',
              fontWeight: 600,
            }}
          >
            Quản lý
          </Link>
        </div>

        {/* 4. Xóa tài khoản */}
        <DeleteAccountDialog />
      </div>
    </div>
  );
}
