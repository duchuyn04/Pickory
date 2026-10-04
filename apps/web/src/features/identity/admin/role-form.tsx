import React, { useState } from 'react';
import { useDialogFocus } from '../../../shared/use-dialog-focus';
import { PermissionGrid, type PermissionItem } from './permission-grid';

export interface RoleData {
  id?: string;
  code: string;
  name: string;
  isSystem: boolean;
  permissions: string[];
}

interface RoleFormProps {
  role?: RoleData | null;
  allPermissions: PermissionItem[];
  onSave: (data: { code: string; name: string; permissions: string[] }) => Promise<void>;
  onClose: () => void;
}

export function RoleForm({ role, allPermissions, onSave, onClose }: RoleFormProps) {
  const dialogRef = useDialogFocus({ onClose });
  const isEditing = Boolean(role?.id);
  const [code, setCode] = useState(role?.code || '');
  const [name, setName] = useState(role?.name || '');
  const [selectedPermissions, setSelectedPermissions] = useState<string[]>(role?.permissions || []);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    const trimmedCode = code.trim();
    const trimmedName = name.trim();

    if (!isEditing) {
      if (!trimmedCode) {
        setError('Mã vai trò không được để trống.');
        return;
      }
      if (!/^[a-z0-9_-]+$/.test(trimmedCode)) {
        setError('Mã vai trò chỉ được chứa chữ thường, số, gạch dưới và gạch nối.');
        return;
      }
    }

    if (!trimmedName) {
      setError('Tên vai trò không được để trống.');
      return;
    }

    setLoading(true);

    try {
      await onSave({
        code: isEditing ? role?.code || trimmedCode : trimmedCode,
        name: trimmedName,
        permissions: selectedPermissions,
      });
      onClose();
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Lưu thông tin role thất bại.');
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
        aria-labelledby="role-form-title"
        tabIndex={-1}
        style={{
          width: '100%',
          maxWidth: 800,
          maxHeight: '90vh',
          backgroundColor: 'var(--color-surface)',
          borderRadius: 'var(--radius-card)',
          border: '1px solid var(--color-border)',
          display: 'flex',
          flexDirection: 'column',
          boxShadow: '0 8px 30px rgba(0, 0, 0, 0.15)',
          outline: 'none',
        }}
      >
        <div
          style={{
            padding: 'var(--space-4) var(--space-6)',
            borderBottom: '1px solid var(--color-border)',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
          }}
        >
          <h2
            id="role-form-title"
            style={{
              fontSize: 'var(--text-lg)',
              fontWeight: 700,
              margin: 0,
              color: 'var(--color-ink)',
            }}
          >
            {isEditing ? `Sửa Role: ${role?.name}` : 'Thêm Role Mới'}
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

        <div style={{ flex: 1, overflowY: 'auto', padding: 'var(--space-6)' }}>
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
            id="role-form"
            noValidate
            onSubmit={handleSubmit}
            style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-4)' }}
          >
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--space-4)' }}>
              <div>
                <label
                  htmlFor="role-code"
                  style={{
                    display: 'block',
                    fontSize: 'var(--text-sm)',
                    fontWeight: 600,
                    color: 'var(--color-ink)',
                    marginBottom: 'var(--space-1)',
                  }}
                >
                  Mã Role (code) *
                </label>
                <input
                  id="role-code"
                  type="text"
                  required
                  disabled={isEditing}
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                  placeholder="vi_du: warehouse_manager"
                  style={{
                    width: '100%',
                    boxSizing: 'border-box',
                    padding: 'var(--space-3)',
                    borderRadius: 'var(--radius-control)',
                    border: '1px solid var(--color-border)',
                    fontSize: 'var(--text-md)',
                    backgroundColor: isEditing
                      ? 'var(--color-surface-muted)'
                      : 'var(--color-surface)',
                  }}
                />
              </div>

              <div>
                <label
                  htmlFor="role-name"
                  style={{
                    display: 'block',
                    fontSize: 'var(--text-sm)',
                    fontWeight: 600,
                    color: 'var(--color-ink)',
                    marginBottom: 'var(--space-1)',
                  }}
                >
                  Tên hiển thị *
                </label>
                <input
                  id="role-name"
                  type="text"
                  required
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="Quản lý kho"
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
            </div>

            <div style={{ marginTop: 'var(--space-4)' }}>
              <label
                style={{
                  display: 'block',
                  fontSize: 'var(--text-sm)',
                  fontWeight: 600,
                  marginBottom: 'var(--space-2)',
                }}
              >
                Phân quyền theo nhóm chức năng:
              </label>
              <PermissionGrid
                permissions={allPermissions}
                selected={selectedPermissions}
                onChange={setSelectedPermissions}
              />
            </div>
          </form>
        </div>

        <div
          style={{
            padding: 'var(--space-4) var(--space-6)',
            borderTop: '1px solid var(--color-border)',
            display: 'flex',
            justifyContent: 'flex-end',
            gap: 'var(--space-3)',
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
            form="role-form"
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
            {loading ? 'Đang lưu...' : isEditing ? 'Lưu thay đổi' : 'Tạo Role'}
          </button>
        </div>
      </div>
    </div>
  );
}
