import type { IconName } from '../../shared/icons';

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
  'dien-thoai': {
    icon: 'smartphone',
    artSrc: '/media/categories/dien-thoai.svg',
    tileProductSlug: 'xiaomi-14t-pro',
  },
  laptop: {
    icon: 'laptop',
    artSrc: '/media/categories/laptop.svg',
    tileProductSlug: 'asus-rog-zephyrus-g14-ga403',
  },
  'man-hinh': {
    icon: 'monitor',
    artSrc: '/media/categories/man-hinh.svg',
    tileProductSlug: 'asus-proart-pa278cv',
  },
  'tai-nghe': {
    icon: 'headphones',
    artSrc: '/media/categories/tai-nghe.svg',
    tileProductSlug: 'bose-quietcomfort-45',
  },
  'phu-kien': {
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
    categorySlug: 'laptop',
    effect: 'shine',
    text: 'Gaming, đồ họa và văn phòng',
    productSlug: 'asus-rog-zephyrus-g14-ga403',
  },
  {
    categorySlug: 'tai-nghe',
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
