import { Link } from 'react-router-dom';
import type { CategoryNode } from '../catalog/customer/catalog-api';
import { withCloudinaryTransform } from '../catalog/shared/image-url';
import { SMALL_BANNERS, type SmallBannerContent } from './home-content';
import { useRailProducts } from './home-queries';
import { pickProductWithImage } from './pick-product';

// Ảnh trên banner nhỏ hiển thị ở 76px, tải rộng 160px để giữ độ nét trên màn mật độ cao.
const SMALL_BANNER_IMAGE_WIDTH = 160;
const SMALL_BANNER_DISPLAY_SIZE = 76;
const SKELETON_COUNT = 3;

interface SmallBannersProps {
  categories?: CategoryNode[];
  isPending?: boolean;
}

// Hàng ba banner nhỏ nằm ngay dưới banner nổi bật của trang chủ.
export function SmallBanners({ categories, isPending = false }: SmallBannersProps) {
  // Giữ ba khung chờ cùng chiều cao khi đang tải danh mục để tránh xê dịch nội dung bên dưới.
  if (isPending) {
    return (
      <div
        className="small-banners"
        role="region"
        aria-label="Danh mục và tính năng nổi bật"
        aria-hidden="true"
      >
        {Array.from({ length: SKELETON_COUNT }, (_, index) => (
          <div key={index} className="small-banner small-banner-skeleton" />
        ))}
      </div>
    );
  }

  const categorySlugs = new Set((categories ?? []).map((category) => category.slug));
  // Ẩn banner dẫn tới danh mục không tồn tại để khách không bấm vào trang lỗi.
  const visibleBanners = SMALL_BANNERS.filter(
    (banner) => !banner.requiresCategory || categorySlugs.has(banner.categorySlug),
  );

  if (visibleBanners.length === 0) return null;

  return (
    <div className="small-banners" role="region" aria-label="Danh mục và tính năng nổi bật">
      {visibleBanners.map((banner) => {
        // Nếu danh mục gốc không có trong cây thì mượn danh mục đầu tiên để tránh gọi API thừa.
        const imageCategorySlug = categorySlugs.has(banner.categorySlug)
          ? banner.categorySlug
          : (categories?.[0]?.slug ?? '');
        return (
          <SmallBannerCard key={banner.id} banner={banner} imageCategorySlug={imageCategorySlug} />
        );
      })}
    </div>
  );
}

interface SmallBannerCardProps {
  banner: SmallBannerContent;
  imageCategorySlug: string;
}

function SmallBannerCard({ banner, imageCategorySlug }: SmallBannerCardProps) {
  const railQuery = useRailProducts(imageCategorySlug, imageCategorySlug.length > 0);
  const product = pickProductWithImage(railQuery.data, banner.preferredProductSlug);

  return (
    <Link to={banner.to} className={`small-banner small-banner-${banner.theme}`}>
      <span className="small-banner-copy">
        <span className="small-banner-label">{banner.label}</span>
        <span className="small-banner-title">{banner.title}</span>
        <span className="small-banner-description">{banner.description}</span>
      </span>

      {/* Nếu danh mục chưa có ảnh sản phẩm thì ẩn khung ảnh và vẫn giữ đủ phần chữ. */}
      {product?.image && (
        <span className="small-banner-media" aria-hidden="true">
          <img
            src={withCloudinaryTransform(product.image.url, SMALL_BANNER_IMAGE_WIDTH, {
              trim: true,
            })}
            alt=""
            width={SMALL_BANNER_DISPLAY_SIZE}
            height={SMALL_BANNER_DISPLAY_SIZE}
            loading="lazy"
          />
        </span>
      )}
    </Link>
  );
}
