import type { ProductSpecEntry } from './catalog-api';

export interface SpecRow {
  key: string;
  label: string;
  value: string;
}

export interface SpecGroup {
  name: string;
  rows: SpecRow[];
}

export function formatSpecValue(spec: ProductSpecEntry): string {
  // Giá trị boolean hiện chữ để khách không phải đoán `true` hay `false`.
  if (typeof spec.value === 'boolean') return spec.value ? 'Có' : 'Không';

  // Số theo định dạng vi-VN (dấu phẩy thập phân). Chuỗi giữ nguyên.
  const text = typeof spec.value === 'number' ? spec.value.toLocaleString('vi-VN') : spec.value;
  // Đơn vị đi sau giá trị, ví dụ "16 GB". Thông số không có đơn vị thì chỉ hiện giá trị.
  return spec.unit ? `${text} ${spec.unit}` : String(text);
}

// Chia nhóm theo `group`, giữ thứ tự xuất hiện trong `specs` do shop quản trị sắp xếp.
export function groupSpecs(specs: ProductSpecEntry[]): SpecGroup[] {
  const groups = new Map<string, SpecRow[]>();
  for (const spec of specs) {
    // Dòng chưa có giá trị bị bỏ để bảng không có ô trống. Giá trị `false` vẫn là dữ liệu hợp lệ.
    if (spec.value === null) continue;
    const rows = groups.get(spec.group) ?? [];
    rows.push({ key: spec.key, label: spec.label, value: formatSpecValue(spec) });
    groups.set(spec.group, rows);
  }
  // Nhóm chỉ được tạo khi có dòng, nên nhóm rỗng tự biến mất.
  return [...groups.entries()].map(([name, rows]) => ({ name, rows }));
}
