// @vitest-environment jsdom
import { act, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  ReportBarChart,
  type ReportBarPoint,
} from '../../../../../src/features/admin-console/reports/report-bar-chart';
import {
  ReportFunnelChart,
  type ReportFunnelPoint,
} from '../../../../../src/features/admin-console/reports/report-funnel-chart';
import {
  ReportPieChart,
  type ReportPieSlice,
} from '../../../../../src/features/admin-console/reports/report-pie-chart';

interface Rendered {
  container: HTMLDivElement;
  root: Root;
}

let rendered: Rendered | null = null;

beforeEach(() => {
  // React cần cờ này để `act` áp dụng đúng môi trường test.
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  const container = document.createElement('div');
  document.body.append(container);
  rendered = { container, root: createRoot(container) };
});

afterEach(async () => {
  if (rendered) {
    await act(async () => rendered?.root.unmount());
    rendered.container.remove();
    rendered = null;
  }
  vi.unstubAllGlobals();
});

async function render(element: ReactNode): Promise<HTMLDivElement> {
  if (!rendered) throw new Error('Thiếu khung dựng test.');
  await act(async () => rendered?.root.render(element));
  return rendered.container;
}

const slices: ReportPieSlice[] = [
  { key: 'a', label: 'Hãng Một', value: 600 },
  { key: 'b', label: 'Hãng Hai', value: 300 },
  { key: 'other', label: 'Khác', value: 100 },
];

describe('biểu đồ tròn', () => {
  it('vẽ một lát cho mỗi nhóm, có nhãn phần trăm, chú giải và bảng số liệu', async () => {
    const container = await render(
      <ReportPieChart
        chartLabel="Doanh thu theo thương hiệu tháng 10/2026"
        slices={slices}
        neutralKey="other"
        tableCaption="Bảng doanh thu theo thương hiệu"
        tableHead={
          <>
            <th scope="col">Thương hiệu</th>
            <th scope="col">Doanh thu</th>
            <th scope="col">Tỷ lệ</th>
          </>
        }
        renderRow={(slice, percent) => (
          <tr key={slice.key}>
            <th scope="row">{slice.label}</th>
            <td>{slice.value}</td>
            <td>{percent}</td>
          </tr>
        )}
        width={600}
        height={320}
      />,
    );

    expect(container.querySelectorAll('.recharts-pie-sector path')).toHaveLength(3);
    // Nhãn phần trăm nằm ngay trên lát, không chỉ trong bảng hay chú giải.
    expect(
      Array.from(container.querySelectorAll('.report-chart-label')).map(
        (label) => label.textContent,
      ),
    ).toEqual(['60%', '30%', '10%']);
    // Lát không nhận focus vì trang không mở chi tiết từ lát.
    expect(container.querySelectorAll('path[role="button"]')).toHaveLength(0);
    expect(container.querySelectorAll('.report-legend-item')).toHaveLength(3);
    expect(container.querySelectorAll('.report-chart-legend button')).toHaveLength(0);
    expect(container.querySelector('.report-table caption')?.textContent).toBe(
      'Bảng doanh thu theo thương hiệu',
    );
    expect(container.querySelectorAll('.report-table tbody tr')).toHaveLength(3);
  });
});

const points: ReportBarPoint[] = [
  { key: '2026-05', label: 'Tháng 5/2026', tick: '5/26', value: 1_000 },
  { key: '2026-10', label: 'Tháng 10/2026', tick: '10/26', value: 3_000 },
];

describe('biểu đồ cột', () => {
  it('vẽ một cột cho mỗi kỳ và bảng số liệu bên dưới', async () => {
    const container = await render(
      <ReportBarChart
        chartLabel="Doanh thu theo kỳ"
        points={points}
        formatAxisValue={(value) => `${value}`}
        tableCaption="Bảng doanh thu theo kỳ"
        tableHead={
          <>
            <th scope="col">Kỳ</th>
            <th scope="col">Doanh thu</th>
          </>
        }
        renderRow={(point) => (
          <tr key={point.key}>
            <th scope="row">{point.label}</th>
            <td>{point.value}</td>
          </tr>
        )}
        width={600}
        height={320}
      />,
    );

    expect(container.querySelectorAll('.recharts-bar-rectangle path')).toHaveLength(2);
    // Cột không nhận focus vì trang không mở chi tiết từ cột.
    expect(container.querySelectorAll('path[role="button"]')).toHaveLength(0);
    expect(container.querySelectorAll('.report-table tbody tr')).toHaveLength(2);
    expect(container.querySelector('.report-table caption')?.textContent).toBe(
      'Bảng doanh thu theo kỳ',
    );
  });
});

const funnelPoints: ReportFunnelPoint[] = [
  { key: 'product_view', label: 'Xem sản phẩm', count: 1_200, rateLabel: 'Bước đầu' },
  { key: 'add_to_cart', label: 'Thêm giỏ', count: 300, rateLabel: '25%' },
  { key: 'order_placed', label: 'Đặt đơn', count: 60, rateLabel: '20%' },
];

describe('biểu đồ phễu', () => {
  it('vẽ một thanh cho mỗi bước và bảng lượt, tỉ lệ bên dưới', async () => {
    const container = await render(
      <ReportFunnelChart
        chartLabel="Phễu chuyển đổi từ 01/05/2026 đến 15/10/2026"
        points={funnelPoints}
        tableCaption="Bảng phễu chuyển đổi từ 01/05/2026 đến 15/10/2026"
        width={600}
        height={320}
      />,
    );

    expect(container.querySelectorAll('.recharts-bar-rectangle path')).toHaveLength(3);
    // Biểu đồ chỉ trình bày số liệu, nên thanh không nhận focus.
    expect(container.querySelectorAll('path[role="button"]')).toHaveLength(0);

    const rows = Array.from(container.querySelectorAll('.report-table tbody tr'));
    expect(rows).toHaveLength(3);
    // Số lượt và tỉ lệ nằm trong bảng, để đọc được khi không nhìn màu hay độ dài thanh.
    expect(rows.map((row) => row.textContent)).toEqual([
      'Xem sản phẩm1.200Bước đầu',
      'Thêm giỏ30025%',
      'Đặt đơn6020%',
    ]);
    expect(container.querySelector('.report-table caption')?.textContent).toBe(
      'Bảng phễu chuyển đổi từ 01/05/2026 đến 15/10/2026',
    );
  });
});
