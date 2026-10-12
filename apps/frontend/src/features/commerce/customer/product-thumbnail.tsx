import { withCloudinaryTransform } from '../../catalog/shared/image-url';
import { ProductImage } from '../../catalog/shared/product-image';

interface ProductThumbnailProps {
  imageUrl?: string | null;
  size?: number;
  className?: string;
}

// Ảnh thu nhỏ sản phẩm dùng chung cho giỏ hàng và danh sách đơn hàng.
export function ProductThumbnail({ imageUrl, size = 64, className }: ProductThumbnailProps) {
  // Cloudinary cấp ảnh gấp đôi chiều rộng hiển thị để rõ nét trên màn hình mật độ cao.
  const source = imageUrl ? withCloudinaryTransform(imageUrl, size * 2) : null;
  return (
    <ProductImage
      src={source}
      alt=""
      width={size}
      height={size}
      loading="lazy"
      className={className}
    />
  );
}
