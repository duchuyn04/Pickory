import { formatCount, formatReportDate } from './report-format';
import type { SalesReportBucket } from './reports-api';

interface ReportDailyDrilldownProps {
  // Tiêu đề nói rõ nhóm và tháng đang xem.
  title: string;
  caption: string;
  buckets: readonly SalesReportBucket[];
  // Đọc số của nhóm đang chọn trong một ngày.
  countOf: (bucket: SalesReportBucket) => number;
}

// Bảng chi tiết theo từng ngày của nhóm đang chọn. Trang chỉ dựng khối này sau khi người dùng chọn một lát.
export function ReportDailyDrilldown({
  title,
  caption,
  buckets,
  countOf,
}: ReportDailyDrilldownProps) {
  return (
    <div className="report-drilldown">
      <h3>{title}</h3>
      <div className="table-wrap">
        <table className="tbl report-table">
          <caption>{caption}</caption>
          <thead>
            <tr>
              <th scope="col">Ngày</th>
              <th scope="col" className="num">
                Số đơn
              </th>
            </tr>
          </thead>
          <tbody>
            {buckets.map((bucket) => (
              <tr key={bucket.key}>
                <th scope="row">{formatReportDate(bucket.from)}</th>
                <td className="num">{formatCount(countOf(bucket))}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
