import { describe, expect, it } from 'vitest';
import type { CategoryNode } from '../../../../../src/features/catalog/customer/catalog-api';
import { findCategoryTrail } from '../../../../../src/features/catalog/customer/category-tree';

function node(id: string, slug: string, parentId: string | null, children: CategoryNode[] = []) {
  return { id, slug, parentId, name: `Danh mục ${slug}`, children };
}

const tree: CategoryNode[] = [
  node('c1', 'may-tinh', null, [node('c2', 'laptop', 'c1')]),
  node('c3', 'tai-nghe', null),
];

describe('tìm danh mục theo slug', () => {
  it('danh mục gốc trả đường dẫn một phần tử', () => {
    expect(findCategoryTrail(tree, 'tai-nghe')?.map((category) => category.id)).toEqual(['c3']);
  });

  it('danh mục con trả đường dẫn từ danh mục cha', () => {
    expect(findCategoryTrail(tree, 'laptop')?.map((category) => category.id)).toEqual(['c1', 'c2']);
  });

  it('slug lạ trả null', () => {
    expect(findCategoryTrail(tree, 'khong-co')).toBeNull();
  });
});
