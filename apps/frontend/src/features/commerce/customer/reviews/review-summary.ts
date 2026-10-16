import type { ReviewSummary } from './reviews-api';

const averageFormat = new Intl.NumberFormat('vi-VN', {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
});

// Mức sao thấp nhất và cao nhất mà backend nhận.
const LOWEST_STARS = 1;
const HIGHEST_STARS = 5;

export interface DistributionRow {
  stars: number;
  count: number;
  // Tỉ lệ so với tổng số đánh giá, từ 0 đến 100. Thanh phân bố dùng giá trị này làm chiều rộng.
  percent: number;
  // Tên truy cập của thanh, ví dụ "5 sao: 3 đánh giá".
  label: string;
}

// Điểm trung bình một chữ số thập phân kiểu Việt Nam, ví dụ `4,5`. Trả `null` khi chưa có đánh giá.
export function formatAverageRating(average: number | null): string | null {
  // Chưa có đánh giá thì không có điểm. Giao diện hiện trạng thái rỗng, không hiện `NaN` hay `0,0`.
  if (average === null) return null;
  return averageFormat.format(average);
}

// Năm dòng phân bố, xếp từ 5 sao xuống 1 sao.
export function buildDistributionRows(summary: ReviewSummary): DistributionRow[] {
  const rows: DistributionRow[] = [];
  for (let stars = HIGHEST_STARS; stars >= LOWEST_STARS; stars -= 1) {
    // Khóa thiếu nghĩa là chưa có đánh giá ở mức sao đó.
    const count = summary.distribution[String(stars)] ?? 0;
    // Tổng bằng 0 thì mọi thanh rỗng. Phép chia cho 0 sẽ ra `NaN` và làm hỏng chiều rộng thanh.
    const percent = summary.count > 0 ? Math.min(100, (count / summary.count) * 100) : 0;
    rows.push({ stars, count, percent, label: `${stars} sao: ${count} đánh giá` });
  }
  return rows;
}
