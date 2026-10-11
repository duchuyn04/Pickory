// Logo của hãng nằm trong `public/media/brands/`, có màu thương hiệu. Nguồn: gói `simple-icons` (hình và mã màu) và Wikimedia Commons (logo nhiều màu của Google, LG).
// Giá trị là tỉ lệ rộng chia cao của khung logo, đo sau khi cắt khung sát hình.
const BRAND_LOGO_ASPECT_RATIOS: Record<string, number> = {
  acer: 4.151,
  anker: 4.195,
  aoc: 3.017,
  apple: 0.815,
  asus: 4.843,
  bose: 8.493,
  dell: 1,
  gigabyte: 4.635,
  google: 1,
  jbl: 1.782,
  keychron: 4.61,
  lenovo: 2.998,
  lg: 2.182,
  logitech: 1.454,
  msi: 0.822,
  oppo: 4.207,
  razer: 0.996,
  samsung: 6.543,
  sennheiser: 1.333,
  sony: 5.682,
  xiaomi: 1,
};

// Logo dài và logo vuông chiếm diện tích gần bằng nhau. Chiều cao giảm theo căn bậc hai của tỉ lệ.
const LOGO_AREA_HEIGHT = 34;
const LOGO_MIN_HEIGHT = 14;
const LOGO_MAX_HEIGHT = 36;

export interface BrandLogo {
  src: string;
  width: number;
  height: number;
}

// Trả null khi hãng chưa có logo. Nơi gọi hiện tên hãng bằng chữ thay cho logo.
export function getBrandLogo(brandSlug: string, baseHeight = LOGO_AREA_HEIGHT): BrandLogo | null {
  const aspectRatio = BRAND_LOGO_ASPECT_RATIOS[brandSlug];
  if (aspectRatio === undefined) return null;
  const scaledHeight = baseHeight / Math.sqrt(aspectRatio);
  // Giữ chiều cao trong khoảng đọc được. Logo rất dài vẫn không mảnh như một đường kẻ.
  const height = Math.round(
    Math.min(
      Math.max(scaledHeight, (LOGO_MIN_HEIGHT * baseHeight) / LOGO_AREA_HEIGHT),
      LOGO_MAX_HEIGHT,
    ),
  );
  return {
    src: `/media/brands/${brandSlug}.svg`,
    width: Math.round(height * aspectRatio),
    height,
  };
}
