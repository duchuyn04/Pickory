import { useState } from 'react';
import { Icon } from '../../../shared/icons';
import './product-image.css';

interface ProductImageProps {
  // Null hoặc rỗng nghĩa là sản phẩm chưa có ảnh, nên hiện khung giữ chỗ ngay.
  src: string | null | undefined;
  srcSet?: string;
  sizes?: string;
  // Chuỗi rỗng nghĩa là ảnh trang trí. Khung giữ chỗ của ảnh trang trí ẩn khỏi trình đọc màn hình.
  alt: string;
  loading?: 'lazy' | 'eager';
  fetchPriority?: 'high' | 'low' | 'auto';
  width?: number;
  height?: number;
  // Áp cho cả thẻ ảnh và khung giữ chỗ, để hai bên chiếm cùng một ô bố cục.
  className?: string;
}

// Ảnh sản phẩm có khung giữ chỗ. Khung hiện khi sản phẩm chưa có ảnh và khi trình duyệt không tải được ảnh.
export function ProductImage({
  src,
  srcSet,
  sizes,
  alt,
  className,
  ...imageProps
}: ProductImageProps) {
  // Nhớ địa chỉ đã lỗi thay vì cờ true/false. Đổi `src` thì địa chỉ mới khác địa chỉ lỗi nên ảnh được thử tải lại.
  const [failedSrc, setFailedSrc] = useState<string | null>(null);

  if (!src || failedSrc === src) {
    const placeholderClassName = className
      ? `product-image-placeholder ${className}`
      : 'product-image-placeholder';
    const accessibilityProps =
      alt === '' ? { 'aria-hidden': true } : { role: 'img', 'aria-label': alt };
    return (
      <div className={placeholderClassName} {...accessibilityProps}>
        <Icon name="image" className="icon product-image-placeholder-icon" />
      </div>
    );
  }

  return (
    <img
      src={src}
      srcSet={srcSet}
      sizes={sizes}
      alt={alt}
      className={className}
      onError={() => setFailedSrc(src)}
      {...imageProps}
    />
  );
}
