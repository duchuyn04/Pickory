import { withCloudinaryTransform } from '../../catalog/shared/image-url';
import { ProductImage } from '../../catalog/shared/product-image';
import type { CartLine } from './cart-api';

// Ảnh dòng giỏ có kích thước cố định, nên bố cục không nhảy khi ảnh về.
export function CartLineImage({ line, size }: { line: CartLine; size: number }) {
  // Cloudinary cấp ảnh gấp đôi chiều rộng hiển thị để rõ nét trên màn hình mật độ cao.
  const source = line.image ? withCloudinaryTransform(line.image.url, size * 2) : null;
  return (
    <ProductImage
      src={source}
      alt=""
      width={size}
      height={size}
      loading="lazy"
      className="cart-line-image"
    />
  );
}
