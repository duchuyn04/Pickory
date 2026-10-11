// Biểu tượng chữ P của Pickory, cắt từ logo gốc `pickory-logo.png`. Logo đầy đủ ở `/media/brand/pickory-logo.png`.
// Ảnh gốc 150 x 128. Khai báo cỡ thật để trình duyệt giữ chỗ đúng tỉ lệ trước khi ảnh tải xong.
const MARK_SRC = '/media/brand/pickory-mark.png';
const MARK_WIDTH = 150;
const MARK_HEIGHT = 128;

interface BrandMarkProps {
  className?: string;
}

// Chữ "Pickory" luôn đứng cạnh biểu tượng, nên ảnh để `alt` rỗng và trình đọc màn hình không đọc tên hai lần.
export function BrandMark({ className }: BrandMarkProps) {
  return (
    <img
      className={className ? `logo-mark ${className}` : 'logo-mark'}
      src={MARK_SRC}
      alt=""
      width={MARK_WIDTH}
      height={MARK_HEIGHT}
    />
  );
}
