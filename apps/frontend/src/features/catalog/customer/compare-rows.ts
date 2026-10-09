import type { CompareAttributeGroup, ComparedProduct, ProductSpecEntry } from './catalog-api';
import { formatSpecValue } from './spec-format';

export interface CompareRow {
  key: string;
  label: string;
  // Một ô cho mỗi sản phẩm, theo thứ tự cột. Null khi sản phẩm chưa nhập thông số này.
  cells: (string | null)[];
  differs: boolean;
}

export interface CompareGroup {
  name: string;
  rows: CompareRow[];
}

// Một dòng khác nhau khi các ô đã format không giống hệt nhau. Null khác mọi giá trị có thật.
function cellsDiffer(cells: (string | null)[]): boolean {
  return new Set(cells).size > 1;
}

export function buildCompareGroups(
  attributeGroups: CompareAttributeGroup[],
  products: ComparedProduct[],
): CompareGroup[] {
  const groups: CompareGroup[] = [];
  for (const attributeGroup of attributeGroups) {
    const rows: CompareRow[] = [];
    for (const attribute of attributeGroup.attributes) {
      const cells = products.map((product) => {
        const value = product.specs[attribute.key] ?? null;
        if (value === null) return null;
        const entry: ProductSpecEntry = {
          key: attribute.key,
          label: attribute.label,
          group: attribute.group,
          type: attribute.type,
          unit: attribute.unit,
          value,
        };
        return formatSpecValue(entry);
      });
      // Dòng mà mọi sản phẩm đều chưa nhập không cho khách thông tin nào, nên bỏ.
      if (cells.every((cell) => cell === null)) continue;
      rows.push({
        key: attribute.key,
        label: attribute.label,
        cells,
        differs: cellsDiffer(cells),
      });
    }
    // Nhóm không còn dòng nào thì ẩn, để bảng không có tiêu đề nhóm trơ trọi.
    if (rows.length > 0) groups.push({ name: attributeGroup.name, rows });
  }
  return groups;
}

// Id đã gửi lên mà API không trả về: sản phẩm bị ẩn hoặc đã xóa.
export function findMissingIds(requestedIds: string[], products: ComparedProduct[]): string[] {
  const returnedIds = new Set(products.map((product) => product.summary.id));
  return requestedIds.filter((id) => !returnedIds.has(id));
}
