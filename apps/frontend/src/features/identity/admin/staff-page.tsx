import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { apiRequest } from '../../../shared/api';
import { ForbiddenNotice } from '../../../shared/forbidden-notice';
import { useCan } from '../hooks/use-actor';
import { AssignRolesDialog, type StaffRow } from '../components/assign-roles-dialog';
import { InviteDialog } from '../components/invite-dialog';
import type { RoleData } from '../components/role-form';
import { ErrorAlert } from '../../../shared/error-alert';
import './identity-admin.css';

// API trả danh sách nhân viên theo trang. Màn này chưa có phân trang và bộ lọc, nên chỉ đọc `items`.
interface StaffListResponse {
  items: StaffRow[];
  total: number;
  page: number;
  limit: number;
}

// Một trang 100 dòng đủ cho số nhân viên của giai đoạn demo. FLT-UI2 sẽ thay bằng phân trang và bộ lọc.
const STAFF_PAGE_LIMIT = 100;

// Trang quản lý nhân viên: xem danh sách, mời, đổi role, khóa và mở khóa.
export function StaffPage() {
  // Hai quyền tách nhau: người chỉ có staff:read xem được danh sách nhưng không thấy các nút thao tác.
  const canReadStaff = useCan('staff:read');
  const canManageStaff = useCan('staff:manage');
  const queryClient = useQueryClient();

  // Trạng thái mở của hai hộp thoại; selectedStaff khác null nghĩa là đang đổi role cho người đó.
  const [inviteOpen, setInviteOpen] = useState(false);
  const [selectedStaff, setSelectedStaff] = useState<StaffRow | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  // Lấy cả cờ lỗi để hiện thông báo khi tải thất bại. Nếu bỏ qua, lỗi 403 hay mất mạng sẽ hiện như danh sách rỗng.
  const {
    data: staffList = [],
    isLoading: staffLoading,
    isError: staffIsError,
    error: staffError,
  } = useQuery<StaffRow[]>({
    queryKey: ['admin-staff'],
    // Lấy `items` từ response phân trang để phần còn lại của màn vẫn làm việc với mảng nhân viên.
    queryFn: async () => {
      const page = await apiRequest<StaffListResponse>(
        `/api/admin/staff?limit=${STAFF_PAGE_LIMIT}`,
      );
      return page.items;
    },
    // Không gọi API khi không có quyền, vì chắc chắn bị từ chối với 403.
    enabled: canReadStaff,
  });

  // Danh sách role cho hộp thoại mời và đổi role; chỉ người có quyền quản lý nhân viên mới cần.
  const {
    data: roles = [],
    isError: rolesIsError,
    error: rolesError,
  } = useQuery<RoleData[]>({
    queryKey: ['admin-roles'],
    queryFn: () => apiRequest<RoleData[]>('/api/admin/roles'),
    enabled: canManageStaff,
  });

  // Hook ở trên phải chạy hết trước lệnh return sớm này; backend vẫn chặn mọi request thật sự.
  if (!canReadStaff) {
    return <ForbiddenNotice permission="staff:read" subject="danh sách nhân viên" />;
  }

  // Một nút dùng cho cả khóa và mở khóa, tùy trạng thái hiện tại của nhân viên.
  async function handleToggleLock(staff: StaffRow) {
    const isLocking = !staff.lockedAt;
    const confirmMsg = isLocking
      ? `Bạn có chắc chắn muốn khóa tài khoản nhân viên "${staff.name || staff.email}"?`
      : `Bạn có chắc chắn muốn mở khóa tài khoản nhân viên "${staff.name || staff.email}"?`;

    // Hỏi lại trước khi khóa hay mở khóa vì thao tác này ảnh hưởng ngay tới người khác.
    if (!window.confirm(confirmMsg)) return;

    // Xóa lỗi của lần thao tác trước để không hiện lẫn.
    setActionError(null);
    try {
      // Một route cho cả hai thao tác. Trường `locked` cho API biết cần khóa hay mở khóa.
      await apiRequest(`/api/admin/staff/${staff.id}`, {
        method: 'PATCH',
        body: JSON.stringify({ locked: isLocking }),
      });
      // Nạp lại danh sách để dòng của người đó hiện trạng thái khóa mới.
      await queryClient.invalidateQueries({ queryKey: ['admin-staff'] });
    } catch (err: unknown) {
      // Hiện nguyên thông báo của API (admin cuối cùng, tự khóa mình, không đủ quyền...) vì đã là tiếng Việt.
      if (err instanceof Error) {
        setActionError(err.message);
      } else {
        setActionError('Thao tác khóa/mở khóa thất bại.');
      }
    }
  }

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-head-text">
          <h1>Quản lý Nhân viên</h1>
          <p>
            Danh sách nhân viên, phân quyền vai trò và quản lý trạng thái tài khoản back-office.
          </p>
        </div>
        {/* Chỉ quyền staff:manage được mời nhân viên mới. */}
        {canManageStaff && (
          <button type="button" className="btn btn-primary" onClick={() => setInviteOpen(true)}>
            + Mời nhân viên
          </button>
        )}
      </div>

      {actionError && <ErrorAlert message={actionError} />}

      {rolesIsError && (
        <ErrorAlert
          message={
            rolesError instanceof Error ? rolesError.message : 'Tải danh sách vai trò thất bại.'
          }
        />
      )}

      {staffLoading ? (
        <p role="status" className="empty-state">
          Đang nạp danh sách nhân viên...
        </p>
      ) : staffIsError ? (
        <ErrorAlert
          message={
            staffError instanceof Error ? staffError.message : 'Tải danh sách nhân viên thất bại.'
          }
        />
      ) : (
        <section className="card card-flush">
          <div className="table-wrap">
            <table className="tbl identity-table">
              <thead>
                <tr>
                  <th scope="col">Nhân viên</th>
                  <th scope="col">Vai trò</th>
                  <th scope="col">Trạng thái</th>
                  {canManageStaff && (
                    <th scope="col" className="num">
                      Thao tác
                    </th>
                  )}
                </tr>
              </thead>
              <tbody>
                {staffList.length > 0 ? (
                  staffList.map((staff) => (
                    <tr key={staff.id}>
                      <th scope="row">
                        {staff.name || 'Chưa có tên'}
                        <span className="cell-sub wrap-anywhere">{staff.email}</span>
                      </th>
                      <td>
                        <div className="row-actions">
                          {/* Role admin dùng badge đỏ vì có mọi quyền, cần nổi bật khi rà soát. */}
                          {staff.roles.map((role) => (
                            <span
                              key={role}
                              className={role === 'admin' ? 'badge badge-bad' : 'badge'}
                            >
                              {role}
                            </span>
                          ))}
                        </div>
                      </td>
                      <td>
                        {staff.lockedAt ? (
                          <span className="badge badge-bad">Đã khóa</span>
                        ) : (
                          <span className="badge badge-ok">Hoạt động</span>
                        )}
                      </td>
                      {canManageStaff && (
                        <td className="num">
                          <div className="row-actions identity-row-actions">
                            <button
                              type="button"
                              className="btn btn-sm"
                              onClick={() => setSelectedStaff(staff)}
                            >
                              Gán vai trò
                            </button>
                            {/* Nút khóa dùng màu nguy hiểm; nút mở khóa là thao tác thường. */}
                            <button
                              type="button"
                              className={staff.lockedAt ? 'btn btn-sm' : 'btn btn-sm btn-danger'}
                              onClick={() => handleToggleLock(staff)}
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
                    <td colSpan={canManageStaff ? 4 : 3} className="empty-state">
                      Chưa có nhân viên nào trong hệ thống.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </section>
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
