import type { IconName } from '../../shared/icons';
import { COMPARE_LIST_LIMIT } from '../catalog/customer/compare-list';

// Giới hạn năm danh mục gốc ở khối đầu trang để giữ bố cục cân đối với thiết kế.
export const HERO_CATEGORY_LIMIT = 5;

// Đường dẫn chuẩn của các danh mục gốc dùng lại trên nhiều khối của trang chủ.
export const CATEGORY_SLUGS = {
  phone: 'dien-thoai',
  laptop: 'laptop',
  monitor: 'man-hinh',
  headphones: 'tai-nghe',
  accessory: 'phu-kien',
} as const;

// Hình minh họa, icon và sản phẩm đại diện của một danh mục gốc trên trang chủ.
export interface CategoryVisual {
  icon: IconName;
  // Đường dẫn trong `public/`. Hình lấy từ bản mẫu `shop-retail/assets/p/`, không có chữ hay logo hãng.
  artSrc: string | null;
  // Ảnh thật của sản phẩm này đứng trên ô danh mục. Sản phẩm bị xóa thì ô dùng sản phẩm mới nhất có ảnh.
  tileProductSlug: string | null;
}

// Danh mục do admin tạo sau này chưa có hình riêng. Trang vẫn hiện icon chung thay vì ô trống.
const FALLBACK_CATEGORY_VISUAL: CategoryVisual = {
  icon: 'package',
  artSrc: null,
  tileProductSlug: null,
};

// Sản phẩm của ô danh mục khác máy trên banner, để khách không thấy một ảnh hai lần liền nhau.
const CATEGORY_VISUALS: Record<string, CategoryVisual> = {
  [CATEGORY_SLUGS.phone]: {
    icon: 'smartphone',
    artSrc: '/media/categories/dien-thoai.svg',
    tileProductSlug: 'xiaomi-14t-pro',
  },
  [CATEGORY_SLUGS.laptop]: {
    icon: 'laptop',
    artSrc: '/media/categories/laptop.svg',
    tileProductSlug: 'asus-rog-zephyrus-g14-ga403',
  },
  [CATEGORY_SLUGS.monitor]: {
    icon: 'monitor',
    artSrc: '/media/categories/man-hinh.svg',
    tileProductSlug: 'asus-proart-pa278cv',
  },
  [CATEGORY_SLUGS.headphones]: {
    icon: 'headphones',
    artSrc: '/media/categories/tai-nghe.svg',
    tileProductSlug: 'bose-quietcomfort-45',
  },
  [CATEGORY_SLUGS.accessory]: {
    icon: 'plug',
    artSrc: '/media/categories/phu-kien.svg',
    tileProductSlug: 'logitech-mx-keys-s',
  },
};

export function getCategoryVisual(categorySlug: string): CategoryVisual {
  return CATEGORY_VISUALS[categorySlug] ?? FALLBACK_CATEGORY_VISUAL;
}

// Hiệu ứng nền của banner danh mục. Mỗi kiểu gợi đúng loại hàng: vệt sáng quét qua màn laptop, cột sóng âm cho tai nghe.
export type PromoEffect = 'shine' | 'equalizer';

export interface CategoryPromoContent {
  categorySlug: string;
  effect: PromoEffect;
  // Câu ngắn dưới tên danh mục. Chỉ nói điều catalog có thật, không hứa khuyến mãi.
  text: string;
  // Ảnh máy đứng trên banner. Sản phẩm bị xóa thì banner dùng sản phẩm mới nhất có ảnh của danh mục.
  productSlug: string;
}

// Cặp banner giữa trang, theo hai banner ngang của thegioididong.com sau khối deal.
export const CATEGORY_PROMOS: CategoryPromoContent[] = [
  {
    categorySlug: CATEGORY_SLUGS.laptop,
    effect: 'shine',
    text: 'Gaming, đồ họa và văn phòng',
    productSlug: 'asus-rog-zephyrus-g14-ga403',
  },
  {
    categorySlug: CATEGORY_SLUGS.headphones,
    effect: 'equalizer',
    text: 'Chống ồn, không dây, chụp tai và nhét tai',
    productSlug: 'sony-wh-1000xm5',
  },
];

// Hai máy cùng danh mục đặt cạnh nhau trên banner so sánh. Bảng thông số lấy từ API, không ghi cứng.
export const PRODUCT_DUEL = {
  leftSlug: 'apple-iphone-16-pro',
  rightSlug: 'samsung-galaxy-s24',
  // Số dòng thông số đặt cạnh nhau. Đủ để thấy điểm khác mà banner không dài như bảng so sánh.
  specRowLimit: 4,
};

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
    preferredProductSlug: PRODUCT_DUEL.leftSlug,
    requiresCategory: false,
  },
];
