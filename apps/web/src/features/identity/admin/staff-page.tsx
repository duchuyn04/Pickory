import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { apiRequest } from '../../../shared/api';
import { useCan } from '../use-actor';
import { AssignRolesDialog, type StaffRow } from './assign-roles-dialog';
import { InviteDialog } from './invite-dialog';
import type { RoleData } from './role-form';

export function StaffPage() {
  const canReadStaff = useCan('staff:read');
  const canManageStaff = useCan('staff:manage');
  const queryClient = useQueryClient();

  const [inviteOpen, setInviteOpen] = useState(false);
  const [selectedStaff, setSelectedStaff] = useState<StaffRow | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  // Kiểm tra cờ lỗi của truy vấn để cảnh báo rõ ràng khi nạp danh sách nhân viên thất bại thay vì âm thầm hiển thị bảng rỗng.
  const {
    data: staffList = [],
    isLoading: staffLoading,
    isError: staffIsError,
    error: staffError,
  } = useQuery<StaffRow[]>({
    queryKey: ['admin-staff'],
    queryFn: () => apiRequest<StaffRow[]>('/api/admin/staff'),
    enabled: canReadStaff,
  });

  // Truy vấn danh sách vai trò để nạp vào dialog gán quyền và hiển thị thông báo riêng nếu xảy ra lỗi.
  const {
    data: roles = [],
    isError: rolesIsError,
    error: rolesError,
  } = useQuery<RoleData[]>({
    queryKey: ['admin-roles'],
    queryFn: () => apiRequest<RoleData[]>('/api/admin/roles'),
    enabled: canManageStaff,
  });

  if (!canReadStaff) {
    return (
      <div
        style={{
          padding: 'var(--space-6)',
          backgroundColor: 'var(--color-surface)',
          borderRadius: 'var(--radius-card)',
        }}
      >
        <h2 style={{ color: 'var(--color-danger)', margin: 0 }}>403 - Không có quyền truy cập</h2>
        <p style={{ color: 'var(--color-ink-muted)' }}>
          Bạn cần quyền <code>staff:read</code> để xem danh sách nhân viên.
        </p>
      </div>
    );
  }

  async function handleToggleLock(staff: StaffRow) {
    const isLocking = !staff.lockedAt;
    const confirmMsg = isLocking
      ? `Bạn có chắc chắn muốn khóa tài khoản nhân viên "${staff.name || staff.email}"?`
      : `Bạn có chắc chắn muốn mở khóa tài khoản nhân viên "${staff.name || staff.email}"?`;

    if (!window.confirm(confirmMsg)) return;

    setActionError(null);
    try {
      const endpoint = isLocking
        ? `/api/admin/staff/${staff.id}/lock`
        : `/api/admin/staff/${staff.id}/unlock`;
      await apiRequest(endpoint, { method: 'POST' });
      // Làm mới danh sách nhân viên trong cache để cập nhật ngay trạng thái khóa/mở khóa lên giao diện.
      await queryClient.invalidateQueries({ queryKey: ['admin-staff'] });
    } catch (err: unknown) {
      if (err instanceof Error) {
        setActionError(err.message);
      } else {
        setActionError('Thao tác khóa/mở khóa thất bại.');
      }
    }
  }

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--space-6)',
        fontFamily: 'var(--font-sans)',
      }}
    >
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <div>
          <h1
            style={{
              fontSize: 'var(--text-xl)',
              fontWeight: 700,
              margin: '0 0 var(--space-1) 0',
              color: 'var(--color-ink)',
            }}
          >
            Quản lý Nhân viên
          </h1>
          <p style={{ fontSize: 'var(--text-sm)', color: 'var(--color-ink-muted)', margin: 0 }}>
            Danh sách nhân viên, phân quyền vai trò và quản lý trạng thái tài khoản back-office.
          </p>
        </div>
        {canManageStaff && (
          <button
            type="button"
            onClick={() => setInviteOpen(true)}
            style={{
              backgroundColor: 'var(--color-brand)',
              color: '#ffffff',
              border: 'none',
              padding: 'var(--space-2) var(--space-4)',
              borderRadius: 'var(--radius-control)',
              fontSize: 'var(--text-sm)',
              fontWeight: 600,
              cursor: 'pointer',
            }}
          >
            + Mời nhân viên
          </button>
        )}
      </div>

      {actionError && (
        <div
          role="alert"
          style={{
            backgroundColor: '#fee4e2',
            border: '1px solid var(--color-danger)',
            color: 'var(--color-danger)',
            padding: 'var(--space-3)',
            borderRadius: 'var(--radius-control)',
            fontSize: 'var(--text-sm)',
          }}
        >
          {actionError}
        </div>
      )}

      {rolesIsError && (
        <div
          role="alert"
          style={{
            backgroundColor: '#fee4e2',
            border: '1px solid var(--color-danger)',
            color: 'var(--color-danger)',
            padding: 'var(--space-3)',
            borderRadius: 'var(--radius-control)',
            fontSize: 'var(--text-sm)',
          }}
        >
          {rolesError instanceof Error ? rolesError.message : 'Tải danh sách vai trò thất bại.'}
        </div>
      )}

      {staffLoading ? (
        <div>Đang nạp danh sách nhân viên...</div>
      ) : staffIsError ? (
        <div
          role="alert"
          style={{
            backgroundColor: '#fee4e2',
            border: '1px solid var(--color-danger)',
            color: 'var(--color-danger)',
            padding: 'var(--space-4)',
            borderRadius: 'var(--radius-control)',
            fontSize: 'var(--text-sm)',
          }}
        >
          {staffError instanceof Error ? staffError.message : 'Tải danh sách nhân viên thất bại.'}
        </div>
      ) : (
        <div
          style={{
            backgroundColor: 'var(--color-surface)',
            borderRadius: 'var(--radius-card)',
            border: '1px solid var(--color-border)',
            overflow: 'hidden',
          }}
        >
          <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'left' }}>
            <thead>
              <tr
                style={{
                  backgroundColor: 'var(--color-surface-muted)',
                  borderBottom: '1px solid var(--color-border)',
                }}
              >
                <th
                  style={{
                    padding: 'var(--space-3) var(--space-4)',
                    fontSize: 'var(--text-xs)',
                    fontWeight: 600,
                  }}
                >
                  Họ và tên
                </th>
                <th
                  style={{
                    padding: 'var(--space-3) var(--space-4)',
                    fontSize: 'var(--text-xs)',
                    fontWeight: 600,
                  }}
                >
                  Email
                </th>
                <th
                  style={{
                    padding: 'var(--space-3) var(--space-4)',
                    fontSize: 'var(--text-xs)',
                    fontWeight: 600,
                  }}
                >
                  Vai trò
                </th>
                <th
                  style={{
                    padding: 'var(--space-3) var(--space-4)',
                    fontSize: 'var(--text-xs)',
                    fontWeight: 600,
                  }}
                >
                  Trạng thái
                </th>
                {canManageStaff && (
                  <th
                    style={{
                      padding: 'var(--space-3) var(--space-4)',
                      fontSize: 'var(--text-xs)',
                      fontWeight: 600,
                      textAlign: 'right',
                    }}
                  >
                    Thao tác
                  </th>
                )}
              </tr>
            </thead>
            <tbody>
              {staffList.length > 0 ? (
                staffList.map((staff) => (
                  <tr key={staff.id} style={{ borderBottom: '1px solid var(--color-border)' }}>
                    <td
                      style={{
                        padding: 'var(--space-3) var(--space-4)',
                        fontSize: 'var(--text-sm)',
                        fontWeight: 600,
                      }}
                    >
                      {staff.name || 'Chưa có'}
                    </td>
                    <td
                      style={{
                        padding: 'var(--space-3) var(--space-4)',
                        fontSize: 'var(--text-sm)',
                      }}
                    >
                      {staff.email}
                    </td>
                    <td
                      style={{
                        padding: 'var(--space-3) var(--space-4)',
                        fontSize: 'var(--text-sm)',
                      }}
                    >
                      <div style={{ display: 'flex', gap: 'var(--space-1)', flexWrap: 'wrap' }}>
                        {staff.roles.map((r) => (
                          <span
                            key={r}
                            style={{
                              backgroundColor:
                                r === 'admin' ? '#fef3f2' : 'var(--color-surface-muted)',
                              color: r === 'admin' ? '#b42318' : 'var(--color-ink)',
                              border: '1px solid var(--color-border)',
                              padding: '1px 6px',
                              borderRadius: 'var(--radius-pill)',
                              fontSize: 'var(--text-xs)',
                              fontWeight: 500,
                            }}
                          >
                            {r}
                          </span>
                        ))}
                      </div>
                    </td>
                    <td
                      style={{
                        padding: 'var(--space-3) var(--space-4)',
                        fontSize: 'var(--text-xs)',
                      }}
                    >
                      {staff.lockedAt ? (
                        <span
                          style={{
                            backgroundColor: '#fef3f2',
                            color: '#b42318',
                            padding: '2px 8px',
                            borderRadius: 'var(--radius-pill)',
                            fontWeight: 600,
                          }}
                        >
                          Đã khóa
                        </span>
                      ) : (
                        <span
                          style={{
                            backgroundColor: '#ecfdf3',
                            color: '#027a48',
                            padding: '2px 8px',
                            borderRadius: 'var(--radius-pill)',
                            fontWeight: 600,
                          }}
                        >
                          Hoạt động
                        </span>
                      )}
                    </td>
                    {canManageStaff && (
                      <td
                        style={{
                          padding: 'var(--space-3) var(--space-4)',
                          fontSize: 'var(--text-sm)',
                          textAlign: 'right',
                        }}
                      >
                        <div style={{ display: 'inline-flex', gap: 'var(--space-2)' }}>
                          <button
                            type="button"
                            onClick={() => setSelectedStaff(staff)}
                            style={{
                              background: 'none',
                              border: '1px solid var(--color-border)',
                              borderRadius: 'var(--radius-control)',
                              padding: '4px 8px',
                              cursor: 'pointer',
                              fontSize: 'var(--text-xs)',
                            }}
                          >
                            Gán vai trò
                          </button>
                          <button
                            type="button"
                            onClick={() => handleToggleLock(staff)}
                            style={{
                              background: 'none',
                              border: '1px solid',
                              borderColor: staff.lockedAt
                                ? 'var(--color-success)'
                                : 'var(--color-danger)',
                              color: staff.lockedAt
                                ? 'var(--color-success)'
                                : 'var(--color-danger)',
                              borderRadius: 'var(--radius-control)',
                              padding: '4px 8px',
                              cursor: 'pointer',
                              fontSize: 'var(--text-xs)',
                            }}
                          >
                            {staff.lockedAt ? 'Mở khóa' : 'Khóa'}
                          </button>
                        </div>
                      </td>
                    )}
                  </tr>
                ))
              ) : (
                <tr>
                  <td
                    colSpan={canManageStaff ? 5 : 4}
                    style={{
                      padding: 'var(--space-6)',
                      textAlign: 'center',
                      color: 'var(--color-ink-muted)',
                    }}
                  >
                    Chưa có nhân viên nào trong hệ thống.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}

      {inviteOpen && (
        <InviteDialog
          availableRoles={roles}
          onSuccess={() => queryClient.invalidateQueries({ queryKey: ['admin-staff'] })}
          onClose={() => setInviteOpen(false)}
        />
      )}

      {selectedStaff && (
        <AssignRolesDialog
          staff={selectedStaff}
          availableRoles={roles}
          onSuccess={() => queryClient.invalidateQueries({ queryKey: ['admin-staff'] })}
          onClose={() => setSelectedStaff(null)}
        />
      )}
    </div>
  );
}
