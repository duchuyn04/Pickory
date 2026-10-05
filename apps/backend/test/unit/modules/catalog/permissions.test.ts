import { describe, expect, it } from 'vitest';
import { catalogPermissions } from '../../../../src/modules/catalog';
import { PERMISSION_CATALOG } from '../../../../src/modules/identity';

describe('catalogPermissions', () => {
  it('khớp chính xác với danh mục quyền thuộc nhóm catalog trong identity', () => {
    // Lọc toàn bộ quyền thuộc nhóm catalog trong danh mục định nghĩa của identity.
    const identityCatalogPermissions = PERMISSION_CATALOG.filter(
      (permission) => permission.group === 'catalog',
    ).map((permission) => permission.id);

    // Xác nhận danh sách quyền của catalog không bị lệch so với danh mục của identity.
    expect([...catalogPermissions]).toEqual(identityCatalogPermissions);
  });
});
