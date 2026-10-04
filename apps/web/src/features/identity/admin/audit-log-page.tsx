import { useQuery } from '@tanstack/react-query';
import React, { useState } from 'react';
import { apiRequest } from '../../../shared/api';
import { useCan } from '../use-actor';

export interface AuditLogRow {
  id: string;
  actorId: string;
  actorEmail?: string | null;
  actorName?: string | null;
  action: string;
  targetType: string;
  targetId: string;
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

export function AuditLogPage() {
  const canReadAudit = useCan('audit-logs:read');

  const [filterAction, setFilterAction] = useState('');
  const [filterActor, setFilterActor] = useState('');
  const [filterTargetType, setFilterTargetType] = useState('');
  const [filterStartDate, setFilterStartDate] = useState('');
  const [filterEndDate, setFilterEndDate] = useState('');

  const [appliedFilters, setAppliedFilters] = useState<{
    action?: string;
    actorId?: string;
    targetType?: string;
    startDate?: string;
    endDate?: string;
  }>({});

  // Kiểm tra tường minh cờ isError và đối tượng error để phân biệt rõ giữa trường hợp truy vấn gặp sự cố với trường hợp dữ liệu rỗng.
  const { data, isLoading, isError, error } = useQuery<AuditLogsResponse>({
    queryKey: ['admin-audit-logs', appliedFilters],
    queryFn: () => {
      const params = new URLSearchParams();
      if (appliedFilters.action) params.set('action', appliedFilters.action);
      if (appliedFilters.actorId) params.set('actorId', appliedFilters.actorId);
      if (appliedFilters.targetType) params.set('targetType', appliedFilters.targetType);
      if (appliedFilters.startDate) params.set('startDate', appliedFilters.startDate);
      if (appliedFilters.endDate) params.set('endDate', appliedFilters.endDate);
      params.set('limit', '50');
      const qs = params.toString();
      const url = qs ? `/api/admin/audit-logs?${qs}` : '/api/admin/audit-logs';
      return apiRequest<AuditLogsResponse>(url);
    },
    enabled: canReadAudit,
  });

  if (!canReadAudit) {
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
          Bạn cần quyền <code>audit-logs:read</code> để xem nhật ký thao tác back-office.
        </p>
      </div>
    );
  }

