import { useQuery, useQueryClient } from '@tanstack/react-query';
import React, { useEffect, useState } from 'react';
import { apiRequest } from '../../../shared/api';
import { ForbiddenNotice } from '../../../shared/forbidden-notice';
import { useCan } from '../../identity/hooks/use-actor';
import './ai-settings.css';

export interface AiSettingsData {
  monthlyBudgetUsd: number;
  chatDailyLimitUser: number;
  chatDailyLimitAnon: number;
}

export interface UsageGroupData {
  costUsd: number;
  calls: number;
}

export interface UsageSummaryData extends UsageGroupData {
  errorRate: number;
  p95LatencyMs: number;
  byDay: (UsageGroupData & { day: string })[];
  byFeature: (UsageGroupData & { feature: string })[];
  budgetWarning: boolean;
}

// Trang quản trị theo dõi chi phí AI và quản lý cấu hình ngân sách, hạn mức.
export function AiSettingsPage() {
  const canReadUsage = useCan('ai-usage:read');
  const canManageSettings = useCan('ai-settings:manage');
  const queryClient = useQueryClient();

  const [filterStartDate, setFilterStartDate] = useState('');
  const [filterEndDate, setFilterEndDate] = useState('');
  const [appliedDateRange, setAppliedDateRange] = useState<{ from?: string; to?: string }>({});

  const [budgetInput, setBudgetInput] = useState('');
  const [userLimitInput, setUserLimitInput] = useState('');
  const [anonLimitInput, setAnonLimitInput] = useState('');
  // Cờ đánh dấu đã nạp giá trị ban đầu vào form để tránh ghi đè dữ liệu người dùng đang gõ dở.
  const [hasInitialized, setHasInitialized] = useState(false);
  const [feedback, setFeedback] = useState<{ isError: boolean; text: string } | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  // Tải báo cáo sử dụng AI theo khoảng thời gian đã lọc.
  const {
    data: usage,
    isLoading: isUsageLoading,
    isError: isUsageError,
    error: usageError,
  } = useQuery<UsageSummaryData>({
    queryKey: ['admin-ai-usage', appliedDateRange],
    queryFn: () => {
      const params = new URLSearchParams();
      if (appliedDateRange.from) params.set('from', appliedDateRange.from);
      if (appliedDateRange.to) params.set('to', appliedDateRange.to);
      const qs = params.toString();
      return apiRequest<UsageSummaryData>(qs ? `/api/admin/ai/usage?${qs}` : '/api/admin/ai/usage');
    },
    enabled: canReadUsage,
  });

  // Tải cấu hình ngân sách và hạn mức hiện tại; tắt tự refetch khi focus cửa sổ để bảo vệ dữ liệu đang sửa.
  const {
    data: settings,
    isLoading: isSettingsLoading,
    isError: isSettingsError,
    error: settingsError,
  } = useQuery<AiSettingsData>({
    queryKey: ['admin-ai-settings'],
    queryFn: () => apiRequest<AiSettingsData>('/api/admin/ai/settings'),
    enabled: canReadUsage,
    refetchOnWindowFocus: false,
  });

  // Chỉ khởi tạo giá trị vào form ở lần nạp thành công đầu tiên, không ghi đè khi đang sửa.
  useEffect(() => {
    if (settings && !hasInitialized) {
      setBudgetInput(String(settings.monthlyBudgetUsd));
      setUserLimitInput(String(settings.chatDailyLimitUser));
      setAnonLimitInput(String(settings.chatDailyLimitAnon));
      setHasInitialized(true);
    }
  }, [settings, hasInitialized]);

  // Ngăn chặn hiển thị nếu người dùng thiếu quyền xem báo cáo sử dụng AI.
  if (!canReadUsage) {
    return <ForbiddenNotice permission="ai-usage:read" subject="thống kê và cấu hình AI" />;
  }

  // Chuyển ngày chọn sang múi giờ Việt Nam (+07:00) thay vì múi giờ cục bộ của trình duyệt.
  function handleFilterSubmit(e: React.FormEvent) {
    e.preventDefault();
    let fromIso: string | undefined;
    if (filterStartDate) {
      // Đầu ngày theo múi giờ Việt Nam: 00:00:00+07:00.
      fromIso = new Date(`${filterStartDate}T00:00:00+07:00`).toISOString();
    }

    let toIso: string | undefined;
    if (filterEndDate) {
      // Cuối ngày theo múi giờ Việt Nam: 23:59:59.999+07:00.
      toIso = new Date(`${filterEndDate}T23:59:59.999+07:00`).toISOString();
    }

    setAppliedDateRange({ from: fromIso, to: toIso });
  }

  function handleResetFilter() {
    setFilterStartDate('');
    setFilterEndDate('');
    setAppliedDateRange({});
  }

  // Cập nhật cấu hình AI khi nhân viên có quyền quản lý gửi biểu mẫu.
  async function handleSaveSettings(e: React.FormEvent) {
    e.preventDefault();
    setFeedback(null);

    // Chuỗi rỗng bị Number chuyển thành 0, không được vô tình tắt toàn bộ AI khi người dùng xóa input.
    if (!budgetInput.trim()) {
      setFeedback({ isError: true, text: 'Vui lòng nhập ngân sách tháng.' });
      return;
    }

    const monthlyBudgetUsd = Number(budgetInput);
    const chatDailyLimitUser = Number(userLimitInput);
    const chatDailyLimitAnon = Number(anonLimitInput);

    // Kiểm tra hợp lệ dữ liệu phía máy khách trước khi gửi lên API.
    if (
      !Number.isFinite(monthlyBudgetUsd) ||
      monthlyBudgetUsd < 0 ||
      !Number.isInteger(chatDailyLimitUser) ||
      chatDailyLimitUser <= 0 ||
      !Number.isInteger(chatDailyLimitAnon) ||
      chatDailyLimitAnon <= 0
    ) {
      setFeedback({
        isError: true,
        text: 'Vui lòng nhập ngân sách không âm và hạn mức chat là số nguyên dương.',
      });
      return;
    }

    // Ngân sách 0 chặn mọi lời gọi mới nên cần xác nhận hành động tắt AI có chủ đích.
    if (
      monthlyBudgetUsd === 0 &&
      !window.confirm('Ngân sách 0 USD sẽ tạm dừng các lời gọi AI mới. Bạn có muốn tiếp tục?')
    ) {
      return;
    }

    setIsSubmitting(true);
    try {
      const updated = await apiRequest<AiSettingsData>('/api/admin/ai/settings', {
        method: 'PUT',
        body: JSON.stringify({ monthlyBudgetUsd, chatDailyLimitUser, chatDailyLimitAnon }),
      });

      // Cập nhật lại input form sau khi lưu thành công.
      setBudgetInput(String(updated.monthlyBudgetUsd));
      setUserLimitInput(String(updated.chatDailyLimitUser));
      setAnonLimitInput(String(updated.chatDailyLimitAnon));

      // Làm mới cache để cập nhật tức thì trạng thái ngân sách và cảnh báo.
      await queryClient.invalidateQueries({ queryKey: ['admin-ai-settings'] });
      await queryClient.invalidateQueries({ queryKey: ['admin-ai-usage'] });

      setFeedback({ isError: false, text: 'Cập nhật cấu hình AI thành công.' });
    } catch (err) {
      setFeedback({
        isError: true,
        text: err instanceof Error ? err.message : 'Có lỗi xảy ra khi cập nhật.',
      });
    } finally {
      setIsSubmitting(false);
    }
  }

  const isSaveDisabled = !canManageSettings || isSubmitting || isSettingsLoading || isSettingsError;
  const isSettingsInputDisabled = !canManageSettings || isSubmitting || isSettingsError;
  // Tỉ lệ lỗi trên 5% hiện màu lỗi để nhân viên chú ý; dưới ngưỡng hiện màu thành công.
  const errorRateClass =
    isUsageError || (usage?.errorRate ?? 0) > 0.05 ? 'kpi-value kpi-bad' : 'kpi-value kpi-ok';

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-head-text">
          <h1>Cấu hình và sử dụng AI</h1>
          <p>
            Theo dõi chi phí mô hình (ước tính cao điểm bảo thủ), độ trễ p95, tỉ lệ lỗi và điều
            chỉnh hạn mức.
          </p>
        </div>
      </div>

      {/* Cảnh báo ngân sách khi chi tiêu tháng hiện tại vượt 80% */}
      {usage?.budgetWarning && (
        <p role="alert" className="alert alert-warn">
          <strong>Cảnh báo ngân sách tháng:</strong> Chi tiêu AI trong tháng đã chạm hoặc vượt 80%
          ngân sách cấu hình.
        </p>
      )}

      <ul className="kpis" aria-label="Tổng quan sử dụng AI">
        <li className="kpi">
          <small>Chi phí ước tính (USD)</small>
          <b className="kpi-value">
            {isUsageError ? '-' : isUsageLoading ? '...' : `$${(usage?.costUsd ?? 0).toFixed(4)}`}
          </b>
        </li>
        <li className="kpi">
          <small>Tổng lượt gọi</small>
          <b className="kpi-value">
            {isUsageError
              ? '-'
              : isUsageLoading
                ? '...'
                : (usage?.calls ?? 0).toLocaleString('vi-VN')}
          </b>
        </li>
        <li className="kpi">
          <small>Tỉ lệ lỗi</small>
          <b className={errorRateClass}>
            {isUsageError
              ? '-'
              : isUsageLoading
                ? '...'
                : `${((usage?.errorRate ?? 0) * 100).toFixed(1)}%`}
          </b>
        </li>
        <li className="kpi">
          <small>Độ trễ p95</small>
          <b className="kpi-value">
            {isUsageError ? '-' : isUsageLoading ? '...' : `${usage?.p95LatencyMs ?? 0} ms`}
          </b>
        </li>
      </ul>

      {/* Biểu mẫu chỉnh sửa ngân sách và hạn mức */}
      <section className="card">
        <div className="card-head">
          <h2>Ngân sách và hạn mức chat</h2>
        </div>
        <p className="muted small ai-settings-note">
          {canManageSettings
            ? 'Thay đổi ngân sách và hạn mức sẽ có hiệu lực ngay ở lượt gọi tiếp theo.'
            : 'Chế độ chỉ xem (ai-usage:read). Cần quyền ai-settings:manage để thay đổi.'}
        </p>

        {isSettingsError && (
          <p role="alert" className="alert ai-settings-note">
            Lỗi tải cài đặt:{' '}
            {(settingsError as Error)?.message || 'Không thể tải cấu hình AI từ máy chủ.'}
          </p>
        )}

        {/* Thông báo thành công dùng nền xanh, thông báo lỗi dùng nền đỏ nhạt. */}
        {feedback && (
          <p
            role="alert"
            className={
              feedback.isError ? 'alert ai-settings-note' : 'alert alert-ok ai-settings-note'
            }
          >
            {feedback.text}
          </p>
        )}

        {isSettingsLoading ? (
          <p role="status" className="empty-state">
            Đang tải cài đặt...
          </p>
        ) : (
          <form noValidate onSubmit={handleSaveSettings}>
            <div className="form-grid form-grid-3">
              <div className="field">
                <label htmlFor="ai-budget-usd">Ngân sách tháng (USD)</label>
                <input
                  id="ai-budget-usd"
                  type="number"
                  step="0.01"
                  min="0"
                  disabled={isSettingsInputDisabled}
                  value={budgetInput}
                  onChange={(e) => setBudgetInput(e.target.value)}
                />
              </div>
              <div className="field">
                <label htmlFor="ai-limit-user">Hạn mức chat ngày (User)</label>
                <input
                  id="ai-limit-user"
                  type="number"
                  step="1"
                  min="1"
                  disabled={isSettingsInputDisabled}
                  value={userLimitInput}
                  onChange={(e) => setUserLimitInput(e.target.value)}
                />
              </div>
              <div className="field">
                <label htmlFor="ai-limit-anon">Hạn mức chat ngày (Khách)</label>
                <input
                  id="ai-limit-anon"
                  type="number"
                  step="1"
                  min="1"
                  disabled={isSettingsInputDisabled}
                  value={anonLimitInput}
                  onChange={(e) => setAnonLimitInput(e.target.value)}
                />
              </div>
            </div>

            {/* Chỉ quyền ai-settings:manage mới thấy nút lưu. */}
            {canManageSettings && (
              <button type="submit" className="btn btn-primary" disabled={isSaveDisabled}>
                {isSubmitting ? 'Đang lưu...' : 'Lưu cấu hình'}
              </button>
            )}
          </form>
        )}
      </section>

      {/* Bộ lọc khoảng ngày theo giờ Việt Nam */}
      <form noValidate onSubmit={handleFilterSubmit} className="card filters-row ai-date-filter">
        <div className="field">
          <label htmlFor="ai-filter-start">Từ ngày</label>
          <input
            id="ai-filter-start"
            type="date"
            value={filterStartDate}
            onChange={(e) => setFilterStartDate(e.target.value)}
          />
        </div>
        <div className="field">
          <label htmlFor="ai-filter-end">Đến ngày</label>
          <input
            id="ai-filter-end"
            type="date"
            value={filterEndDate}
            onChange={(e) => setFilterEndDate(e.target.value)}
          />
        </div>
        <div className="head-actions">
          <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
            Lọc
          </button>
          <button type="button" className="btn" onClick={handleResetFilter}>
            Đặt lại
          </button>
        </div>
      </form>

      {/* Bảng chi phí phân rã theo ngày và theo tính năng */}
      <div className="ai-usage-grid">
        <UsageTable
          title="Chi phí theo ngày"
          firstColumn="Ngày"
          loadingText="Đang nạp dữ liệu theo ngày..."
          isLoading={isUsageLoading}
          errorMessage={isUsageError ? (usageError as Error)?.message : undefined}
          rows={(usage?.byDay ?? []).map((row) => ({ ...row, label: row.day }))}
        />
        <UsageTable
          title="Chi phí theo tính năng"
          firstColumn="Tính năng"
          loadingText="Đang nạp dữ liệu theo tính năng..."
          isLoading={isUsageLoading}
          errorMessage={isUsageError ? (usageError as Error)?.message : undefined}
          rows={(usage?.byFeature ?? []).map((row) => ({ ...row, label: row.feature }))}
        />
      </div>
    </div>
  );
}

