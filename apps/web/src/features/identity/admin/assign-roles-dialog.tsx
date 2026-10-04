import React, { useState } from 'react';
import { apiRequest } from '../../../shared/api';
import { useDialogFocus } from '../../../shared/use-dialog-focus';
import type { RoleData } from './role-form';

export interface StaffRow {
  id: string;
  email: string;
  name: string | null;
  phone: string | null;
  emailVerified: boolean;
  kind: string;
  lockedAt: string | null;
  createdAt: string;
  roles: string[];
}

interface AssignRolesDialogProps {
  staff: StaffRow | null;
  availableRoles: RoleData[];
  onSuccess: () => void;
  onClose: () => void;
}

export function AssignRolesDialog({
  staff,
  availableRoles,
  onSuccess,
  onClose,
}: AssignRolesDialogProps) {
  const dialogRef = useDialogFocus({
    isOpen: Boolean(staff),
    onClose,
  });

  const [selectedRoles, setSelectedRoles] = useState<string[]>(() => staff?.roles || []);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!staff) return null;
  const staffAssignableRoles = availableRoles.filter((r) => r.code !== 'customer');

  function toggleRole(code: string) {
    if (selectedRoles.includes(code)) {
      setSelectedRoles(selectedRoles.filter((r) => r !== code));
    } else {
      setSelectedRoles([...selectedRoles, code]);
    }
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);

    try {
      await apiRequest(`/api/admin/staff/${staff!.id}/roles`, {
        method: 'PUT',
        body: JSON.stringify({
          roleCodes: selectedRoles,
        }),
      });
      onSuccess();
      onClose();
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Gán vai trò thất bại.');
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
        aria-labelledby="assign-roles-dialog-title"
        tabIndex={-1}
        style={{
          width: '100%',
          maxWidth: 480,
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
            id="assign-roles-dialog-title"
            style={{
              fontSize: 'var(--text-lg)',
              fontWeight: 700,
              margin: 0,
              color: 'var(--color-ink)',
            }}
          >
            Gán vai trò cho: {staff.name || staff.email}
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
          Tài khoản: <strong>{staff.email}</strong>. Chọn các vai trò áp dụng cho nhân viên này.
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
          <div
            style={{
              display: 'flex',
              flexDirection: 'column',
              gap: 'var(--space-2)',
              maxHeight: 220,
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
              {loading ? 'Đang lưu...' : 'Lưu vai trò'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
