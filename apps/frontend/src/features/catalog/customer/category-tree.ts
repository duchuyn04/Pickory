import type { CategoryNode } from './catalog-api';

// Trả đường dẫn từ danh mục gốc tới danh mục có slug này, để vẽ breadcrumb. Trả null khi không có slug.
export function findCategoryTrail(tree: CategoryNode[], slug: string): CategoryNode[] | null {
  for (const node of tree) {
    if (node.slug === slug) return [node];
    const childTrail = findCategoryTrail(node.children, slug);
    // Danh mục con nằm sau danh mục cha trong đường dẫn.
    if (childTrail) return [node, ...childTrail];
  }
  return null;
}
