import { useQuery } from '@tanstack/react-query';
import React, { useState } from 'react';
import { apiRequest } from '../../../shared/api';
import { ForbiddenNotice } from '../../../shared/forbidden-notice';
import { useCan } from '../hooks/use-actor';
import { ErrorAlert } from '../../../shared/error-alert';
import './identity-admin.css';

// Một dòng nhật ký như API trả về; actorEmail và actorName do server tra thêm để hiển thị người làm thay vì chuỗi UUID.
export interface AuditLogRow {
  id: string;
  actorId: string;
  actorEmail?: string | null;
  actorName?: string | null;
  action: string;
  targetType: string;
  targetId: string;
  // Trạng thái trước và sau thao tác; kiểu unknown vì cấu trúc phụ thuộc loại thao tác.
  before: unknown;
  after: unknown;
  createdAt: string;
}

interface AuditLogsResponse {
  items: AuditLogRow[];
  total: number;
  page: number;
  limit: number;
}

// Trang xem nhật ký thao tác, chỉ đọc, có lọc theo hành động, người làm, đối tượng và khoảng ngày.
export function AuditLogPage() {
  const canReadAudit = useCan('audit-logs:read');

  // Giá trị đang gõ trong các ô lọc, chưa áp dụng cho tới khi bấm nút Lọc.
  const [filterAction, setFilterAction] = useState('');
  const [filterActor, setFilterActor] = useState('');
  const [filterTargetType, setFilterTargetType] = useState('');
  const [filterStartDate, setFilterStartDate] = useState('');
  const [filterEndDate, setFilterEndDate] = useState('');

  // Bộ lọc đã áp dụng, nằm trong khóa truy vấn để đổi bộ lọc thì tự gọi lại API.
  const [appliedFilters, setAppliedFilters] = useState<{
    action?: string;
    actorId?: string;
    targetType?: string;
    startDate?: string;
    endDate?: string;
  }>({});

  // Lấy cả cờ lỗi để phân biệt "tải thất bại" với "không có dòng nào khớp bộ lọc".
  const { data, isLoading, isError, error } = useQuery<AuditLogsResponse>({
    queryKey: ['admin-audit-logs', appliedFilters],
    queryFn: () => {
      // Chỉ thêm tham số nào có giá trị, để URL gọn và server không lọc theo chuỗi rỗng.
      const params = new URLSearchParams();
      if (appliedFilters.action) params.set('action', appliedFilters.action);
      if (appliedFilters.actorId) params.set('actorId', appliedFilters.actorId);
      if (appliedFilters.targetType) params.set('targetType', appliedFilters.targetType);
      if (appliedFilters.startDate) params.set('startDate', appliedFilters.startDate);
      if (appliedFilters.endDate) params.set('endDate', appliedFilters.endDate);
      // Lấy 50 dòng mỗi lần; server giới hạn tối đa 100.
      params.set('limit', '50');
      const qs = params.toString();
      const url = qs ? `/api/admin/audit-logs?${qs}` : '/api/admin/audit-logs';
      return apiRequest<AuditLogsResponse>(url);
    },
    // Không gọi API khi không có quyền, vì chắc chắn bị từ chối với 403.
    enabled: canReadAudit,
  });

  // Hook ở trên phải chạy hết trước lệnh return sớm này; backend vẫn chặn mọi request thật sự.
  if (!canReadAudit) {
    return <ForbiddenNotice permission="audit-logs:read" subject="nhật ký thao tác back-office" />;
  }

  function handleFilterSubmit(e: React.FormEvent) {
    // Chặn trình duyệt tải lại trang khi gửi form.
    e.preventDefault();
    // Ô ngày trả về chuỗi 'YYYY-MM-DD'. Tách ra rồi tạo Date theo giờ máy người dùng, vì new Date('YYYY-MM-DD') hiểu là nửa đêm UTC và lệch 7 tiếng ở Việt Nam.
    let startIso: string | undefined;
    if (filterStartDate) {
      const [year, month, day] = filterStartDate.split('-').map(Number);
      if (year && month && day) {
        // Từ ngày: bắt đầu từ 00:00:00 của ngày đó.
        startIso = new Date(year, month - 1, day, 0, 0, 0, 0).toISOString();
      }
    }

    let endIso: string | undefined;
    if (filterEndDate) {
      const [year, month, day] = filterEndDate.split('-').map(Number);
      if (year && month && day) {
        // Đến ngày: lấy tới 23:59:59.999 để bao trọn cả ngày đó; nếu để 00:00 thì mất hết thao tác trong ngày chọn.
        // toISOString vì server chỉ nhận chuỗi ISO có múi giờ.
        endIso = new Date(year, month - 1, day, 23, 59, 59, 999).toISOString();
      }
    }

    // Ô để trống thành undefined để không gửi tham số lọc rỗng.
    setAppliedFilters({
      action: filterAction.trim() || undefined,
      actorId: filterActor.trim() || undefined,
      targetType: filterTargetType.trim() || undefined,
      startDate: startIso,
      endDate: endIso,
    });
  }

  function handleReset() {
    setFilterAction('');
    setFilterActor('');
    setFilterTargetType('');
    setFilterStartDate('');
    setFilterEndDate('');
    setAppliedFilters({});
  }

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-head-text">
          <h1>Nhật ký thao tác (Audit Logs)</h1>
          <p>Theo dõi toàn bộ hành động thay đổi dữ liệu, cấu hình và phân quyền trong hệ thống.</p>
        </div>
      </div>

      <section className="card card-flush">
        {/* Biểu mẫu lọc nằm trong thẻ bảng, để bộ lọc và kết quả đọc thành một khối. */}
        <form noValidate onSubmit={handleFilterSubmit} className="filters-row">
          <div className="field">
            <label htmlFor="audit-filter-action">Hành động</label>
            <input
              id="audit-filter-action"
              type="text"
              value={filterAction}
              onChange={(e) => setFilterAction(e.target.value)}
              placeholder="role.create"
            />
          </div>
          <div className="field">
            <label htmlFor="audit-filter-actor">Actor ID</label>
            <input
              id="audit-filter-actor"
              type="text"
              value={filterActor}
              onChange={(e) => setFilterActor(e.target.value)}
              placeholder="Mã người thực hiện"
            />
          </div>
          <div className="field">
            <label htmlFor="audit-filter-target">Loại đối tượng</label>
            <input
              id="audit-filter-target"
              type="text"
              value={filterTargetType}
              onChange={(e) => setFilterTargetType(e.target.value)}
              placeholder="role, user..."
            />
          </div>
          <div className="field">
            <label htmlFor="audit-filter-start-date">Từ ngày</label>
            <input
              id="audit-filter-start-date"
              type="date"
              value={filterStartDate}
              onChange={(e) => setFilterStartDate(e.target.value)}
            />
          </div>
          <div className="field">
            <label htmlFor="audit-filter-end-date">Đến ngày</label>
            <input
              id="audit-filter-end-date"
              type="date"
              value={filterEndDate}
              onChange={(e) => setFilterEndDate(e.target.value)}
            />
          </div>
          <div className="head-actions">
            <button type="submit" className="btn btn-primary">
              Lọc
            </button>
            <button type="button" className="btn" onClick={handleReset}>
              Đặt lại
            </button>
          </div>
        </form>

        {isLoading ? (
          <p role="status" className="empty-state">
            Đang nạp dữ liệu nhật ký...
          </p>
        ) : isError ? (
          <ErrorAlert
            message={error instanceof Error ? error.message : 'Tải nhật ký thao tác thất bại.'}
            className="identity-table-alert"
          />
        ) : (
          <div className="table-wrap">
            <table className="tbl identity-table audit-table">
              <thead>
                <tr>
                  <th scope="col">Thời gian</th>
                  <th scope="col">Hành động</th>
                  <th scope="col">Người thực hiện</th>
                  <th scope="col">Đối tượng</th>
                  <th scope="col">Trước &amp; Sau</th>
                </tr>
              </thead>
              <tbody>
                {data?.items && data.items.length > 0 ? (
                  data.items.map((log) => (
                    <tr key={log.id}>
                      <td className="audit-time">
                        {new Date(log.createdAt).toLocaleString('vi-VN')}
                      </td>
                      <td>
                        <span className="badge mono">{log.action}</span>
                      </td>
                      <td>
                        {log.actorName ? (
                          <>
                            <strong>{log.actorName}</strong>
                            <span className="cell-sub wrap-anywhere">
                              {log.actorEmail || log.actorId}
                            </span>
                          </>
                        ) : (
                          <span className="wrap-anywhere">{log.actorEmail || log.actorId}</span>
                        )}
                      </td>
                      <td>
                        <strong>{log.targetType}</strong>
                        <span className="cell-sub mono wrap-anywhere">{log.targetId}</span>
                      </td>
                      <td>
                        <details className="audit-details">
                          <summary>Chi tiết dữ liệu</summary>
                          {/* JSON dài nằm trong khối cuộn riêng, để cột không đẩy bảng rộng ra. */}
                          {log.before ? (
                            <div>
                              <strong className="small">Trước:</strong>
                              <pre className="audit-json">
                                {JSON.stringify(log.before, null, 2)}
                              </pre>
                            </div>
                          ) : null}
                          {log.after ? (
                            <div>
                              <strong className="small">Sau:</strong>
                              <pre className="audit-json">{JSON.stringify(log.after, null, 2)}</pre>
                            </div>
                          ) : null}
                        </details>
                      </td>
                    </tr>
                  ))
                ) : (
                  <tr>
                    <td colSpan={5} className="empty-state">
                      Chưa có dòng nhật ký nào phù hợp.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}
