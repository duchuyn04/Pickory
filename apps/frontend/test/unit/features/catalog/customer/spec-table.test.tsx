// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ProductSpecEntry } from '../../../../../src/features/catalog/customer/catalog-api';
import { SpecTable } from '../../../../../src/features/catalog/customer/spec-table';

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

const specs: ProductSpecEntry[] = [
  { key: 'screen', label: 'Màn hình', group: 'Hiển thị', type: 'number', unit: 'inch', value: 14 },
  { key: 'touch', label: 'Cảm ứng', group: 'Hiển thị', type: 'boolean', unit: null, value: false },
  { key: 'ram', label: 'RAM', group: 'Cấu hình', type: 'enum', unit: 'GB', value: '16' },
  { key: 'gpu', label: 'Card đồ họa', group: 'Cấu hình', type: 'text', unit: null, value: null },
  { key: 'os', label: 'Hệ điều hành', group: 'Phần mềm', type: 'text', unit: null, value: null },
];

describe('SpecTable', () => {
  it('chia nhóm theo thứ tự xuất hiện, mỗi nhóm một bảng có tiêu đề h3', async () => {
    await act(async () => root.render(<SpecTable specs={specs} />));
    expect([...container.querySelectorAll('h3')].map((heading) => heading.textContent)).toEqual([
      'Hiển thị',
      'Cấu hình',
    ]);
    expect(container.querySelectorAll('table')).toHaveLength(2);
  });

  it('hiện giá trị đã định dạng, bỏ dòng null và ẩn nhóm rỗng', async () => {
    await act(async () => root.render(<SpecTable specs={specs} />));
    const rows = [...container.querySelectorAll('tr')].map((row) => row.textContent);
    expect(rows).toEqual(['Màn hình14 inch', 'Cảm ứngKhông', 'RAM16 GB']);
    expect(container.textContent).not.toContain('Phần mềm');
    expect(container.textContent).not.toContain('Card đồ họa');
  });

  it('ẩn cả mục khi không có dòng nào', async () => {
    await act(async () => root.render(<SpecTable specs={[]} />));
    expect(container.innerHTML).toBe('');
  });
});
