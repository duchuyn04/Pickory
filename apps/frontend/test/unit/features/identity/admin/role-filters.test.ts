import { describe, expect, it } from 'vitest';
import { filterRoles } from '../../../../../src/features/identity/admin/role-filters';

const roles = [
  { id: '1', code: 'admin', name: 'Quản trị viên' },
  { id: '2', code: 'sales', name: 'Nhân viên bán hàng' },
  { id: '3', code: 'support', name: 'Chăm sóc khách hàng' },
];

const ids = (list: { id: string }[]) => list.map((item) => item.id);

describe('filterRoles', () => {
  it('từ khóa rỗng trả nguyên danh sách', () => {
    expect(filterRoles(roles, '')).toBe(roles);
  });

  it('khớp theo tên hiển thị không dấu', () => {
    expect(ids(filterRoles(roles, 'quan tri'))).toEqual(['1']);
  });

  it('khớp theo mã role', () => {
    expect(ids(filterRoles(roles, 'sales'))).toEqual(['2']);
  });

  it('không phân biệt hoa thường và bỏ khoảng trắng thừa', () => {
    expect(ids(filterRoles(roles, '  CHAM   SOC '))).toEqual(['3']);
  });

  it('không khớp thì trả danh sách rỗng', () => {
    expect(filterRoles(roles, 'kho')).toEqual([]);
  });
});
