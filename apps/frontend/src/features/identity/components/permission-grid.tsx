import React from 'react';

// Một quyền như server gửi qua GET /api/admin/permissions.
export interface PermissionItem {
  id: string;
  group: string;
  name: string;
  description: string;
}

// Tên nhóm tiếng Việt thân thiện cho từng module; nhóm nào chưa có tên ở đây sẽ hiện theo mã nhóm.
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
  // Component không tự giữ trạng thái; cha nhận danh sách đã chọn mới qua onChange.
  onChange: (selected: string[]) => void;
  // Dùng khi chỉ muốn xem mà không cho sửa.
  disabled?: boolean;
}

// Bảng tick quyền chia theo nhóm module, có nút chọn cả nhóm.
export function PermissionGrid({ permissions, selected, onChange, disabled }: PermissionGridProps) {
  // Gom quyền theo nhóm để vẽ từng khối riêng, dễ tìm hơn một danh sách dài hơn hai mươi quyền.
  const grouped = permissions.reduce<Record<string, PermissionItem[]>>((acc, item) => {
    const list = acc[item.group] || [];
    list.push(item);
    acc[item.group] = list;
    return acc;
  }, {});

  function togglePermission(id: string) {
    // Kiểm tra disabled ở cả hàm xử lý, không chỉ ở thuộc tính của ô tick, để chắc chắn không sửa được khi chỉ xem.
    if (disabled) return;
    if (selected.includes(id)) {
      onChange(selected.filter((p) => p !== id));
    } else {
      onChange([...selected, id]);
    }
  }

  // Chọn hoặc bỏ cả nhóm một lần, để cấp quyền cho một module không phải tick từng ô.
  function toggleGroup(groupItems: PermissionItem[]) {
    if (disabled) return;
    const groupIds = groupItems.map((i) => i.id);
    const allSelected = groupIds.every((id) => selected.includes(id));
    // Đã chọn hết cả nhóm thì bấm là bỏ hết; ngược lại (chưa chọn hoặc chọn dở) bấm là chọn nốt cho đủ.
    if (allSelected) {
      onChange(selected.filter((id) => !groupIds.includes(id)));
    } else {
      // Dùng Set để quyền đã chọn sẵn trong nhóm không bị thêm hai lần.
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
