import React from 'react';

export interface PermissionItem {
  id: string;
  group: string;
  name: string;
  description: string;
}

const MODULE_GROUP_NAMES: Record<string, string> = {
  identity: 'Tài khoản & Phân quyền',
  catalog: 'Sản phẩm & Tồn kho',
  commerce: 'Đơn hàng & Thanh toán',
  'support-desk': 'Hỗ trợ khách hàng (CSKH)',
  shipping: 'Vận chuyển',
  'knowledge-base': 'Cơ sở tri thức',
  'ai-gateway': 'Cấu hình AI',
  'admin-console': 'Báo cáo & Tổng hợp',
};

interface PermissionGridProps {
  permissions: PermissionItem[];
  selected: string[];
  onChange: (selected: string[]) => void;
  disabled?: boolean;
}

/**
 * Lưới phân quyền trực quan theo từng phân hệ chức năng cho phép chọn nhanh từng quyền hoặc cả nhóm quyền.
 */
export function PermissionGrid({ permissions, selected, onChange, disabled }: PermissionGridProps) {
  // Phân loại danh sách quyền theo phân hệ chức năng để hiển thị trực quan và dễ quản lý trên giao diện.
  const grouped = permissions.reduce<Record<string, PermissionItem[]>>((acc, item) => {
    const list = acc[item.group] || [];
    list.push(item);
    acc[item.group] = list;
    return acc;
  }, {});

  function togglePermission(id: string) {
    if (disabled) return;
    if (selected.includes(id)) {
      onChange(selected.filter((p) => p !== id));
    } else {
      onChange([...selected, id]);
    }
  }

  // Cho phép bật hoặc tắt nhanh toàn bộ các quyền trong một phân hệ chức năng để tiết kiệm thời gian thao tác.
  function toggleGroup(groupItems: PermissionItem[]) {
    if (disabled) return;
    const groupIds = groupItems.map((i) => i.id);
    const allSelected = groupIds.every((id) => selected.includes(id));
    if (allSelected) {
      onChange(selected.filter((id) => !groupIds.includes(id)));
    } else {
      const merged = Array.from(new Set([...selected, ...groupIds]));
      onChange(merged);
    }
  }

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--space-5)',
        fontFamily: 'var(--font-sans)',
      }}
    >
      {Object.entries(grouped).map(([groupKey, items]) => {
        const groupLabel = MODULE_GROUP_NAMES[groupKey] || groupKey;
        const allSelected = items.every((i) => selected.includes(i.id));

        return (
          <div
            key={groupKey}
            style={{
              border: '1px solid var(--color-border)',
              borderRadius: 'var(--radius-control)',
              overflow: 'hidden',
              backgroundColor: 'var(--color-surface)',
            }}
          >
            <div
              style={{
                backgroundColor: 'var(--color-surface-muted)',
                padding: 'var(--space-2) var(--space-4)',
                borderBottom: '1px solid var(--color-border)',
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
              }}
            >
              <span
                style={{ fontWeight: 600, fontSize: 'var(--text-sm)', color: 'var(--color-ink)' }}
              >
                {groupLabel}
              </span>
              {!disabled && (
                <button
                  type="button"
                  onClick={() => toggleGroup(items)}
                  style={{
                    background: 'none',
                    border: 'none',
                    color: 'var(--color-brand)',
                    fontSize: 'var(--text-xs)',
                    cursor: 'pointer',
                    fontWeight: 600,
                  }}
                >
                  {allSelected ? 'Bỏ chọn nhóm' : 'Chọn tất cả nhóm'}
                </button>
              )}
            </div>

            <div
              style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(auto-fill, minmax(260px, 1fr))',
                gap: 'var(--space-3)',
                padding: 'var(--space-4)',
              }}
            >
              {items.map((item) => {
                const isChecked = selected.includes(item.id);
                return (
                  <label
                    key={item.id}
                    style={{
                      display: 'flex',
                      alignItems: 'flex-start',
                      gap: 'var(--space-2)',
                      cursor: disabled ? 'default' : 'pointer',
                      fontSize: 'var(--text-sm)',
                    }}
                  >
                    <input
                      type="checkbox"
                      disabled={disabled}
                      checked={isChecked}
                      onChange={() => togglePermission(item.id)}
                      style={{ marginTop: '2px', accentColor: 'var(--color-brand)' }}
                    />
                    <div>
                      <div style={{ fontWeight: 500, color: 'var(--color-ink)' }}>{item.name}</div>
                      <div style={{ fontSize: 'var(--text-xs)', color: 'var(--color-ink-muted)' }}>
                        {item.description}
                      </div>
                    </div>
                  </label>
                );
              })}
            </div>
          </div>
        );
      })}
    </div>
  );
}
