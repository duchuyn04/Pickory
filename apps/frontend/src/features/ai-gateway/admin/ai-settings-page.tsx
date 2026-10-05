import { useQuery, useQueryClient } from '@tanstack/react-query';
import React, { useEffect, useState } from 'react';
import { apiRequest } from '../../../shared/api';
import { useCan } from '../../identity/hooks/use-actor';

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
    return (
      <div
        style={{
          padding: 'var(--space-6)',
          backgroundColor: 'var(--color-surface)',
          borderRadius: 'var(--radius-card)',
          border: '1px solid var(--color-border)',
        }}
      >
        <h2 style={{ color: 'var(--color-danger)', margin: '0 0 var(--space-2) 0' }}>
          403 - Không có quyền truy cập
        </h2>
        <p style={{ color: 'var(--color-ink-muted)', margin: 0 }}>
          Bạn cần quyền <code>ai-usage:read</code> để xem thống kê và cấu hình AI.
        </p>
      </div>
    );
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

  const cardStyle: React.CSSProperties = {
    backgroundColor: 'var(--color-surface)',
    border: '1px solid var(--color-border)',
    borderRadius: 'var(--radius-card)',
    padding: 'var(--space-4)',
  };

  const inputStyle: React.CSSProperties = {
    width: '100%',
    boxSizing: 'border-box',
    padding: 'var(--space-2) var(--space-3)',
    borderRadius: 'var(--radius-control)',
    border: '1px solid var(--color-border)',
    fontSize: 'var(--text-sm)',
    backgroundColor:
      canManageSettings && !isSettingsError ? 'var(--color-surface)' : 'var(--color-surface-muted)',
  };

  const isSaveDisabled = !canManageSettings || isSubmitting || isSettingsLoading || isSettingsError;

  const buttonStyle: React.CSSProperties = {
    padding: 'var(--space-2) var(--space-4)',
    backgroundColor: 'var(--color-brand)',
    color: '#ffffff',
    border: 'none',
    borderRadius: 'var(--radius-control)',
    fontSize: 'var(--text-sm)',
    fontWeight: 600,
    cursor: isSaveDisabled ? 'not-allowed' : 'pointer',
    opacity: isSaveDisabled ? 0.6 : 1,
  };

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--space-5)',
        fontFamily: 'var(--font-sans)',
        minWidth: 0,
      }}
    >
      <div>
        <h1
          style={{
            fontSize: 'var(--text-xl)',
            fontWeight: 700,
            color: 'var(--color-ink)',
            margin: '0 0 var(--space-1) 0',
          }}
        >
          Cấu hình và sử dụng AI
        </h1>
        <p style={{ fontSize: 'var(--text-sm)', color: 'var(--color-ink-muted)', margin: 0 }}>
          Theo dõi chi phí mô hình (ước tính cao điểm bảo thủ), độ trễ p95, tỉ lệ lỗi và điều chỉnh
          hạn mức.
        </p>
      </div>

      {/* Cảnh báo ngân sách khi chi tiêu tháng hiện tại vượt 80% */}
      {usage?.budgetWarning && (
        <div
          role="alert"
          style={{
            backgroundColor: 'var(--color-sand-soft)',
            border: '1px solid var(--color-warning)',
            borderRadius: 'var(--radius-card)',
            padding: 'var(--space-3) var(--space-4)',
            color: 'var(--color-warning)',
          }}
        >
          <strong>Cảnh báo ngân sách tháng:</strong> Chi tiêu AI trong tháng đã chạm hoặc vượt 80%
          ngân sách cấu hình.
        </div>
      )}

      {/* Thẻ thống kê tổng quan KPI */}
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(min(180px, 100%), 1fr))',
          gap: 'var(--space-3)',
        }}
      >
        <div style={cardStyle}>
          <div style={{ fontSize: 'var(--text-xs)', color: 'var(--color-ink-muted)' }}>
            Chi phí ước tính (USD)
          </div>
          <div
            style={{
              fontSize: 'var(--text-xl)',
              fontWeight: 700,
              color: 'var(--color-brand)',
              marginTop: 'var(--space-1)',
            }}
          >
            {isUsageError ? '-' : isUsageLoading ? '...' : `$${(usage?.costUsd ?? 0).toFixed(4)}`}
          </div>
        </div>
        <div style={cardStyle}>
          <div style={{ fontSize: 'var(--text-xs)', color: 'var(--color-ink-muted)' }}>
            Tổng lượt gọi
          </div>
          <div
            style={{
              fontSize: 'var(--text-xl)',
              fontWeight: 700,
              color: 'var(--color-ink)',
              marginTop: 'var(--space-1)',
            }}
          >
            {isUsageError
              ? '-'
              : isUsageLoading
                ? '...'
                : (usage?.calls ?? 0).toLocaleString('vi-VN')}
          </div>
        </div>
        <div style={cardStyle}>
          <div style={{ fontSize: 'var(--text-xs)', color: 'var(--color-ink-muted)' }}>
            Tỉ lệ lỗi
          </div>
          <div
            style={{
              fontSize: 'var(--text-xl)',
              fontWeight: 700,
              color: isUsageError
                ? 'var(--color-danger)'
                : (usage?.errorRate ?? 0) > 0.05
                  ? 'var(--color-danger)'
                  : 'var(--color-success)',
              marginTop: 'var(--space-1)',
            }}
          >
            {isUsageError
              ? '-'
              : isUsageLoading
                ? '...'
                : `${((usage?.errorRate ?? 0) * 100).toFixed(1)}%`}
          </div>
        </div>
        <div style={cardStyle}>
          <div style={{ fontSize: 'var(--text-xs)', color: 'var(--color-ink-muted)' }}>
            Độ trễ p95
          </div>
          <div
            style={{
              fontSize: 'var(--text-xl)',
              fontWeight: 700,
              color: 'var(--color-ink)',
              marginTop: 'var(--space-1)',
            }}
          >
            {isUsageError ? '-' : isUsageLoading ? '...' : `${usage?.p95LatencyMs ?? 0} ms`}
          </div>
        </div>
      </div>

      {/* Biểu mẫu chỉnh sửa ngân sách và hạn mức */}
      <div style={cardStyle}>
        <h2
          style={{
            fontSize: 'var(--text-md)',
            fontWeight: 700,
            color: 'var(--color-ink)',
            margin: '0 0 var(--space-2) 0',
          }}
        >
          Ngân sách và hạn mức chat
        </h2>
        <p
          style={{
            fontSize: 'var(--text-xs)',
            color: 'var(--color-ink-muted)',
            margin: '0 0 var(--space-3) 0',
          }}
        >
          {canManageSettings
            ? 'Thay đổi ngân sách và hạn mức sẽ có hiệu lực ngay ở lượt gọi tiếp theo.'
            : 'Chế độ chỉ xem (ai-usage:read). Cần quyền ai-settings:manage để thay đổi.'}
        </p>

        {/* Thông báo lỗi khi tải cấu hình thất bại */}
        {isSettingsError && (
          <div
            role="alert"
            style={{
              padding: 'var(--space-2) var(--space-3)',
              borderRadius: 'var(--radius-control)',
              fontSize: 'var(--text-sm)',
              marginBottom: 'var(--space-3)',
              backgroundColor: '#fee4e2',
              color: 'var(--color-danger)',
              border: '1px solid var(--color-danger)',
            }}
          >
            Lỗi tải cài đặt:{' '}
            {(settingsError as Error)?.message || 'Không thể tải cấu hình AI từ máy chủ.'}
          </div>
        )}

        {feedback && (
          <div
            role="alert"
            style={{
              padding: 'var(--space-2) var(--space-3)',
              borderRadius: 'var(--radius-control)',
              fontSize: 'var(--text-sm)',
              marginBottom: 'var(--space-3)',
              backgroundColor: feedback.isError ? '#fee4e2' : 'var(--color-surface-muted)',
              color: feedback.isError ? 'var(--color-danger)' : 'var(--color-success)',
              border: `1px solid ${feedback.isError ? 'var(--color-danger)' : 'var(--color-success)'}`,
            }}
          >
            {feedback.text}
          </div>
        )}

        {isSettingsLoading ? (
          <div>Đang tải cài đặt...</div>
        ) : (
          <form noValidate onSubmit={handleSaveSettings}>
            <div
              style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(auto-fit, minmax(min(220px, 100%), 1fr))',
                gap: 'var(--space-3)',
                marginBottom: 'var(--space-3)',
              }}
            >
              <div>
                <label
                  htmlFor="ai-budget-usd"
                  style={{
                    display: 'block',
                    fontSize: 'var(--text-xs)',
                    fontWeight: 600,
                    color: 'var(--color-ink)',
                    marginBottom: 'var(--space-1)',
                  }}
                >
                  Ngân sách tháng (USD)
                </label>
                <input
                  id="ai-budget-usd"
                  type="number"
                  step="0.01"
                  min="0"
                  disabled={!canManageSettings || isSubmitting || isSettingsError}
                  value={budgetInput}
                  onChange={(e) => setBudgetInput(e.target.value)}
                  style={inputStyle}
                />
              </div>
              <div>
                <label
                  htmlFor="ai-limit-user"
                  style={{
                    display: 'block',
                    fontSize: 'var(--text-xs)',
                    fontWeight: 600,
                    color: 'var(--color-ink)',
                    marginBottom: 'var(--space-1)',
                  }}
                >
                  Hạn mức chat ngày (User)
                </label>
                <input
                  id="ai-limit-user"
                  type="number"
                  step="1"
                  min="1"
                  disabled={!canManageSettings || isSubmitting || isSettingsError}
                  value={userLimitInput}
                  onChange={(e) => setUserLimitInput(e.target.value)}
                  style={inputStyle}
                />
              </div>
              <div>
                <label
                  htmlFor="ai-limit-anon"
                  style={{
                    display: 'block',
                    fontSize: 'var(--text-xs)',
                    fontWeight: 600,
                    color: 'var(--color-ink)',
                    marginBottom: 'var(--space-1)',
                  }}
                >
                  Hạn mức chat ngày (Khách)
                </label>
                <input
                  id="ai-limit-anon"
                  type="number"
                  step="1"
                  min="1"
                  disabled={!canManageSettings || isSubmitting || isSettingsError}
                  value={anonLimitInput}
                  onChange={(e) => setAnonLimitInput(e.target.value)}
                  style={inputStyle}
                />
              </div>
            </div>

            {canManageSettings && (
              <button type="submit" disabled={isSaveDisabled} style={buttonStyle}>
                {isSubmitting ? 'Đang lưu...' : 'Lưu cấu hình'}
              </button>
            )}
          </form>
        )}
      </div>

      {/* Bộ lọc khoảng ngày theo giờ Việt Nam */}
      <form
        noValidate
        onSubmit={handleFilterSubmit}
        style={{
          display: 'flex',
          flexWrap: 'wrap',
          gap: 'var(--space-3)',
          alignItems: 'center',
          backgroundColor: 'var(--color-surface)',
          padding: 'var(--space-3) var(--space-4)',
          borderRadius: 'var(--radius-card)',
          border: '1px solid var(--color-border)',
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-2)' }}>
          <label
            htmlFor="ai-filter-start"
            style={{ fontSize: 'var(--text-xs)', color: 'var(--color-ink-muted)' }}
          >
            Từ ngày:
          </label>
          <input
            id="ai-filter-start"
            type="date"
            value={filterStartDate}
            onChange={(e) => setFilterStartDate(e.target.value)}
            style={{
              padding: 'var(--space-1) var(--space-2)',
              borderRadius: 'var(--radius-control)',
              border: '1px solid var(--color-border)',
              fontSize: 'var(--text-sm)',
            }}
          />
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-2)' }}>
          <label
            htmlFor="ai-filter-end"
            style={{ fontSize: 'var(--text-xs)', color: 'var(--color-ink-muted)' }}
          >
            Đến ngày:
          </label>
          <input
            id="ai-filter-end"
            type="date"
            value={filterEndDate}
            onChange={(e) => setFilterEndDate(e.target.value)}
            style={{
              padding: 'var(--space-1) var(--space-2)',
              borderRadius: 'var(--radius-control)',
              border: '1px solid var(--color-border)',
              fontSize: 'var(--text-sm)',
            }}
          />
        </div>
        <button
          type="submit"
          disabled={isSubmitting}
          style={{
            ...buttonStyle,
            cursor: isSubmitting ? 'not-allowed' : 'pointer',
            opacity: isSubmitting ? 0.6 : 1,
          }}
        >
          Lọc
        </button>
        <button
          type="button"
          onClick={handleResetFilter}
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

      {/* Bảng chi phí phân rã theo ngày và theo tính năng */}
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(min(320px, 100%), 1fr))',
          gap: 'var(--space-4)',
        }}
      >
        {/* Bảng theo ngày */}
        <div style={cardStyle}>
          <h2
            style={{
              fontSize: 'var(--text-md)',
              fontWeight: 700,
              color: 'var(--color-ink)',
              margin: '0 0 var(--space-2) 0',
            }}
          >
            Chi phí theo ngày
          </h2>
          {isUsageLoading ? (
            <div>Đang nạp dữ liệu theo ngày...</div>
          ) : isUsageError ? (
            <div style={{ color: 'var(--color-danger)' }}>
              Lỗi: {(usageError as Error)?.message}
            </div>
          ) : !usage?.byDay || usage.byDay.length === 0 ? (
            <div style={{ color: 'var(--color-ink-muted)', fontSize: 'var(--text-sm)' }}>
              Không có dữ liệu trong khoảng thời gian đã chọn.
            </div>
          ) : (
            <div style={{ overflowX: 'auto', width: '100%' }}>
              <table
                style={{
                  width: '100%',
                  minWidth: '280px',
                  borderCollapse: 'collapse',
                  fontSize: 'var(--text-sm)',
                }}
              >
                <thead>
                  <tr style={{ borderBottom: '1px solid var(--color-border)', textAlign: 'left' }}>
                    <th
                      scope="col"
                      style={{ padding: 'var(--space-2)', color: 'var(--color-ink-muted)' }}
                    >
                      Ngày
                    </th>
                    <th
                      scope="col"
                      style={{ padding: 'var(--space-2)', color: 'var(--color-ink-muted)' }}
                    >
                      Lượt gọi
                    </th>
                    <th
                      scope="col"
                      style={{ padding: 'var(--space-2)', color: 'var(--color-ink-muted)' }}
                    >
                      Chi phí ước tính (USD)
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {usage.byDay.map((row) => (
                    <tr key={row.day} style={{ borderBottom: '1px solid var(--color-border)' }}>
                      <td style={{ padding: 'var(--space-2)', color: 'var(--color-ink)' }}>
                        {row.day}
                      </td>
                      <td style={{ padding: 'var(--space-2)', color: 'var(--color-ink)' }}>
                        {row.calls.toLocaleString('vi-VN')}
                      </td>
                      <td style={{ padding: 'var(--space-2)', color: 'var(--color-brand)' }}>
                        ${row.costUsd.toFixed(4)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>

        {/* Bảng theo tính năng */}
        <div style={cardStyle}>
          <h2
            style={{
              fontSize: 'var(--text-md)',
              fontWeight: 700,
              color: 'var(--color-ink)',
              margin: '0 0 var(--space-2) 0',
            }}
          >
            Chi phí theo tính năng
          </h2>
          {isUsageLoading ? (
            <div>Đang nạp dữ liệu theo tính năng...</div>
          ) : isUsageError ? (
            <div style={{ color: 'var(--color-danger)' }}>
              Lỗi: {(usageError as Error)?.message}
            </div>
          ) : !usage?.byFeature || usage.byFeature.length === 0 ? (
            <div style={{ color: 'var(--color-ink-muted)', fontSize: 'var(--text-sm)' }}>
              Không có dữ liệu trong khoảng thời gian đã chọn.
            </div>
          ) : (
            <div style={{ overflowX: 'auto', width: '100%' }}>
              <table
                style={{
                  width: '100%',
                  minWidth: '280px',
                  borderCollapse: 'collapse',
                  fontSize: 'var(--text-sm)',
                }}
              >
                <thead>
                  <tr style={{ borderBottom: '1px solid var(--color-border)', textAlign: 'left' }}>
                    <th
                      scope="col"
                      style={{ padding: 'var(--space-2)', color: 'var(--color-ink-muted)' }}
                    >
                      Tính năng
                    </th>
                    <th
                      scope="col"
                      style={{ padding: 'var(--space-2)', color: 'var(--color-ink-muted)' }}
                    >
                      Lượt gọi
                    </th>
                    <th
                      scope="col"
                      style={{ padding: 'var(--space-2)', color: 'var(--color-ink-muted)' }}
                    >
                      Chi phí ước tính (USD)
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {usage.byFeature.map((row) => (
                    <tr key={row.feature} style={{ borderBottom: '1px solid var(--color-border)' }}>
                      <td
                        style={{
                          padding: 'var(--space-2)',
                          color: 'var(--color-ink)',
                          fontFamily: 'monospace',
                          fontSize: 'var(--text-xs)',
                        }}
                      >
                        {row.feature}
                      </td>
                      <td style={{ padding: 'var(--space-2)', color: 'var(--color-ink)' }}>
                        {row.calls.toLocaleString('vi-VN')}
                      </td>
                      <td style={{ padding: 'var(--space-2)', color: 'var(--color-brand)' }}>
                        ${row.costUsd.toFixed(4)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
