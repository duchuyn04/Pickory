import type { IconName } from '../../shared/icons';
import { COMPARE_LIST_LIMIT } from '../catalog/customer/compare-list';

// Giới hạn năm danh mục gốc ở khối đầu trang và phần thân để giữ bố cục cân đối với thiết kế.
export const HERO_CATEGORY_LIMIT = 5;

// Đường dẫn chuẩn của các danh mục gốc dùng lại trên nhiều khối của trang chủ.
export const CATEGORY_SLUGS = {
  phone: 'dien-thoai',
  laptop: 'laptop',
  monitor: 'man-hinh',
  headphones: 'tai-nghe',
  accessory: 'phu-kien',
} as const;

// Icon đại diện của một danh mục gốc trên menu bên trái trang chủ.
export interface CategoryVisual {
  icon: IconName;
}

// Danh mục do quản trị viên tạo sau này chưa có icon riêng vẫn dùng icon hộp hàng mặc định.
const FALLBACK_CATEGORY_VISUAL: CategoryVisual = {
  icon: 'package',
};

const CATEGORY_VISUALS: Record<string, CategoryVisual> = {
  [CATEGORY_SLUGS.phone]: { icon: 'smartphone' },
  [CATEGORY_SLUGS.laptop]: { icon: 'laptop' },
  [CATEGORY_SLUGS.monitor]: { icon: 'monitor' },
  [CATEGORY_SLUGS.headphones]: { icon: 'headphones' },
  [CATEGORY_SLUGS.accessory]: { icon: 'plug' },
};

export function getCategoryVisual(categorySlug: string): CategoryVisual {
  return CATEGORY_VISUALS[categorySlug] ?? FALLBACK_CATEGORY_VISUAL;
}

export type SmallBannerTheme = 'dark' | 'warm' | 'cool';

export interface SmallBannerContent {
  id: string;
  theme: SmallBannerTheme;
  label: string;
  title: string;
  description: string;
  to: string;
  // Danh mục nguồn để kiểm tra tồn tại và lấy ảnh sản phẩm thật.
  categorySlug: string;
  preferredProductSlug: string | null;
  // Banner dẫn tới trang danh mục phải ẩn khi danh mục tương ứng không còn trong cây.
  requiresCategory: boolean;
}

// Ba banner nhỏ dưới banner nổi bật theo thiết kế trang chủ.
export const SMALL_BANNERS: SmallBannerContent[] = [
  {
    id: 'laptop',
    theme: 'dark',
    label: 'Laptop',
    title: 'Gaming và đồ họa',
    description: 'Cấu hình mạnh',
    to: `/c/${CATEGORY_SLUGS.laptop}`,
    categorySlug: CATEGORY_SLUGS.laptop,
    preferredProductSlug: 'asus-rog-zephyrus-g14-ga403',
    requiresCategory: true,
  },
  {
    id: 'tai-nghe',
    theme: 'warm',
    label: 'Tai nghe',
    title: 'Chống ồn chủ động',
    description: 'Đeo êm cả ngày',
    to: `/c/${CATEGORY_SLUGS.headphones}`,
    categorySlug: CATEGORY_SLUGS.headphones,
    preferredProductSlug: 'sony-wh-1000xm5',
    requiresCategory: true,
  },
  {
    id: 'so-sanh',
    theme: 'cool',
    label: 'So sánh',
    title: 'So sánh sản phẩm',
    description: `Tối đa ${COMPARE_LIST_LIMIT} máy`,
    to: '/compare',
    categorySlug: CATEGORY_SLUGS.phone,
    preferredProductSlug: 'apple-iphone-16-pro',
    requiresCategory: false,
  },
];
