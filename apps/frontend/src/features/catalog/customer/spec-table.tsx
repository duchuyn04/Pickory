import type { ProductSpecEntry } from './catalog-api';
import { groupSpecs } from './spec-format';

export function SpecTable({ specs }: { specs: ProductSpecEntry[] }) {
  const groups = groupSpecs(specs);
  // Không có dòng nào để hiện thì ẩn cả mục, để trang không có tiêu đề thông số trơ trọi.
  if (groups.length === 0) return null;

  return (
    <section className="product-info-section" aria-labelledby="product-specs-title">
      <h2 id="product-specs-title" className="product-info-title">
        Thông số kỹ thuật
      </h2>
      <div className="spec-groups">
        {groups.map((group) => (
          <div key={group.name} className="spec-group">
            <h3>{group.name}</h3>
            <table className="spec-table">
              <tbody>
                {group.rows.map((row) => (
                  <tr key={row.key}>
                    <th scope="row">{row.label}</th>
                    <td>{row.value}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ))}
      </div>
    </section>
  );
}
