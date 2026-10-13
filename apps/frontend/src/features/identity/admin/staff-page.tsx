import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { apiRequest } from '../../../shared/api';
import { FilteredEmptyState } from '../../../shared/filtered-empty-state';
import { Pager } from '../../../shared/pager';
import { SearchInput } from '../../../shared/search-input';
import { useListFilters } from '../../../shared/use-list-filters';
import { ForbiddenNotice } from '../../../shared/forbidden-notice';
import { useCan } from '../hooks/use-actor';
import { AssignRolesDialog, type StaffRow } from '../components/assign-roles-dialog';
import { InviteDialog } from '../components/invite-dialog';
import type { RoleData } from '../components/role-form';
import { ErrorAlert } from '../../../shared/error-alert';
import './identity-admin.css';

interface StaffListResponse {
  items: StaffRow[];
  total: number;
  page: number;
  limit: number;
}

// Tên tham số bộ lọc trên URL. Trùng tên tham số của `GET /api/admin/staff`.
const FILTER_NAMES = ['search', 'status', 'role'] as const;

// Nhãn và màu badge theo trường `status` của API. Nhân viên đã mời chưa đặt mật khẩu nên không phải "Hoạt động".
const STATUS_BADGES: Record<string, { label: string; className: string }> = {
  active: { label: 'Hoạt động', className: 'badge badge-ok' },
  invited: { label: 'Đã mời', className: 'badge' },
  locked: { label: 'Đã khóa', className: 'badge badge-bad' },
};

// Mã role của khách hàng không dùng cho nhân viên, nên không đưa vào danh sách chọn.
const CUSTOMER_ROLE_CODE = 'customer';

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

  // Bộ lọc và trang nằm trên URL. Mở lại link cho đúng danh sách đã lọc.
  const { values, page, setFilter, setPage } = useListFilters(FILTER_NAMES);
  const hasFilters = FILTER_NAMES.some((name) => values[name] !== '');

  // Lấy cả cờ lỗi để hiện thông báo khi tải thất bại. Nếu bỏ qua, lỗi 403 hay mất mạng sẽ hiện như danh sách rỗng.
  const {
    data: staffPage,
    isLoading: staffLoading,
    isError: staffIsError,
    error: staffError,
  } = useQuery<StaffListResponse>({
    // Mọi bộ lọc và số trang nằm trong khóa truy vấn. Mỗi tổ hợp có cache riêng và đổi giá trị thì tự gọi lại.
    queryKey: ['admin-staff', values, page],
    queryFn: () => {
      // `URLSearchParams` mã hóa ký tự đặc biệt trong từ khóa để URL không sai.
      const params = new URLSearchParams();
      for (const name of FILTER_NAMES) {
        if (values[name] !== '') params.set(name, values[name]);
      }
      // Trang 1 là mặc định của API nên không gửi.
      if (page > 1) params.set('page', String(page));
      const queryString = params.toString();
      return apiRequest<StaffListResponse>(
        queryString ? `/api/admin/staff?${queryString}` : '/api/admin/staff',
      );
    },
    // Không gọi API khi không có quyền, vì chắc chắn bị từ chối với 403.
    enabled: canReadStaff,
  });

  // Danh sách role cho bộ lọc, hộp thoại mời và đổi role. API cho mọi nhân viên đọc, nên người chỉ có `staff:read` cũng cần để lọc.
  const {
    data: roles = [],
    isError: rolesIsError,
    error: rolesError,
  } = useQuery<RoleData[]>({
    queryKey: ['admin-roles'],
    queryFn: () => apiRequest<RoleData[]>('/api/admin/roles'),
    enabled: canReadStaff,
  });

  const staffList = staffPage?.items ?? [];
  const filterableRoles = roles.filter((role) => role.code !== CUSTOMER_ROLE_CODE);

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

      <section className="card card-flush">
        <div className="filters-row">
          <SearchInput
            label="Tìm theo tên hoặc email"
            value={values.search}
            onSearch={(text) => setFilter('search', text)}
          />
          <div className="field">
            <label htmlFor="staff-status">Trạng thái</label>
            <select
              id="staff-status"
              value={values.status}
              onChange={(event) => setFilter('status', event.target.value)}
            >
              <option value="">Tất cả</option>
              <option value="active">Hoạt động</option>
              <option value="invited">Đã mời</option>
              <option value="locked">Đã khóa</option>
            </select>
          </div>
          <div className="field">
            <label htmlFor="staff-role">Vai trò</label>
            <select
              id="staff-role"
              value={values.role}
              onChange={(event) => setFilter('role', event.target.value)}
            >
              <option value="">Tất cả</option>
              {filterableRoles.map((role) => (
                <option key={role.code} value={role.code}>
                  {role.name}
                </option>
              ))}
            </select>
          </div>
        </div>

        {staffLoading ? (
          <p role="status" className="empty-state">
            Đang nạp danh sách nhân viên...
          </p>
        ) : staffIsError ? (
          <ErrorAlert
            message={
              staffError instanceof Error ? staffError.message : 'Tải danh sách nhân viên thất bại.'
            }
            className="identity-table-alert"
          />
        ) : staffList.length === 0 && (hasFilters || page > 1) ? (
          <FilteredEmptyState page={page} onFirstPage={() => setPage(1)} />
        ) : (
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
                        {/* Đọc trường `status` của API. Trạng thái lạ hiện nguyên mã với badge trung tính. */}
                        <span className={STATUS_BADGES[staff.status]?.className ?? 'badge'}>
                          {STATUS_BADGES[staff.status]?.label ?? staff.status}
                        </span>
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
        )}
        {staffPage && !staffIsError && (
          <Pager
            page={page}
            total={staffPage.total}
            limit={staffPage.limit}
            onPageChange={setPage}
            label="Phân trang nhân viên"
          />
        )}
      </section>

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
