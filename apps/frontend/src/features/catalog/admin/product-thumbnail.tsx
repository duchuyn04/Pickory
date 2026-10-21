import { Icon } from '../../../shared/icons';
import { withCloudinaryTransform } from '../shared/image-url';
import type { ProductImageDetail } from './product-form';
import { firstProductImage, resolveImageAlt } from './product-image';

// Cỡ ảnh nhỏ trong bảng. Thẻ `img`, khung giữ chỗ và CSS dùng chung số này, để dòng không đổi cao khi ảnh tải xong.
const THUMBNAIL_SIZE = 48;
// Chiều rộng xin Cloudinary. Gấp hơn 4 lần cỡ hiển thị để ảnh vẫn nét trên màn có mật độ điểm ảnh cao.
const THUMBNAIL_SOURCE_WIDTH = 200;

interface ProductThumbnailProps {
  images: ProductImageDetail[];
  productName: string;
}

// Ảnh đầu tiên của sản phẩm trong bảng, hoặc khung giữ chỗ cùng cỡ khi sản phẩm chưa có ảnh.
export function ProductThumbnail({ images, productName }: ProductThumbnailProps) {
  const image = firstProductImage(images);

  // Khung giữ chỗ cùng cỡ ảnh, nên dòng có ảnh và dòng không ảnh thẳng hàng. `aria-label` là tên truy cập của khung.
  if (image === null) {
    return (
      <span className="product-thumb product-thumb-empty" role="img" aria-label="Chưa có ảnh">
        <Icon name="image" />
      </span>
    );
  }

  return (
    <img
      className="product-thumb"
      // Xin bản 200 px. Trình duyệt không tải ảnh gốc nặng cho ô 48 px.
      src={withCloudinaryTransform(image.url, THUMBNAIL_SOURCE_WIDTH)}
      alt={resolveImageAlt(image, productName)}
      // Cỡ cố định trong thẻ để trình duyệt giữ chỗ trước khi ảnh về. Bảng không xô dòng.
      width={THUMBNAIL_SIZE}
      height={THUMBNAIL_SIZE}
      // Ảnh dưới màn hình chỉ tải khi cuộn tới. Danh sách 20 dòng không tải 20 ảnh cùng lúc.
      loading="lazy"
      decoding="async"
    />
  );
}
