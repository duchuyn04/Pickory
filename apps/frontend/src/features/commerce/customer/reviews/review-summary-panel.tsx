import { buildDistributionRows, formatAverageRating } from './review-summary';
import type { ReviewSummary } from './reviews-api';
import { StarRating } from './star-rating';

// Điểm trung bình, số đánh giá và phân bố số sao. Chưa có đánh giá thì chỉ hiện trạng thái rỗng.
export function ReviewSummaryPanel({ summary }: { summary: ReviewSummary }) {
  const averageText = formatAverageRating(summary.average);

  // Không có điểm trung bình nghĩa là chưa có đánh giá nào đang hiện. Không dựng số liệu giả.
  if (averageText === null) {
    return <p className="review-empty">Chưa có đánh giá</p>;
  }

  const rows = buildDistributionRows(summary);
  return (
    <div className="review-summary">
      <div className="review-summary-score">
        <strong className="review-average">{averageText}</strong>
        {/* Số sao tô là điểm đã làm tròn. Tên truy cập nói điểm thật, để khớp chữ hiện trên màn hình. */}
        <StarRating
          rating={Math.round(summary.average ?? 0)}
          label={`Điểm trung bình ${averageText} trên 5 sao`}
        />
        <span className="muted small">{summary.count.toLocaleString('vi-VN')} đánh giá</span>
      </div>
      <ul className="review-distribution" aria-label="Phân bố số sao">
        {rows.map((row) => (
          <li key={row.stars} className="review-distribution-row">
            <span className="review-distribution-stars">{row.stars} sao</span>
            {/* Thanh có tên truy cập riêng, vì chiều rộng thanh không có nghĩa với trình đọc màn hình. */}
            {/* Chiều rộng đặt bằng thuộc tính SVG, vì design system cấm thuộc tính `style` trong component. */}
            <svg
              className="review-distribution-bar"
              role="img"
              aria-label={row.label}
              viewBox="0 0 100 8"
              preserveAspectRatio="none"
            >
              <title>{row.label}</title>
              <rect className="review-distribution-fill" width={row.percent} height="8" />
            </svg>
            <span className="review-distribution-count">{row.count.toLocaleString('vi-VN')}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
