import type { IconName } from '../../shared/icons';

// Hình minh họa và icon của một danh mục gốc trên trang chủ.
export interface CategoryVisual {
  icon: IconName;
  // Đường dẫn trong `public/`. Hình lấy từ bản mẫu `shop-retail/assets/p/`, không có chữ hay logo hãng.
  artSrc: string | null;
}

// Danh mục do admin tạo sau này chưa có hình riêng. Trang vẫn hiện icon chung thay vì ô trống.
const FALLBACK_CATEGORY_VISUAL: CategoryVisual = { icon: 'package', artSrc: null };

const CATEGORY_VISUALS: Record<string, CategoryVisual> = {
  'dien-thoai': { icon: 'smartphone', artSrc: '/media/categories/dien-thoai.svg' },
  laptop: { icon: 'laptop', artSrc: '/media/categories/laptop.svg' },
  'man-hinh': { icon: 'monitor', artSrc: '/media/categories/man-hinh.svg' },
  'tai-nghe': { icon: 'headphones', artSrc: '/media/categories/tai-nghe.svg' },
  'phu-kien': { icon: 'plug', artSrc: '/media/categories/phu-kien.svg' },
};

export function getCategoryVisual(categorySlug: string): CategoryVisual {
  return CATEGORY_VISUALS[categorySlug] ?? FALLBACK_CATEGORY_VISUAL;
}

// Nền của banner. Hai tông xen kẽ để khách nhận ra banner đã đổi.
export type BannerTone = 'deep' | 'dark';

export interface HeroSlideContent {
  categorySlug: string;
  // Nhãn ngắn trên tab chọn banner.
  tabLabel: string;
  title: string;
  text: string;
  ctaLabel: string;
  tone: BannerTone;
}

// Câu chữ chỉ nói điều catalog có thật. Chưa có module khuyến mãi, nên banner không hứa giảm giá hay trả góp.
export const HERO_SLIDES: HeroSlideContent[] = [
  {
    categorySlug: 'dien-thoai',
    tabLabel: 'Điện thoại',
    title: 'Điện thoại chính hãng',
    text: 'Nhiều hãng và nhiều tầm giá. So sánh cấu hình trước khi chọn máy.',
    ctaLabel: 'Xem điện thoại',
    tone: 'deep',
  },
  {
    categorySlug: 'laptop',
    tabLabel: 'Laptop',
    title: 'Laptop cho học tập và công việc',
    text: 'Lọc theo vi xử lý, RAM và màn hình để tìm máy vừa nhu cầu.',
    ctaLabel: 'Xem laptop',
    tone: 'dark',
  },
  {
    categorySlug: 'tai-nghe',
    tabLabel: 'Tai nghe',
    title: 'Tai nghe cho mọi nhu cầu',
    text: 'Lọc theo kiểu tai nghe, chống ồn chủ động và thời lượng pin.',
    ctaLabel: 'Xem tai nghe',
    tone: 'deep',
  },
];

export interface SideBannerContent {
  categorySlug: string;
  title: string;
}

// Hai danh mục còn lại nằm ở cột phải, để hero giới thiệu đủ năm danh mục.
export const SIDE_BANNERS: SideBannerContent[] = [
  { categorySlug: 'man-hinh', title: 'Màn hình làm việc và chơi game' },
  { categorySlug: 'phu-kien', title: 'Chuột, bàn phím, sạc và cáp' },
];
