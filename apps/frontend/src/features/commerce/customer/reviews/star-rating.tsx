// Nét sao lấy từ bộ Lucide (giấy phép ISC), cùng bộ với `shared/icons.tsx`.
const STAR_PATH =
  'M11.525 2.295a.53.53 0 0 1 .95 0l2.31 4.679a2.123 2.123 0 0 0 1.595 1.16l5.166.756a.53.53 0 0 1 .294.904l-3.736 3.638a2.123 2.123 0 0 0-.611 1.878l.882 5.14a.53.53 0 0 1-.771.56l-4.618-2.428a2.122 2.122 0 0 0-1.973 0L6.396 21.01a.53.53 0 0 1-.77-.56l.881-5.139a2.122 2.122 0 0 0-.611-1.879L2.16 9.795a.53.53 0 0 1 .294-.906l5.165-.755a2.122 2.122 0 0 0 1.597-1.16z';

const STAR_COUNT = 5;

// Một ngôi sao trang trí. Nghĩa của hàng sao nằm ở tên truy cập của phần tử cha.
export function StarGlyph({ isFilled }: { isFilled: boolean }) {
  return (
    <svg
      className={isFilled ? 'star-glyph star-glyph-filled' : 'star-glyph'}
      viewBox="0 0 24 24"
      aria-hidden="true"
      focusable="false"
    >
      <path d={STAR_PATH} />
    </svg>
  );
}

interface StarRatingProps {
  rating: number;
  // Tên truy cập thay cho câu mặc định, dùng khi số sao hiện ra đã là kết quả làm tròn của một điểm khác.
  label?: string;
}

// Hàng năm sao chỉ để xem. Trình đọc màn hình đọc cả hàng thành một câu, ví dụ "4 trên 5 sao".
export function StarRating({ rating, label }: StarRatingProps) {
  return (
    <span
      className="star-rating"
      role="img"
      aria-label={label ?? `${rating} trên ${STAR_COUNT} sao`}
    >
      {Array.from({ length: STAR_COUNT }, (_, index) => (
        <StarGlyph key={index} isFilled={index < rating} />
      ))}
    </span>
  );
}