interface UsageTableProps {
  title: string;
  firstColumn: string;
  loadingText: string;
  isLoading: boolean;
  errorMessage?: string;
  rows: (UsageGroupData & { label: string })[];
}

// Bảng chi phí dùng chung cho hai cách phân rã (theo ngày, theo tính năng), vì cùng cột và cùng trạng thái.
function UsageTable({
  title,
  firstColumn,
  loadingText,
  isLoading,
  errorMessage,
  rows,
}: UsageTableProps) {
  return (
    <section className="card card-flush">
      <div className="card-head ai-usage-head">
        <h2>{title}</h2>
      </div>
      {isLoading ? (
        <p role="status" className="empty-state">
          {loadingText}
        </p>
      ) : errorMessage !== undefined ? (
        <p role="alert" className="alert ai-usage-alert">
          Lỗi: {errorMessage}
        </p>
      ) : rows.length === 0 ? (
        <p className="empty-state">Không có dữ liệu trong khoảng thời gian đã chọn.</p>
      ) : (
        <div className="table-wrap">
          <table className="tbl">
            <thead>
              <tr>
                <th scope="col">{firstColumn}</th>
                <th scope="col" className="num">
                  Lượt gọi
                </th>
                <th scope="col" className="num">
                  Chi phí ước tính (USD)
                </th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.label}>
                  <th scope="row" className="wrap-anywhere">
                    {row.label}
                  </th>
                  <td className="num">{row.calls.toLocaleString('vi-VN')}</td>
                  <td className="num">${row.costUsd.toFixed(4)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