  function handleFilterSubmit(e: React.FormEvent) {
    e.preventDefault();
    let startIso: string | undefined;
    if (filterStartDate) {
      const [year, month, day] = filterStartDate.split('-').map(Number);
      if (year && month && day) {
        startIso = new Date(year, month - 1, day, 0, 0, 0, 0).toISOString();
      }
    }

    let endIso: string | undefined;
    if (filterEndDate) {
      const [year, month, day] = filterEndDate.split('-').map(Number);
      if (year && month && day) {
        endIso = new Date(year, month - 1, day, 23, 59, 59, 999).toISOString();
      }
    }

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
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--space-6)',
        fontFamily: 'var(--font-sans)',
      }}
    >
      <div>
        <h1
          style={{
            fontSize: 'var(--text-xl)',
            fontWeight: 700,
            margin: '0 0 var(--space-1) 0',
            color: 'var(--color-ink)',
          }}
        >
          Nhật ký thao tác (Audit Logs)
        </h1>
        <p style={{ fontSize: 'var(--text-sm)', color: 'var(--color-ink-muted)', margin: 0 }}>
          Theo dõi toàn bộ hành động thay đổi dữ liệu, cấu hình và phân quyền trong hệ thống.
        </p>
      </div>

      {/* Biểu mẫu lọc và tìm kiếm nhật ký thao tác */}
      <form
        noValidate
        onSubmit={handleFilterSubmit}
        style={{
          display: 'flex',
          flexWrap: 'wrap',
          gap: 'var(--space-3)',
          backgroundColor: 'var(--color-surface)',
          padding: 'var(--space-4)',
          borderRadius: 'var(--radius-card)',
          border: '1px solid var(--color-border)',
          alignItems: 'center',
        }}
      >
        <input
          type="text"
          value={filterAction}
          onChange={(e) => setFilterAction(e.target.value)}
          placeholder="Lọc theo hành động (ví dụ: role.create)"
          aria-label="Lọc theo hành động"
          style={{
            padding: 'var(--space-2) var(--space-3)',
            borderRadius: 'var(--radius-control)',
            border: '1px solid var(--color-border)',
            fontSize: 'var(--text-sm)',
            minWidth: 220,
          }}
        />

        <input
          type="text"
          value={filterActor}
          onChange={(e) => setFilterActor(e.target.value)}
          placeholder="Lọc theo Actor ID"
          aria-label="Lọc theo Actor ID"
          style={{
            padding: 'var(--space-2) var(--space-3)',
            borderRadius: 'var(--radius-control)',
            border: '1px solid var(--color-border)',
            fontSize: 'var(--text-sm)',
            minWidth: 180,
          }}
        />

        <input
          type="text"
          value={filterTargetType}
          onChange={(e) => setFilterTargetType(e.target.value)}
          placeholder="Loại đối tượng (role, user...)"
          aria-label="Loại đối tượng"
          style={{
            padding: 'var(--space-2) var(--space-3)',
            borderRadius: 'var(--radius-control)',
            border: '1px solid var(--color-border)',
            fontSize: 'var(--text-sm)',
            minWidth: 160,
          }}
        />
        <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-2)' }}>
          <label
            htmlFor="audit-filter-start-date"
            style={{ fontSize: 'var(--text-xs)', color: 'var(--color-ink-muted)' }}
          >
            Từ ngày:
          </label>
          <input
            id="audit-filter-start-date"
            type="date"
            value={filterStartDate}
            onChange={(e) => setFilterStartDate(e.target.value)}
            style={{
              padding: 'var(--space-2) var(--space-3)',
              borderRadius: 'var(--radius-control)',
              border: '1px solid var(--color-border)',
              fontSize: 'var(--text-sm)',
            }}
          />
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-2)' }}>
          <label
            htmlFor="audit-filter-end-date"
            style={{ fontSize: 'var(--text-xs)', color: 'var(--color-ink-muted)' }}
          >
            Đến ngày:
          </label>
          <input
            id="audit-filter-end-date"
            type="date"
            value={filterEndDate}
            onChange={(e) => setFilterEndDate(e.target.value)}
            style={{
              padding: 'var(--space-2) var(--space-3)',
              borderRadius: 'var(--radius-control)',
              border: '1px solid var(--color-border)',
              fontSize: 'var(--text-sm)',
            }}
          />
        </div>

        <button
          type="submit"
          style={{
            padding: 'var(--space-2) var(--space-4)',
            backgroundColor: 'var(--color-brand)',
            color: '#ffffff',
            border: 'none',
            borderRadius: 'var(--radius-control)',
            fontSize: 'var(--text-sm)',
            fontWeight: 600,
            cursor: 'pointer',
          }}
        >
          Lọc
        </button>

        <button
          type="button"
          onClick={handleReset}
          style={{
            padding: 'var(--space-2) var(--space-3)',
            backgroundColor: 'transparent',
            border: '1px solid var(--color-border)',
            borderRadius: 'var(--radius-control)',
            fontSize: 'var(--text-sm)',
            cursor: 'pointer',
          }}
        >
          Đặt lại
        </button>
      </form>

      {/* Bảng hiển thị chi tiết các bản ghi kiểm toán */}
      {isLoading ? (
        <div>Đang nạp dữ liệu nhật ký...</div>
      ) : isError ? (
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
          {error instanceof Error ? error.message : 'Tải nhật ký thao tác thất bại.'}
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
                  Thời gian
                </th>
                <th
                  style={{
                    padding: 'var(--space-3) var(--space-4)',
                    fontSize: 'var(--text-xs)',
                    fontWeight: 600,
                  }}
                >
                  Hành động
                </th>
                <th
                  style={{
                    padding: 'var(--space-3) var(--space-4)',
                    fontSize: 'var(--text-xs)',
                    fontWeight: 600,
                  }}
                >
                  Người thực hiện
                </th>
                <th
                  style={{
                    padding: 'var(--space-3) var(--space-4)',
                    fontSize: 'var(--text-xs)',
                    fontWeight: 600,
                  }}
                >
                  Đối tượng
                </th>
                <th
                  style={{
                    padding: 'var(--space-3) var(--space-4)',
                    fontSize: 'var(--text-xs)',
                    fontWeight: 600,
                  }}
                >
                  Trước & Sau
                </th>
              </tr>
            </thead>
            <tbody>
              {data?.items && data.items.length > 0 ? (
                data.items.map((log) => (
                  <tr key={log.id} style={{ borderBottom: '1px solid var(--color-border)' }}>
                    <td
                      style={{
                        padding: 'var(--space-3) var(--space-4)',
                        fontSize: 'var(--text-xs)',
                        whiteSpace: 'nowrap',
                      }}
                    >
                      {new Date(log.createdAt).toLocaleString('vi-VN')}
                    </td>
                    <td
                      style={{
                        padding: 'var(--space-3) var(--space-4)',
                        fontSize: 'var(--text-sm)',
                        fontFamily: 'monospace',
                        fontWeight: 600,
                      }}
                    >
                      {log.action}
                    </td>
                    <td
                      style={{
                        padding: 'var(--space-3) var(--space-4)',
                        fontSize: 'var(--text-xs)',
                      }}
                    >
                      {log.actorName ? (
                        <div>
                          <strong>{log.actorName}</strong>
                          <div style={{ color: 'var(--color-ink-muted)', fontSize: '11px' }}>
                            {log.actorEmail || log.actorId}
                          </div>
                        </div>
                      ) : (
                        <span>{log.actorEmail || log.actorId}</span>
                      )}
                    </td>
                    <td
                      style={{
                        padding: 'var(--space-3) var(--space-4)',
                        fontSize: 'var(--text-xs)',
                      }}
                    >
                      <strong>{log.targetType}</strong>: {log.targetId}
                    </td>
                    <td
                      style={{
                        padding: 'var(--space-3) var(--space-4)',
                        fontSize: 'var(--text-xs)',
                      }}
                    >
                      <details>
                        <summary style={{ cursor: 'pointer', color: 'var(--color-brand)' }}>
                          Chi tiết dữ liệu
                        </summary>
                        <div
                          style={{
                            marginTop: 'var(--space-2)',
                            fontSize: '11px',
                            fontFamily: 'monospace',
                          }}
                        >
                          {log.before ? (
                            <div>
                              <strong>Trước:</strong>
                              <pre
                                style={{
                                  margin: 0,
                                  backgroundColor: 'var(--color-surface-muted)',
                                  padding: '4px',
                                }}
                              >
                                {JSON.stringify(log.before, null, 2)}
                              </pre>
                            </div>
                          ) : null}
                          {log.after ? (
                            <div style={{ marginTop: '4px' }}>
                              <strong>Sau:</strong>
                              <pre
                                style={{
                                  margin: 0,
                                  backgroundColor: 'var(--color-surface-muted)',
                                  padding: '4px',
                                }}
                              >
                                {JSON.stringify(log.after, null, 2)}
                              </pre>
                            </div>
                          ) : null}
                        </div>
                      </details>
                    </td>
                  </tr>
                ))
              ) : (
                <tr>
                  <td
                    colSpan={5}
                    style={{
                      padding: 'var(--space-6)',
                      textAlign: 'center',
                      color: 'var(--color-ink-muted)',
                    }}
                  >
                    Chưa có dòng nhật ký nào phù hợp.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
